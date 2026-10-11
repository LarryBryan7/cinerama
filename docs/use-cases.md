# Casos de uso

Se detallan los casos de uso con más lógica de negocio o más flujos alternativos. Las operaciones CRUD simples (géneros, perfil) quedan cubiertas por sus historias de usuario.

---

## CU-01 Reservar asientos

| Campo | Valor |
|---|---|
| Actor principal | Cliente |
| Actores secundarios | Otros clientes (vía tiempo real), Sistema (liberación de bloqueos) |
| Historias | HU-08, HU-09, HU-10, HU-12, HU-16 |
| Reglas | RN-16, RN-18 a RN-28, RN-31 |
| Precondiciones | El cliente inició sesión. La función está programada y no comenzó. El dispositivo tiene conexión. |
| Postcondición de éxito | Existe una reserva `Confirmed` y sus asientos están en `Booked`. Los clientes conectados vieron el cambio. |
| Postcondición de fallo | No se crea ninguna reserva. Los bloqueos del cliente vencen o se liberan. |

**Flujo principal**

1. El cliente elige una función.
2. El sistema muestra el mapa de asientos con su estado actual y suscribe al cliente a los cambios de esa función.
3. El cliente selecciona uno o más asientos (o pide una recomendación, CU-01a).
4. El sistema bloquea los asientos por 5 minutos, notifica el cambio a los demás clientes y muestra la cuenta regresiva.
5. El cliente pasa al checkout y revisa asientos, precio y total.
6. El cliente confirma (pago simulado).
7. El sistema valida que los bloqueos sigan vigentes, crea la reserva, marca los asientos como `Booked` y notifica el cambio.
8. El sistema muestra el ticket con el QR.

**Flujos alternativos**

| ID | Paso | Condición | Respuesta del sistema |
|---|---|---|---|
| A1 | 4 | Al menos un asiento ya no está disponible (lo tomó otra persona, está vendido o hubo una carrera simultánea). | No bloquea ninguno (RN-21). Responde 409 con la lista de asientos en conflicto. La app los marca como no disponibles y conserva el resto de la selección para que el cliente la reintente. |
| A2 | 4 | El cliente ya tiene 10 asientos bloqueados. | 422 `lock_limit_exceeded`. |
| A3 | 3–5 | El cliente deselecciona un asiento. | Se libera ese bloqueo (RN-22) y se notifica. |
| A4 | 5–6 | La cuenta regresiva llega a cero. | La app vuelve al mapa e informa que la selección expiró. El servidor ya trata esos bloqueos como vencidos (RN-23). |
| A5 | 7 | El bloqueo venció en el servidor (reloj del dispositivo desfasado, o confirmación en el último segundo). | 409 `lock_expired`. No se crea la reserva. Se vuelve al paso 2. |
| A6 | 7 | Se pierde la respuesta de la confirmación (timeout). | La app reintenta con la misma `Idempotency-Key`. El servidor devuelve la reserva ya creada (RN-27). |
| A7 | 2–7 | Se pierde la conexión. | La app muestra el estado sin conexión y bloquea la acción de confirmar. Al reconectar, vuelve a pedir el mapa completo. |
| A8 | 4, 7 | La función ya comenzó. | 422 `sales_closed`. |

**CU-01a Recomendación**: el cliente indica la cantidad de personas. El sistema propone hasta 3 grupos de asientos contiguos ordenados por puntaje. El cliente acepta uno, lo que lleva al paso 4 con esos asientos, o lo ignora.

---

## CU-02 Cancelar reserva

| Campo | Valor |
|---|---|
| Actores | Cliente (su reserva) o Administrador (cualquiera) |
| Historias / Reglas | HU-15 / RN-30, RN-32 |
| Precondición | La reserva está `Confirmed`. |
| Postcondición | La reserva queda `Cancelled` con fecha de cancelación. Sus asientos vuelven a `Available` y se notifica el cambio. |

**Flujo principal**: el actor solicita la cancelación → el sistema verifica dueño y plazo → en una transacción marca la reserva, libera la relación con sus asientos (`ReleasedAt`) y deja los asientos `Available` → notifica.

