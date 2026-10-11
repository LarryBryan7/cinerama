# ADR-016: SignalR solo para avisar, con el token en la URL solo para el hub

- **Estado:** Aceptada
- **Fecha:** 2026-10-02

## Contexto

Dos personas miran el mismo mapa de asientos. Cuando una bloquea, libera o compra un asiento, la otra debería verlo sin recargar. El hub no puede convertirse en una segunda forma de escribir datos, no puede revelar quién tiene un asiento bloqueado y debe comportarse bien cuando la conexión se cae o el token vence.

## Decisión

1. **El hub solo escucha.** `SeatHub` tiene dos métodos, `JoinShowtime` y `LeaveShowtime`. Bloquear, liberar, reservar y cancelar pasan **siempre por REST**, que valida, persiste y decide. Un test recorre por reflexión los métodos públicos del hub y falla si aparece uno más.
2. **Avisos después del commit.** `ISeatNotifier` (Application) se invoca cuando la transacción ya terminó; la implementación (`SignalRSeatNotifier`, Api) envía al grupo. Un fallo al avisar se registra, no revierte ni rompe la operación (ADR-005).
3. **Un grupo por función:** `showtime-{id}`. Quien no se unió no recibe nada. Una conexión puede seguir como máximo 5 funciones (evita que un cliente cree grupos sin límite) y unirse dos veces es inofensivo.
4. **El evento no dice quién.** Lleva `showtimeId` y una lista de `{ seatId, status }`. Cada cliente conoce sus propios bloqueos por la respuesta de REST. Un test verifica la forma exacta del JSON.
5. **Sin repetición de mensajes.** SignalR no garantiza entregar lo emitido mientras el cliente estaba desconectado. Al reconectar, el cliente **vuelve a unirse al grupo y pide el mapa completo por REST**; el estado REST es la referencia. Mientras no hay conexión en vivo, la app consulta el mapa cada pocos segundos.
6. **Token del hub.** Los navegadores y los clientes WebSocket no pueden enviar `Authorization` en el *upgrade*, así que el token viaja como `?access_token=`, **solo para rutas `/hubs`**: la API REST lo ignora (un test lo prueba). El hub usa `CloseOnAuthenticationExpiration`: cuando el token vence, el servidor cierra la conexión y el cliente se reconecta con un token renovado.
7. **Sin detalles de error y con límites:** `EnableDetailedErrors = false`, mensajes de hasta 8 KB.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Que el hub reciba `LockSeat` / `BookSeats` | Duplica la validación y las reglas de autorización en dos puertas. REST ya está probado contra la concurrencia real (ADR-004). |
| Incluir el id del usuario en el evento | Expone datos de otras personas a todo el grupo (RN-34). |
| Guardar eventos para reenviarlos al reconectar | Complejidad y estado en el servidor para una garantía que REST ya da gratis: el mapa completo. |
| Sondeo (*polling*) únicamente | Más tráfico y más latencia. Se conserva solo como respaldo cuando SignalR no está conectado. |
| Server-Sent Events / WebSocket propio | SignalR ya resuelve grupos, reconexión y autenticación en la plataforma elegida. |

## Consecuencias

- **El token en la URL puede quedar en registros de proxies o balanceadores.** Es la limitación conocida de WebSockets. Mitigación: el access token dura 15 minutos y el nivel de registro de `Microsoft.AspNetCore` está en `Warning` (un test lo verifica), de modo que la API no registra la URL de la conexión. Un proxy delante de la API debería ocultar el parámetro en sus registros.
- Si SignalR cae, **todo el flujo sigue funcionando por REST** (test `SeatHubDownTests`); solo se pierde la inmediatez.
- Con más de una instancia, un cliente solo recibe lo que se emitió en *su* instancia, salvo que exista el backplane de la fase 6 (ADR-008). Hasta entonces la app compensa con la consulta periódica.
- En Flutter se usa `signalr_netcore`, un paquete **no oficial** con mantenimiento moderado. Se limita a WebSockets, la app nunca registra la URL del hub y la capa está detrás de la interfaz `SeatRealtime`, así que se puede reemplazar sin tocar los BLoC. Es un riesgo a revisar antes de producción.

## Evidencia (tests contra SQL Server real y un servidor HTTP real)

| Qué se prueba | Test |
|---|---|
| Dos clientes ven los cambios del otro; cancelar y la limpieza también avisan | `SeatHubTests` (TC-RT-01) |
| Aislamiento por función y salida del grupo | `EachShowtimeIsItsOwnGroup…`, `AfterLeaving…` (TC-RT-03) |
| El evento no contiene quién bloqueó | `TheEvent_CarriesOnlyShowtimeSeatAndStatus…` (RN-34) |
| Sin token, token basura, manipulado, de otra clave o vencido: rechazados | `SeatHubSecurityTests` (TC-RT-04) |
| Token vencido durante la conexión: el servidor cierra y no llegan más eventos | `WhenTheTokenExpiresWhileConnected…` (TC-RT-05) |
| El token en la URL solo lo acepta el hub | `ATokenInTheQueryString_IsOnlyAcceptedByTheHub…` |
| Reconexión: no hay repetición, se pierden los grupos y REST tiene la verdad | `SeatHubReconnectionTests` (TC-RT-06) |
| Avisos después del commit; operaciones fallidas no avisan | `SeatHubCommitOrderTests` (TC-RT-07) |
| SignalR caído: el flujo completo sigue por REST | `SeatHubDownTests` (TC-RT-08) |
| Ningún secreto ni token en registros ni respuestas | `SecretsNeverLeakTests` (TC-SEC-13) |

**Mutaciones.** Se rompió cada mecanismo y se midió qué tests fallan (de 25 de tiempo real):

| Mutación | Tests que fallan |
|---|---|
| El hub pasa a `[AllowAnonymous]` | 5 (todos los de rechazo de conexión, TC-RT-04) |
| Avisar a todos los clientes en lugar de al grupo de la función | 3 (aislamiento por función, salida del grupo y reconexión) |
| Aceptar `?access_token=` en cualquier ruta, no solo `/hubs` | 1 (`ATokenInTheQueryString_IsOnlyAcceptedByTheHub…`) |
| Quitar `CloseOnAuthenticationExpiration` | 1 (TC-RT-05) |

Un hallazgo útil: **quitar solo `[Authorize]` del hub no hace fallar ningún test.** No es un hueco de los tests: la API tiene una política de autorización por defecto que exige usuario autenticado en cualquier endpoint (TC-SEC-11), y el hub la hereda. Es defensa en profundidad: hay dos barreras y cada una por sí sola basta. Por eso la mutación válida es `[AllowAnonymous]`, que sí rompe ambas.

## Motivación de portafolio

**Producto.** El mapa en vivo es un requisito (RF-SEAT-05). SignalR es la solución nativa de ASP.NET Core. Las decisiones de seguridad (hub que solo escucha, token solo en `/hubs`, evento sin identidades) son las que se tomarían en producción.
