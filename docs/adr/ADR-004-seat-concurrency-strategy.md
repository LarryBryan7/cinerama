# ADR-004: Concurrencia de asientos con RowVersion, transacción e índice filtrado

- **Estado:** Aceptada
- **Fecha:** 2026-10-02

## Contexto

Dos clientes pueden intentar bloquear o confirmar el mismo asiento en el mismo milisegundo, incluso desde instancias distintas de la API (ADR-008). El resultado debe ser determinista: uno recibe éxito y el otro **409 Conflict**. Nunca puede haber una doble venta, ni siquiera ante un error de programación.

## Decisión

Tres barreras independientes, cada una suficiente para un tipo de fallo:

1. **Concurrencia optimista con `RowVersion`** en `ShowSeats`.
   - El bloqueo y la confirmación cargan las filas con tracking, validan la regla en el dominio (`ShowSeat.Lock(userId, now)`) y guardan con `SaveChangesAsync`.
   - EF Core genera `UPDATE ... WHERE Id = @id AND RowVersion = @original`. Si otra transacción modificó la fila, se actualizan 0 filas y se lanza `DbUpdateConcurrencyException`, que se traduce a 409 con la lista de asientos en conflicto.
2. **Transacción.** `SaveChanges` es atómico: si uno de los N asientos falla, no se bloquea ninguno (RN-21). La confirmación usa una transacción explícita porque además inserta `Booking` y `BookingSeats`.
3. **Índice único filtrado** `UX_BookingSeats_ShowSeat_Active ON BookingSeats(ShowSeatId) WHERE ReleasedAt IS NULL`. Aunque un error dejara pasar las dos barreras anteriores, SQL Server rechaza una segunda reserva activa del mismo asiento (RN-31). La violación se traduce también a 409.

Además, el `CHECK` de coherencia de estado (ver [database.md](../database.md)) impide combinaciones imposibles.

### Qué pasa cuando dos usuarios intentan reservar el mismo asiento

1. A y B leen F7 con `RowVersion = 0x01`.
2. Ambos ejecutan `UPDATE ... WHERE RowVersion = 0x01`. SQL Server serializa las dos escrituras sobre la fila.
3. La primera actualiza una fila, hace commit y la `RowVersion` pasa a `0x02`.
4. La segunda ya no encuentra `0x01`: actualiza 0 filas → excepción → rollback → **409**.
5. Después del commit, el ganador emite `SeatStatusChanged` y el mapa del perdedor se actualiza.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Lock distribuido en Redis (Redlock) | Agrega una segunda fuente de verdad que puede desincronizarse con la base (por ejemplo, si expira el lock de Redis o hay una pausa de GC). La base igual necesitaría su propia garantía. Ver ADR-008. |
| Bloqueo pesimista (`UPDLOCK, HOLDLOCK`) | Correcto, pero mantiene locks durante la transacción y no se integra con el seguimiento de cambios de EF Core. El optimista es más adecuado porque los conflictos son raros. |
| `UPDATE` condicional por estado (`WHERE Status = 'Available'`) con `ExecuteUpdate` | También es correcto y se usa para la liberación masiva. Para el bloqueo se prefiere la entidad con RowVersion porque la regla queda en el dominio (testeable sin base) y no en un `WHERE`. |
| Nivel `SERIALIZABLE` global | Bloqueos de rango y deadlocks innecesarios para operaciones que solo tocan filas por clave. Se reserva para el solapamiento de funciones (ver database.md). |

## Consecuencias

- La garantía de integridad depende solo de SQL Server. Redis y SignalR pueden fallar sin consecuencias para los datos.
- **Prueba automatizada obligatoria:** N clientes HTTP con usuarios distintos bloquean el mismo asiento a la vez (un `Barrier` sincroniza el arranque). El test verifica que hay exactamente un 200, N−1 respuestas 409 y una sola fila `Locked` en la base. Hay una variante para la confirmación y otra para un mismo usuario que hace doble toque (idempotencia, RN-27).
- El test corre contra SQL Server real (Testcontainers, ADR-012). Un proveedor en memoria no implementa `rowversion` ni transacciones reales y daría una falsa sensación de seguridad.

## Implementación (fase 2)

Cómo quedó construido, y la evidencia de que cada pieza hace su trabajo.

**Estructura**
- El **dominio** decide: `ShowSeat.TryLock`, `TryRelease` y `TryConfirm` reciben la hora como parámetro (nunca leen el reloj) y devuelven un resultado; el servicio de aplicación traduce ese resultado a HTTP. Se prueban sin base de datos (`ShowSeatTests`).
- **Todas** las transiciones de un asiento (bloquear, liberar, confirmar, cancelar) usan entidades con seguimiento y `RowVersion`. `ExecuteUpdate` solo se usa para revocar refresh tokens.
- Los asientos se cargan **siempre ordenados por `SeatId`**: dos solicitudes con asientos en común actualizan en el mismo orden y no se interbloquean. Un test las lanza con los mismos tres asientos en orden inverso, diez veces, y no hay ningún 500.
- La API identifica los asientos por su **id físico** (`Seat.Id`, el que muestra el mapa); `ShowSeats.Id` es interno y no sale de la API.