**Alternativos**: reserva ajena → 404 (A1). Fuera de plazo → 422 (A2). Ya cancelada → 409 `booking_already_cancelled` (A3). Sin conexión → la app no envía la solicitud (A4).

---

## CU-03 Programar función

| Campo | Valor |
|---|---|
| Actor | Administrador |
| Historias / Reglas | HU-19 / RN-07, RN-12 a RN-15 |
| Postcondición | La función existe y tiene un `ShowSeat` `Available` por cada asiento activo de la sala. |

**Flujo principal**:
1. El administrador elige película, sala, fecha, hora y precio.
2. El sistema calcula el fin de la función.
3. En una transacción, verifica que no se solape con otra función de la sala, crea la función y genera sus asientos.
4. La función aparece en la cartelera.

**Alternativos**: solapamiento → 409 (A1). Película inactiva → 422 (A2). Fecha pasada → 422 (A3). Dos administradores programan funciones solapadas en el mismo instante → la transacción serializable hace que solo una se confirme (A4).

---

## CU-04 Mantener la sesión

| Campo | Valor |
|---|---|
| Actores | Cliente (app) o Administrador (panel web) |
| Historias / Reglas | HU-02, HU-03, HU-04 / RN-03, RN-05 |

**Flujo principal**:
1. El cliente envía una solicitud y recibe 401 `token_expired`.
2. El cliente pide un refresh. Solo hay un refresh en curso a la vez; las demás solicitudes esperan su resultado.
3. El servidor valida el refresh token, lo rota y entrega un access token nuevo.
4. El cliente repite las solicitudes pendientes.

**Diferencia por canal**: la app móvil envía el refresh token en el cuerpo y lo guarda en el almacenamiento seguro del sistema operativo. El panel web no lo ve nunca: viaja en una cookie `HttpOnly` (ver [ADR-006](adr/ADR-006-web-session-cookies.md)).

**Alternativos**: refresh vencido o revocado → cierre de sesión (A1). Refresh reutilizado → se revoca la cadena completa y se cierra la sesión (A2). Usuario desactivado → 401 y cierre de sesión (A3).

---

## CU-05 Consultar el ticket sin conexión

| Campo | Valor |
|---|---|
| Actor | Cliente |
| Historia | HU-22 |
| Precondición | El cliente abrió sus reservas al menos una vez con conexión en ese dispositivo. |

**Flujo principal**: el cliente abre "Mis reservas" → la app muestra los datos locales con la fecha de la última sincronización → el cliente abre el ticket → la app genera el QR a partir del contenido firmado que guardó localmente.

**Alternativos**: no hay datos locales → mensaje "Conéctate para descargar tus reservas" (A1). La reserva fue cancelada en el servidor y el dispositivo no se sincronizó → el ticket local sigue visible con un aviso "Última sincronización: …". El control de acceso valida el QR en línea y lo rechaza (A2). Este riesgo es aceptado y está documentado en [ADR-010](adr/ADR-010-mobile-offline-strategy.md).

---

## CU-06 Liberar bloqueos vencidos

| Campo | Valor |
|---|---|
| Actor | Sistema (proceso en segundo plano) |
| Requerimiento / Regla | RF-SYS-01 / RN-23 |

**Flujo principal**: cada 30 segundos, el proceso ejecuta `sp_ReleaseExpiredSeatLocks`. El procedimiento cambia a `Available` los asientos `Locked` vencidos y devuelve cuáles cambió. El proceso agrupa esos asientos por función y emite un evento por función.

**Aclaración clave**: la corrección del sistema **no depende** de este proceso. Si se detiene, los bloqueos vencidos igual se consideran disponibles al bloquear y al mostrar el mapa (RN-23). Lo único que se pierde es la notificación en tiempo real de esa liberación.

**Alternativos**: SQL Server no disponible → se registra el error y se reintenta en el siguiente ciclo (A1). Dos instancias ejecutan el proceso a la vez → cada fila la libera una sola instancia, porque el `UPDATE` es atómico, y por lo tanto no hay notificaciones duplicadas (A2).
