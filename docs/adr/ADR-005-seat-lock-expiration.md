# ADR-005: Expiración de bloqueos en dos niveles, con BackgroundService y SignalR

- **Estado:** Aceptada
- **Fecha:** 2026-10-02

## Contexto

Un asiento bloqueado debe volver a estar disponible si la compra no se completa en 5 minutos (RN-19). Hay dos necesidades distintas:

1. **Corrección:** nadie debe quedar impedido de tomar un asiento cuyo bloqueo venció.
2. **Experiencia:** los demás clientes deberían ver el asiento libre en cuanto vence, sin recargar.

## Decisión

**Nivel 1, al consultar (corrección):** la regla de dominio considera disponible un asiento `Locked` con `LockedUntil < ahora` (RN-23). El bloqueo, la confirmación y el mapa aplican esta regla. **La corrección no depende de ningún proceso en segundo plano.**

**Nivel 2, limpieza (experiencia):** `SeatLockCleanupService : BackgroundService` funciona así:
- usa un `PeriodicTimer` de 30 segundos;
- en cada ciclo crea un scope con `IServiceScopeFactory`, porque el servicio es singleton y el `DbContext` es *scoped*;
- ejecuta `sp_ReleaseExpiredSeatLocks`, que es un `UPDATE ... OUTPUT` atómico;
- agrupa las filas liberadas por función y emite `SeatStatusChanged` por SignalR al grupo `showtime-{id}`.

**Tiempo real:** las notificaciones salen **después del commit**, a través de la interfaz `ISeatNotifier` definida en Application e implementada en Api con `IHubContext<SeatHub>`. Así nunca se notifica un cambio que terminó en rollback.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Solo el BackgroundService, sin la regla al consultar | Durante hasta 30 segundos (o mientras el servicio esté caído) un asiento vencido seguiría figurando como ocupado. La corrección dependería de un temporizador. |
| Solo la regla al consultar, sin BackgroundService | Correcto, pero los demás clientes no se enteran de la liberación hasta que recargan el mapa. |
| Job de SQL Server Agent | No existe en todas las ediciones (Express) y no puede emitir eventos de SignalR. |
| Expiración por TTL de Redis | Requeriría que Redis fuera la fuente de verdad del bloqueo (descartado en ADR-004 y ADR-008). |
| Polling del mapa en el cliente cada N segundos | Más tráfico y más latencia. SignalR ofrece push con grupos y reconexión. |

## Consecuencias

- Con varias instancias, todas ejecutan el proceso. No hace falta un lock distribuido: el `UPDATE` es atómico, cada fila la libera una sola instancia y cada instancia notifica solo lo que liberó (no hay eventos duplicados).
- El reloj se inyecta con `TimeProvider`. Los tests usan `FakeTimeProvider` para adelantar 5 minutos sin esperar.
- Un cliente que se reconecta pide el mapa completo por REST antes de aplicar eventos. SignalR no garantiza la entrega de mensajes emitidos mientras el cliente estaba desconectado.

## Implementación (fase 2)

- **`sp_ReleaseExpiredSeatLocks @NowUtc, @BatchSize`.** La hora la entrega la aplicación (su `TimeProvider`) y no `SYSUTCDATETIME()`: la aplicación y la base usan **un solo reloj** y los tests adelantan el tiempo sin esperar 5 minutos. El criterio es `LockedUntil <= @NowUtc`, el mismo que `ShowSeat.IsExpired`. Libera de a 500 filas y quien llama repite mientras el lote venga lleno (un test libera 600). Vive como archivo SQL embebido y la migración `AddSeatLockCleanupProcedure` lo aplica.
- **`SeatLockCleanupService`** usa `PeriodicTimer(period, TimeProvider)`: el primer tick ocurre a los 30 s (no al arrancar) y los tests lo disparan con `FakeTimeProvider.Advance`. Cada pasada crea su propio scope. Un fallo se registra y no detiene el servicio.
- **`Seating:CleanupEnabled`** apaga la limpieza. No es un interruptor "de producción": existe para **demostrar** que la corrección no depende de ella. Con la limpieza apagada y una hora de diferencia, otro usuario toma el asiento igual (`TheCleanupCanBeDisabled_AndTheSystemStaysCorrect`).
- **Avisos.** `ISeatNotifier` se llama **después del commit** y su fallo no revierte ni falla la operación. Un test comprueba el orden: el aviso lee la base desde otra conexión con `LOCK_TIMEOUT`, y si se enviara antes del commit esa lectura fallaría por la fila bloqueada. La implementación con SignalR llega en la fase 3; hasta entonces `NullSeatNotifier` no avisa a nadie.
- **Precisión.** Los instantes se truncan a milisegundos (`UtcNowMs`), la precisión de `datetime2(3)`. Un test lo detectó: la API devolvía `LockedUntil` con 7 decimales al crear y con 3 al consultar.

## Motivación de portafolio

**Producto.** El bloqueo temporal y el mapa en vivo son requisitos del producto. Las tecnologías (BackgroundService y SignalR) son las nativas de la plataforma para resolverlos.