**El lock por usuario (RN-20).** El límite de 10 asientos es entre *varias solicitudes del mismo usuario*, y la `RowVersion` no lo protege (cada solicitud toca asientos distintos). Se serializan las solicitudes de un mismo usuario sobre una misma función con `sp_getapplock`, de modo `Transaction`: dura lo que la transacción, no deja nada que limpiar y no afecta a otros usuarios. Está detrás de `IAppDbContext.TryAcquireTransactionLockAsync`, así Application no contiene T-SQL.

**Un reintento, por un único caso.** Si la limpieza libera un bloqueo vencido justo mientras otro usuario lo toma, la `RowVersion` cambia y su escritura falla aunque el asiento haya quedado libre. Al recargar no hay ningún dueño: se reintenta una vez en lugar de devolver un 409 engañoso. Si al recargar sí hay un dueño, el 409 es verdadero y se informa qué asientos. Ambos casos se prueban de forma determinista (`RaceInjectionTests`).

**Violaciones de índices únicos.** `CineramaDbContext` traduce el error de SQL Server a `UniqueConstraintViolationException` (que hereda de `DbUpdateException`) con el nombre del índice. Cada servicio decide qué significa: `UX_BookingSeats_ShowSeat_Active` → `seat_unavailable`; `UX_Users_Email` → `email_already_registered`. Application no depende de SqlClient.

**Idempotencia (RN-27).** Misma clave y misma solicitud → se devuelve la reserva original (200). Misma clave con otro contenido → 422 `idempotency_key_reused`. Clave de más de 24 h → 409 `idempotency_key_expired`. La clave es por usuario. Si dos solicitudes con la misma clave compiten, la perdedora falla por los asientos o por el índice de la clave, vuelve a leer y devuelve la reserva de la ganadora.

**Evidencia (tests contra SQL Server real)**

| Qué se prueba | Cómo |
|---|---|
| 20 usuarios, mismo asiento, a la vez | Exactamente un `200` y diecinueve `409`; el asiento es de quien recibió el `200`; un solo aviso. Diez repeticiones. |
| Todo o nada con conjuntos que se solapan | Un solo ganador; el asiento exclusivo del perdedor queda libre. |
| 30 usuarios hacen el flujo completo (bloquear y confirmar) | Una sola reserva por asiento. |
| 40 usuarios eligen parejas al azar | Ningún asiento se vende dos veces; quien bloquea siempre logra confirmar. |
| Una misma persona confirma 8 veces a la vez con claves distintas | Una reserva; el resto, `409`. |
| Misma clave 5 veces a la vez | Todas reciben la misma reserva. |
| 4 cancelaciones simultáneas | Una cancelada, tres `409 booking_already_cancelled`, un solo aviso. |
| **Las tres barreras por separado** | La `RowVersion` y el índice único filtrado se activan inyectando la carrera de forma determinista; este último detiene la doble reserva **aun cuando la `RowVersion` no interviene**. |
| Invariantes al final de cada test | Ningún asiento en dos reservas activas; todo asiento vendido tiene exactamente una reserva activa. |

**Mutaciones.** Para comprobar que los tests no son decorativos, se rompió cada mecanismo a propósito y se midió qué tests fallan:

| Mutación | Tests que fallan | Qué demuestra |
|---|---|---|
| EF deja de enviar la `RowVersion` en el `WHERE` de cada `UPDATE` | 11 de 223 (10 de concurrencia, 1 de base de datos) | El **bloqueo** depende de la `RowVersion`: sin ella, 20 usuarios consiguen el mismo asiento. |
| Sin lock por usuario (cada solicitud toma un lock distinto) | 2 (el límite de 10 asientos y la prueba determinista) | El límite entre solicitudes simultáneas depende de ese lock. |
| Un bloqueo vencido sigue contando como vigente | 4 (mapa, bloqueo, confirmación) | RN-23 está cubierta de punta a punta. |

Un hallazgo útil de la primera mutación: **con la `RowVersion` rota, los tests de doble confirmación, de misma clave y de re-reserva tras cancelar siguen pasando**. No es un hueco de los tests: es la defensa en profundidad funcionando, porque en esos caminos el índice único filtrado de `BookingSeats` y el de la clave de idempotencia detienen la doble reserva por su cuenta. El bloqueo, en cambio, no tiene un índice único equivalente (un asiento `Locked` no es una fila nueva), así que ahí la `RowVersion` es la única barrera.

**Dos errores de método que conviene dejar escritos.** La primera vez que se rompió el lock por usuario, el test pasó: la mutación estaba mal hecha (ignoraba el resultado de `sp_getapplock` pero la llamada seguía tomando el lock). Y la primera medición de la mutación de `RowVersion` fallaba *todo* porque el cambio de modelo impedía que la API arrancara (EF detecta cambios pendientes del modelo al migrar), lo que habría hecho creer que los tests detectaban el problema. En ambos casos se rehizo la mutación hasta que fuera válida y se agregó una prueba determinista del lock por usuario.

## Motivación de portafolio

**Producto.** Es el problema central del dominio. La solución es la que se usaría en producción.
