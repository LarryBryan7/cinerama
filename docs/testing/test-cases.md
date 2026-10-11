# Casos de prueba

**Tipos de ejecución:** *Unit* (unitario automatizado) · *Int.* (integración automatizada) · *Manual* · *Newman* (colección Postman).

**Datos base** (seed de prueba): cliente A `cliente1@cinerama.test`, cliente B `cliente2@cinerama.test`, administrador `admin@cinerama.test`, y la función 42 en la Sala 2, que empieza en 5 horas, con todos los asientos disponibles y precio 18.00.

---

## Reserva y asientos

### TC-BOOK-01 Reserva exitosa
- **Objetivo:** verificar el flujo completo bloqueo → confirmación.
- **Trazabilidad:** HU-09, HU-12 · RN-18, RN-24, RN-25, RN-28 · *Int.* + *Manual*
- **Precondiciones:** cliente A autenticado; F7 y F8 disponibles en la función 42.
- **Pasos:**
  1. `POST /showtimes/42/seats/lock` con F7 y F8.
  2. `POST /bookings` con `showtimeId=42`, F7, F8 y una `Idempotency-Key` nueva.
  3. `GET /showtimes/42/seats`.
- **Resultado esperado:**
  - Paso 1 → 200 con `lockedUntil` ≈ ahora + 5 min.
  - Paso 2 → 201, `status=Confirmed`, `totalAmount=36.00`, código de 8 caracteres sin 0/O/1/I.
  - Paso 3 → F7 y F8 en `Booked`.
  - En la base, cada `BookingSeat` tiene `Price=18.00` y `ReleasedAt` en NULL.

### TC-SEAT-01 Asiento ya vendido
- **Objetivo:** un asiento `Booked` no puede bloquearse.
- **Trazabilidad:** HU-09 · RN-18 · *Int.*
- **Precondiciones:** F7 vendido al cliente B.
- **Pasos:** el cliente A ejecuta `POST /showtimes/42/seats/lock` con F7.
- **Resultado esperado:** 409, `code=seat_unavailable`, `conflicts` contiene F7. F7 sigue `Booked`.

### TC-SEAT-02 Asiento bloqueado por otro cliente
- **Objetivo:** un bloqueo vigente ajeno impide bloquear.
- **Trazabilidad:** HU-09 · RN-18 · *Int.*
- **Precondiciones:** el cliente B bloqueó F7 hace 1 minuto.
- **Pasos:** el cliente A bloquea F7.
- **Resultado esperado:** 409 `seat_unavailable`. `LockedByUserId` sigue siendo B y `LockedUntil` no cambió.

### TC-SEAT-03 Todo o nada
- **Objetivo:** si un asiento falla, no se bloquea ninguno.
- **Trazabilidad:** HU-09 · RN-21 · *Int.*
- **Precondiciones:** F7 bloqueado por B; F8 disponible.
- **Pasos:** el cliente A bloquea F7 y F8.
- **Resultado esperado:** 409 con `conflicts=[F7]`. **F8 sigue `Available`.**

### TC-SEAT-04 Bloqueo vencido disponible antes de la limpieza
- **Objetivo:** la corrección no depende del BackgroundService.
- **Trazabilidad:** HU-08 · RN-23 · *Int.* (con `FakeTimeProvider` y el servicio de limpieza deshabilitado)
- **Precondiciones:** B bloqueó F7. El reloj avanza 5 min 1 s. El proceso de limpieza **no** corrió.
- **Pasos:**
  1. `GET /showtimes/42/seats` como A.
  2. A bloquea F7.
- **Resultado esperado:** paso 1 → F7 aparece `Available`. Paso 2 → 200, y F7 queda bloqueado por A.

### TC-SEAT-05 Liberación automática de bloqueos vencidos
- **Objetivo:** el proceso libera y notifica.
- **Trazabilidad:** RF-SYS-01 · RN-23 · *Int.*
- **Precondiciones:** 3 asientos de la función 42 bloqueados y vencidos; 1 asiento bloqueado y vigente.
- **Pasos:** ejecutar un ciclo de `SeatLockCleanupService`.
- **Resultado esperado:** los 3 vencidos pasan a `Available` con los campos de bloqueo en NULL; el vigente no cambia. `ISeatNotifier` recibe **un** evento para la función 42 con exactamente 3 cambios.

### TC-SEAT-06 Límite de 10 asientos
- **Trazabilidad:** HU-09 · RN-20 · *Int.*
- **Precondiciones:** A tiene 10 asientos bloqueados en la función 42.
- **Pasos:** A bloquea un asiento más.
- **Resultado esperado:** 422 `lock_limit_exceeded`. Siguen siendo 10.

### TC-SEAT-07 Volver a bloquear no extiende el plazo
- **Trazabilidad:** RN-19 · *Unit* + *Int.*
- **Precondiciones:** A bloqueó F7 a las T0.
- **Pasos:** a T0 + 3 min, A vuelve a bloquear F7.
- **Resultado esperado:** 200, y `lockedUntil` sigue siendo T0 + 5 min.

### TC-SEAT-08 Liberar un bloqueo ajeno
- **Trazabilidad:** HU-10 · RN-22 · *Int.*
- **Pasos:** B bloquea F7; A llama a `POST /showtimes/42/seats/release` con F7.
- **Resultado esperado:** 404 (A no tiene ese bloqueo). F7 sigue bloqueado por B.

### TC-SEAT-09 Venta cerrada
- **Trazabilidad:** RN-16 · *Int.*
- **Precondiciones:** función 43 iniciada hace 1 minuto.
- **Pasos:** A bloquea un asiento de la 43.
- **Resultado esperado:** 422 `sales_closed`.

### TC-SEAT-10 Función inexistente
- **Trazabilidad:** HU-07 · *Int.* + *Newman*
- **Pasos:** `GET /showtimes/999999/seats` y `POST /showtimes/999999/seats/lock`.
- **Resultado esperado:** 404 en ambos, con ProblemDetails y sin stack trace.

### TC-BOOK-02 Confirmar con el bloqueo vencido
- **Trazabilidad:** HU-12 · RN-24 · *Int.*
- **Precondiciones:** A bloqueó F7 y pasaron 5 min 1 s. B bloqueó F7 después.
- **Pasos:** A confirma con F7.
- **Resultado esperado:** 409 `lock_expired`. No se crea ninguna reserva. F7 sigue bloqueado por B.

### TC-BOOK-03 Idempotencia
- **Trazabilidad:** HU-12 · RN-27 · *Int.*
- **Pasos:**
  1. A confirma con la clave `k-1`.
  2. A repite exactamente la misma solicitud con `k-1`.
- **Resultado esperado:** paso 1 → 201. Paso 2 → 200 con el **mismo** `id` y `code`. En la base hay una sola reserva.

### TC-BOOK-04 Cancelación a tiempo
- **Trazabilidad:** HU-15 · RN-30 · *Int.* + *Manual*
- **Precondiciones:** A tiene una reserva con F7 y F8 en una función que empieza en 5 horas.
- **Pasos:**
  1. `POST /bookings/{id}/cancel`.
  2. `GET /showtimes/42/seats`.
- **Resultado esperado:**
  - Paso 1 → 200, `status=Cancelled`, `cancelledAt` informado.
  - Paso 2 → F7 y F8 en `Available`.
  - En la base, los `BookingSeats` tienen `ReleasedAt` informado.
  - Otro cliente puede reservar F7 sin violar el índice único filtrado.

### TC-BOOK-05 Cancelación fuera de plazo
- **Trazabilidad:** HU-15 · RN-30 · *Int.*
- **Precondiciones:** la función empieza en 90 minutos.
- **Pasos:** A cancela su reserva.
- **Resultado esperado:** 422 `cancellation_window_closed`. La reserva sigue `Confirmed`.

### TC-BOOK-06 Cancelar dos veces
- **Trazabilidad:** CU-02 A3 · *Int.*
- **Resultado esperado:** la segunda cancelación responde 409 `booking_already_cancelled`.

## Concurrencia

### TC-CONC-01 Dos clientes, mismo asiento, mismo instante (bloqueo)
- **Objetivo:** demostrar RNF-01 bajo concurrencia real.
- **Trazabilidad:** HU-09 · RN-18, RN-31 · ADR-004 · *Int.*
- **Precondiciones:** 20 usuarios distintos autenticados, cada uno con su propio `HttpClient`. F7 disponible.
- **Pasos:**
  1. Los 20 esperan en un `Barrier`.
  2. Todos envían a la vez `POST /showtimes/42/seats/lock` con F7.
  3. Se repite el escenario 10 veces con asientos distintos.
- **Resultado esperado:** en cada repetición hay **exactamente un 200** y 19 respuestas 409. En la base, F7 está `Locked` con `LockedByUserId` igual al usuario que recibió 200. Ninguna respuesta es 500.

### TC-CONC-02 Dos confirmaciones simultáneas sobre el mismo asiento
- **Trazabilidad:** RN-24, RN-31 · *Int.*
- **Precondiciones:** se fuerza un estado en el que dos solicitudes compiten por confirmar F7 (A confirma mientras el bloqueo de A vence y B lo toma).
- **Resultado esperado:** como máximo una reserva activa contiene F7. La otra solicitud recibe 409. El índice único filtrado nunca se viola sin traducirse a 409.

### TC-CONC-03 Doble toque del mismo usuario al confirmar
- **Trazabilidad:** RN-27 · *Int.*
- **Pasos:** A envía dos `POST /bookings` simultáneos con la misma `Idempotency-Key`.
- **Resultado esperado:** ambas respuestas devuelven la misma reserva (201 y 200, en cualquier orden). Hay una sola reserva en la base.

## Seguridad

### TC-SEC-01 Token inválido
- **Trazabilidad:** RN-34 · *Int.* + *Newman*
- **Pasos:** `GET /bookings` con (a) sin token, (b) un token con la firma alterada, (c) un token firmado con otra clave, (d) un token con `alg: none`.
- **Resultado esperado:** 401 en todos los casos. Ninguno revela detalles de la validación.

### TC-SEC-02 Token vencido
- **Trazabilidad:** HU-03 · RN-05 · *Int.*
- **Pasos:** usar un access token con `exp` en el pasado.
- **Resultado esperado:** 401 con `code=token_expired`.

### TC-SEC-03 Usuario sin permisos (Customer en ruta de Admin)
- **Trazabilidad:** HU-17 · RN-33 · *Int.* + *Newman*
- **Pasos:** el cliente A llama a `POST /movies`, `GET /users`, `GET /reports/summary` y `PUT /cinema-rooms/1/layout`.
- **Resultado esperado:** 403 en todos. No hay cambios en la base.

### TC-SEC-04 IDOR: leer una reserva ajena
- **Trazabilidad:** HU-14 · RN-32 · *Int.*
- **Precondiciones:** la reserva R pertenece a B.
- **Pasos:** A ejecuta `GET /bookings/{R}`.
- **Resultado esperado:** **404** (no 403). La respuesta es idéntica a la de un id inexistente.

### TC-SEC-05 IDOR: cancelar una reserva ajena
- **Trazabilidad:** HU-15 · RN-32 · *Int.*
- **Pasos:** A ejecuta `POST /bookings/{R de B}/cancel`.
- **Resultado esperado:** 404. R sigue `Confirmed` y sus asientos siguen `Booked`.

### TC-SEC-06 Listado limitado al dueño
- **Trazabilidad:** HU-13 · RN-32, RN-34 · *Int.*
- **Pasos:** A ejecuta `GET /bookings?userId={B}` (un parámetro que no existe en el contrato).
- **Resultado esperado:** 200 solo con las reservas de A. El parámetro se ignora.

### TC-SEC-07 Reutilización de un refresh token
- **Trazabilidad:** HU-03 · RN-05 · ADR-007 · *Int.*
- **Pasos:**
  1. Login → R1.
  2. Refresh con R1 → R2.
  3. Refresh con R1 otra vez.
  4. Refresh con R2.
- **Resultado esperado:** paso 3 → 401 `refresh_token_reused`. Paso 4 → 401, porque se revocó toda la familia.

### TC-SEC-08 Usuario desactivado
- **Trazabilidad:** HU-20 · RN-03 · *Int.*
- **Pasos:** el administrador desactiva a A; luego A intenta un refresh y un login.
- **Resultado esperado:** 401 en ambos.

### TC-SEC-09 Cookie de refresh del panel web
- **Trazabilidad:** ADR-006 · RNF-05 · *Int.* + *Manual*
- **Pasos:** login del administrador con `useCookie=true`.
- **Resultado esperado:**
  - El cuerpo no contiene `refreshToken`.
  - `Set-Cookie` tiene `HttpOnly`, `Secure`, `SameSite=Strict` y `Path=/api/v1/auth`.
  - En DevTools, `localStorage` y `sessionStorage` no contienen tokens.

### TC-SEC-10 CORS
- **Trazabilidad:** RNF-06 · *Int.*
- **Pasos:** preflight `OPTIONS /api/v1/auth/refresh` desde (a) el origen configurado y (b) `https://evil.example`.
- **Resultado esperado:** (a) `Access-Control-Allow-Origin` igual al origen y `Allow-Credentials: true`. (b) Sin encabezados CORS.

### TC-SEC-11 Seguridad por defecto
- **Trazabilidad:** RNF-03 · *Int.*
- **Pasos:** recorrer por reflexión todos los endpoints registrados y llamarlos sin token.
- **Resultado esperado:** 401 en todos, salvo la lista explícita de públicos (auth, cartelera, funciones, health).

## Tiempo real

### TC-RT-01 Evento a otro cliente
- **Trazabilidad:** HU-16 · RF-SEAT-05 · *Int.* + *E2E (emulador)*
- **Pasos:** B se une al grupo de la función 42; A bloquea F7 por REST.
- **Resultado esperado:** B recibe `SeatStatusChanged` con F7 en `Locked` en menos de 2 s. El evento no contiene ids de usuario. Lo mismo ocurre al liberar, reservar, cancelar y cuando la limpieza libera un bloqueo vencido. En el emulador, el mapa de la app muestra el bloqueo ajeno y luego la liberación sin recargar.

### TC-RT-02 Dos instancias, con y sin backplane
- **Trazabilidad:** RNF-09 · ADR-008 · *Manual* (script de demo, fase 6)
- **Pasos:** B se conecta a `api-2` y A bloquea F7 a través de `api-1`. Se repite con `SIGNALR__USEREDIS=false` y `true`.
- **Resultado esperado:** sin backplane, B **no** recibe el evento y, si intenta bloquear F7, recibe 409. Con backplane, B recibe el evento.

### TC-RT-03 Aislamiento por función
- **Trazabilidad:** ADR-016 · *Int.*
- **Pasos:** B está en la función 42 y C en la 43; A bloquea un asiento de la 42. Después B sale del grupo y A bloquea otro.
- **Resultado esperado:** C no recibe nada; B deja de recibir al salir. Unirse a una función inexistente se rechaza; una conexión admite como máximo 5 funciones.

### TC-RT-04 Conexión no autenticada
- **Trazabilidad:** ADR-016 · *Int.*
- **Pasos:** conectar sin token, con basura, con un token manipulado, firmado con otra clave y vencido.
- **Resultado esperado:** `401` en todos los casos. Con el token vencido, la negociación indica el motivo (`token_expired`) para que el cliente lo renueve.

### TC-RT-05 Token que vence con la conexión abierta
- **Trazabilidad:** ADR-016 · *Int.*
- **Pasos:** conectar con un token de pocos segundos y esperar a que venza.
- **Resultado esperado:** el servidor cierra la conexión y, a partir de ahí, no llegan más eventos.

### TC-RT-06 Reconexión
- **Trazabilidad:** ADR-016 · ADR-005 · *Int.* + *Unit (BLoC)*
- **Pasos:** un cliente se desconecta, ocurren cambios y se reconecta.
- **Resultado esperado:** los avisos perdidos **no** se repiten y los grupos se pierden; `GET /showtimes/{id}/seats` devuelve la verdad completa. La app vuelve a unirse al grupo y reemplaza su mapa con el de REST.

### TC-RT-07 Avisos solo después del commit
- **Trazabilidad:** ADR-005 · *Int.*
- **Resultado esperado:** cuando llega un aviso, la base ya contiene el cambio (se lee desde otra conexión). Una operación que falla (409, validación) no emite nada.

### TC-RT-08 SignalR caído
- **Trazabilidad:** ADR-016 · *Int.*
- **Pasos:** el notificador falla en cada llamada; se ejecuta el flujo completo (bloquear, reservar, cancelar).
- **Resultado esperado:** todas las operaciones terminan con éxito y el fallo solo se registra. REST sigue siendo la fuente de verdad.

### TC-SEC-13 Los secretos no se filtran
- **Trazabilidad:** ADR-016 · ADR-014 · *Int.*
- **Resultado esperado:** ni el token de acceso, ni el refresh token, ni `Jwt:SigningKey`, ni `Ticket:SigningKey` aparecen en los registros ni en ninguna respuesta (incluidos errores). El `ticketPayload` expone la firma, nunca la clave.

## Catálogo y administración

### TC-ADM-01 Solapamiento de funciones
- **Trazabilidad:** HU-19 · RN-13 · *Int.*
- **Pasos:** crear una función en la Sala 1 de 18:00 a 20:15 (con limpieza) y luego otra en la misma sala a las 20:00.
- **Resultado esperado:** 409 `showtime_overlap`.

### TC-ADM-02 Generación de asientos de función
- **Trazabilidad:** RN-15 · *Int.*
- **Resultado esperado:** una función en una sala de 130 asientos activos genera 130 `ShowSeats` en `Available`.

### TC-ADM-03 Las rutas de administración exigen el rol
- **Trazabilidad:** RN-33 · *Int.*
- **Pasos:** llamar cada endpoint de administración (catálogo, cines, salas, funciones, reservas, usuarios y reportes) sin token y con la cuenta de un cliente.
- **Resultado esperado:** `401` sin token, `403` con un cliente, en todos.

### TC-ADM-04 Películas: alta, baja lógica y reservas activas
- **Trazabilidad:** HU-17 · RN-08, RN-09, RN-51 · *Int.*
- **Resultado esperado:** al crear una película con géneros y un tráiler con adornos (`&t=30s`) se guarda el enlace limpio `watch?v=código` y aparece en la cartelera pública; darla de baja la oculta pero conserva su historial; si tiene funciones futuras con reservas confirmadas la baja se rechaza con `409 movie_has_active_bookings` y no cambia nada.

### TC-ADM-05 Layout de una sala
- **Trazabilidad:** HU-18 · RN-10, RN-11 · *Int.*
- **Resultado esperado:** un layout con pasillo central genera los 130 asientos esperados y devuelve el pasillo como `emptyCells`; con funciones futuras ni el layout ni la desactivación proceden (`409 room_has_future_showtimes`) y nada cambia.

### TC-ADM-06 Cancelar una función con reservas
- **Trazabilidad:** HU-19 · RN-17 · *Int.*
- **Resultado esperado:** la función queda `Cancelled`, sus reservas confirmadas pasan a `Cancelled`, los asientos y bloqueos vuelven a `Available` y se avisa a los clientes conectados.

### TC-ADM-07 Dos administradores programan a la vez
- **Trazabilidad:** RN-13 · ADR-004 · *Int.*
- **Pasos:** 2 y luego 8 administradores crean a la vez funciones que se traslapan en la misma sala.
- **Resultado esperado:** exactamente un `201`; el resto `409 showtime_overlap` o `concurrency_conflict`. Nunca dos funciones solapadas.

### TC-WEB-01 Sesión del panel
- **Trazabilidad:** RN-60 · ADR-006, ADR-018 · *Vitest*
- **Resultado esperado:** sin sesión una ruta protegida lleva al login y al entrar se vuelve a ella; con la cookie de refresh vigente la sesión se recupera al cargar; una cuenta de cliente no entra y su sesión recién abierta se revoca; varias solicitudes que reciben `401 token_expired` a la vez comparten **un solo** refresh; si el refresh falla se vuelve al login; el access token nunca se escribe en `localStorage` ni `sessionStorage`.

### TC-WEB-02 Funciones desde el panel
- **Trazabilidad:** HU-19 · RN-13, RN-56, RN-59 · *Vitest*
- **Resultado esperado:** la hora escrita se envía como UTC según la zona del cine aunque el navegador esté en otra (las pruebas corren en `Asia/Tokyo`); un traslape o una fecha anterior al estreno aparecen junto al campo y el formulario sigue abierto; editar queda deshabilitado si hay reservas; cancelar advierte cuántas reservas confirmadas se cancelarán **según un dato consultado al abrir el diálogo** (la lista puede estar vieja) y no deja confirmar hasta tenerlo, ni llama a la API hasta confirmar.

### TC-ADM-08 Los decimales de los reportes no se truncan
- **Trazabilidad:** RN-36, RN-37 · *Int.*
- **Pasos:** dos funciones de 7 asientos a 3,33; en una se venden 3 entradas.
- **Resultado esperado:** ocupación promedio `0.214286` (6 decimales, no `0.21`), ingresos y ticket promedio `9.99`.

### TC-WEB-03 Reservas, usuarios y salas desde el panel
- **Trazabilidad:** RN-30, RN-39, RN-11 · *Vitest*
- **Resultado esperado:** solo se cancela una reserva confirmada de una función que no empezó; no se puede desactivar la propia cuenta; desactivar pide confirmación y avisa que cierra sesiones; el editor de layout se maneja con teclado (flechas), queda bloqueado con funciones futuras y conserva lo editado si la API responde `409`.

### TC-WEB-04 Formularios y reportes del panel
- **Trazabilidad:** RN-57, RN-58 · *Vitest*
- **Resultado esperado:** los formularios validan antes de llamar a la API y muestran el error de campo del servidor (`validation_failed`, claves en PascalCase o camelCase); un rango de reporte inválido no se aplica; un periodo sin ventas muestra un estado vacío; si un reporte falla, solo esa sección muestra el error.

## Estreno, preventa, cines y opiniones (fase 3.5)

### TC-PRE-01 Estado de estreno
- **Trazabilidad:** RF-CAT-05 · RN-41 · *Unit* + *Int.*
- **Pasos:** crear películas que se estrenaron ayer, hoy, en 3 días con preventa abierta, en 3 días con la preventa abriendo mañana, y en 30 días; pedir la cartelera con `status=nowShowing`, `presale` y `comingSoon`.
- **Resultado esperado:** "estrena hoy" ya es cartelera; la de preventa abierta sale solo en preventa; las otras dos, en próximamente. Preventa y próximamente se ordenan por fecha de estreno. Un estado inválido responde 400 con el campo `status`.

### TC-PRE-02 Reservar en preventa
- **Trazabilidad:** RF-BOOK-06 · RN-42 · *Int.*
- **Pasos:** (a) bloquear y confirmar un asiento de una película en preventa; (b) bloquear y confirmar uno de una película cuya preventa abre mañana o que está "próximamente".
- **Resultado esperado:** (a) 200 y 201. (b) 422 `sales_not_open`, **sin ningún cambio** en la base (ni asiento bloqueado ni reserva) y sin avisos de tiempo real.

### TC-CIN-01 Cines y ciudades
- **Trazabilidad:** RF-CAT-06 · RN-49, RN-50 · *Int.*
- **Resultado esperado:** `/cities` lista cada ciudad una vez, con su cantidad de cines activos. Cada película lista solo los cines donde tiene funciones próximas (sin repetirlos). El filtro `city` deja las películas de esa ciudad y no oculta las "próximamente". Las funciones de un cine inactivo no se listan y no se pueden vender (422 `cinema_closed`).

### TC-REV-01 Calificación y opinión destacada
- **Trazabilidad:** RF-CAT-07 · RN-44, RN-45 · *Int.*
- **Resultado esperado:** el promedio sale con un decimal (4,33… → 4,3); sin opiniones es `null`. La destacada es la más reciente **con comentario**. Ninguna respuesta contiene el nombre completo ni el email de un autor.

### TC-REV-02 Publicar, cambiar y borrar la opinión propia
- **Trazabilidad:** RF-REV-02 · RN-43, RN-46 · *Int.*
- **Pasos:** sin sesión, `PUT /movies/{id}/reviews/mine`; con sesión, publicar dos veces; probar puntuación 0, 6 y -1, comentario de 501 caracteres, con carácter de control, vacío y con marcado HTML; opinar sobre una película en preventa; borrar dos veces.
- **Resultado esperado:** 401 sin sesión. 201 la primera vez y 200 la segunda, **una sola fila**, misma fecha de creación. 400 para lo inválido. El comentario vacío se guarda como nada; el HTML se guarda como texto plano. 422 `movie_not_released` si no se estrenó. Borrar: 204 y luego 404 `review_not_found`.

### TC-REV-03 Carrera de primeras opiniones
- **Trazabilidad:** RN-43 · *Int.* (determinista)
- **Pasos:** justo antes de guardar la opinión de Ana, otra solicitud inserta la suya (otra conexión).
- **Resultado esperado:** 200 (no 500), una sola fila y gana la última escritura.

### TC-SEC-14 Nadie opina ni borra como otra persona
- **Trazabilidad:** RN-34 · *Int.*
- **Resultado esperado:** un `userId` o `authorName` en el cuerpo se ignora; `DELETE …/mine` solo toca la opinión propia (otra persona recibe 404 y la ajena no cambia).

### TC-OPS-03 Límite de escrituras de opiniones
- **Trazabilidad:** RN-47 · *Int.*
- **Resultado esperado:** al superar el cupo por minuto, 429 `rate_limited`; el cupo es por usuario y leer no cuenta.

### TC-DATA-01 Los datos de demostración están completos
- **Trazabilidad:** RF-CAT-05 a RF-CAT-07 · [ui-data-coverage.md](../ui-data-coverage.md) · *Int.*
- **Resultado esperado:** las 9 películas tienen título, sinopsis, duración, clasificación, géneros, fecha de estreno y **un póster cuyo archivo existe**. El texto importado va en minúsculas con la primera letra en mayúscula, y la clasificación es "+14" o "Todo espectador". El estado concuerda con las fechas (hoy: 8 en cartelera y 1 en preventa); las que se pueden reservar tienen funciones y cines (y ninguna función antes del estreno). Cada función empieza a una hora que Cinerama publica y ninguna sala tiene dos funciones a la vez. Cada película estrenada tiene entre 8 y 20 opiniones, un promedio entre 1 y 5 y una opinión destacada con el autor abreviado. El cine tiene la dirección real, coordenadas reales de Chimbote y sus 7 salas. Ninguna opinión es anterior al estreno ni del futuro.

### TC-UI-01 Inicio sin sesión
- **Trazabilidad:** CR-01 · *Unit (router)* + *Widget* + *E2E (emulador)*
- **Resultado esperado:** al abrir la app sin sesión se ve la cartelera, no el login. Elegir una función, "Entradas" o "Perfil" piden iniciar sesión y, al terminar, vuelven a donde iba la persona. Cerrar sesión lleva a la cartelera.

### TC-UI-02 Tarjeta de cartelera con datos reales
- **Trazabilidad:** RF-CAT-07 · *Widget*
- **Resultado esperado:** la tarjeta muestra la calificación, la opinión destacada, los cines y la sinopsis **de esa película**, y ningún dato de maqueta. Una película "próximamente" no muestra cines y su botón solo abre el detalle. Las pestañas siguen visibles aunque una esté vacía.

### TC-UI-03 Carrusel de tarjetas
- **Trazabilidad:** RF-CAT-07 · *Widget*
- **Resultado esperado:** la tarjeta activa queda centrada y las vecinas asoman a los lados con menos opacidad y tamaño. Arrastrar mueve las tarjetas y al soltar se ajusta a la más cercana; un arrastre corto vuelve a la de origen; no se pasa del primero ni del último. Tocar una vecina la centra (no abre el detalle); tocar la activa lo abre. Los cines y el botón cambian con la película activa. Al cambiar de pestaña o de ciudad vuelve a la primera tarjeta, y cerca del final se pide la página siguiente.

### TC-LOC-01 Ubicación del dispositivo
- **Trazabilidad:** RF-CAT-06 · RN-49 · *Unit* + *Widget* + *Manual (emulador)*
- **Resultado esperado:** al abrir Inicio se pide el permiso de ubicación una sola vez. Con permiso, cada cine muestra su distancia real, el más cercano va primero y "cerca de ti" aparece solo hasta 25 km. Sin permiso (o con la ubicación apagada) la cartelera funciona igual y cada cine muestra su dirección. El selector de ciudad ofrece "Usar mi ubicación" mientras no haya posición; con el permiso bloqueado lleva a los ajustes. La posición no aparece en ninguna solicitud al servidor.

### TC-UI-04 Video corto (tarjeta del carrusel y detalle)
- **Trazabilidad:** RF-CAT-07 · RN-48 · *Int.* + *Widget*
- **Resultado esperado:** la API sirve el `.mp4` como `video/mp4`, con respuestas por rangos (206) y sin servir otros formatos de video. La lista y el detalle exponen `previewVideoUrl` como URL absoluta, o `null`. En la app el video arranca solo cuando su tarjeta pasa a ser la activa del carrusel (y en el detalle), sin sonido y en bucle; las demás tarjetas no crean reproductor y al irse de la tarjeta se libera. Llena el recuadro del póster (261 × 140) recortado con cover, o un recuadro 16:9 en el detalle; mientras carga se ve el póster con un indicador y, si falla (al cargar o a mitad de reproducción), se queda el póster. Se pausa en segundo plano o cuando otra pantalla cubre Inicio (y continúa al volver), y el controlador se libera al salir (también si la pantalla se cierra durante la carga). Una película sin video no deja espacio.

### TC-UI-05 Tráiler en el carrusel
- **Trazabilidad:** RF-CAT-08 · RN-51, RN-54 · *Widget* (reproductor simulado) + *Manual (dispositivo)*
- **Resultado esperado:** el reproductor usa solo parámetros documentados (botón de pantalla completa de YouTube apagado, sin anotaciones). Solo las películas con un tráiler de YouTube válido tienen botón de play. Al tocarlo, el tráiler se reproduce dentro de la tarjeta, en el lugar del póster, y arranca solo; la zona crece a 261 × 200 (mínimo de YouTube: 200 × 200) y el video corto cede el lugar. **Nada se superpone al reproductor**: los botones de pantalla completa y de cerrar están debajo de él, dentro de la tarjeta y sin tocarse. Cerrar, terminar o fallar (con un aviso) devuelve la tarjeta a como estaba, sin mover el carrusel. El botón atrás cierra el tráiler antes de salir de Inicio. Pasar a otra tarjeta lo cierra y volver no lo reanuda. Tocar el play de una tarjeta vecina la centra. El botón de pantalla completa de la app (no el de YouTube) abre una pantalla en horizontal desde el segundo en que iba, con la X en una franja fuera del video y el video ≥ 200 × 200; con la X o atrás vuelve a la tarjeta en pausa donde quedó y se devuelven la orientación y las barras; si el video termina, la tarjeta vuelve a como estaba.

### TC-REV-04 Comentarios del tráiler de YouTube
- **Trazabilidad:** RF-REV-03 · RN-52, RN-53 · *Unit* + *Int.* + *Widget* + *Manual (con clave real)*
- **Resultado esperado:** se piden hasta 20 comentarios; si el video tiene 2, se muestran 2. La hoja muestra **solo** esos, con el rótulo "Comentarios del tráiler en YouTube", la nota "Fuente: YouTube…" y sus "me gusta" reales; el botón que la abre dice "Ver comentarios" (sin la cuenta de opiniones de la app) y en el detalle la calificación de la app va en otra fila; los comentarios de la comunidad (por ejemplo, los generados por el seed) no se listan. Sin clave, sin tráiler, con comentarios cerrados o si YouTube falla, dice "Aún no hay comentarios." sin errores; mientras llegan, se ve un indicador. La clave no aparece en ninguna respuesta ni en la URL de la solicitud a YouTube. Dos solicitudes seguidas gastan una sola consulta (caché), y un fallo no se reintenta en cada solicitud. Un `limit` fuera de 1 a 20 da 400 sin gastar cuota.

## Reportes

### TC-REP-01 KPIs con valores conocidos
- **Trazabilidad:** HU-21 · RN-36, RN-37 · *Int.*
- **Precondiciones:** 2 funciones de 10 asientos. La función 1 tiene una reserva de 3 asientos a 10.00. La función 2 tiene una reserva de 2 asientos a 15.00 y otra de 1 asiento a 15.00 que luego se cancela.
- **Resultado esperado:**
  - Ingresos = 60.00 (3×10 + 2×15)
  - Reservas = 2
  - Entradas = 5
  - Ticket promedio = 30.00
  - Ocupación promedio = 5/20 = 25 %

## Sin conexión (app móvil)

### TC-OFF-01 Ticket sin conexión
- **Trazabilidad:** HU-22 · RF-OFF-02 · *Manual*
- **Precondiciones:** A abrió "Mis reservas" con conexión.
- **Pasos:** activar el modo avión y abrir el ticket.
- **Resultado esperado:** se ven el QR, el código y los datos de la reserva, con el aviso de modo sin conexión y la fecha de la última sincronización.

### TC-OFF-02 Reservar sin conexión no está permitido
- **Trazabilidad:** HU-22 · RF-OFF-03 · ADR-010 · *Manual* + *bloc_test*
- **Pasos:** en modo avión, intentar abrir el mapa de asientos.
- **Resultado esperado:** mensaje "Necesitas conexión para elegir asientos". No se hace ninguna solicitud ni se encola nada.

### TC-OFF-03 Reconciliación
- **Trazabilidad:** RF-OFF-04 · *Manual*
- **Pasos:** con el dispositivo 1 sin conexión, cancelar la reserva desde el dispositivo 2. Reconectar el dispositivo 1.
- **Resultado esperado:** la reserva aparece `Cancelled` en el dispositivo 1.

---

## Casos agregados en la fase 2

Surgieron al implementar: cubren los riesgos que el diseño inicial no había separado. Todos están automatizados contra SQL Server real.

### TC-CONC-04 Cancelaciones simultáneas de una misma reserva
- **Trazabilidad:** RN-30 · ADR-004 · *Int.*
- **Pasos:** 4 solicitudes `POST /bookings/{id}/cancel` a la vez, 5 repeticiones.
- **Resultado esperado:** exactamente un `200` y tres `409 booking_already_cancelled`; los asientos quedan `Available` y se avisa **una sola vez**.

### TC-CONC-05 Un mismo usuario, varias solicitudes de bloqueo a la vez
- **Trazabilidad:** RN-20 · ADR-004 · *Int.*
- **Pasos:** (a) 5 solicitudes simultáneas de 4 asientos distintos del mismo usuario. (b) Prueba determinista: se detiene la primera solicitud dentro de su transacción y se verifica que la segunda del mismo usuario **espera**.
- **Resultado esperado:** nunca más de 10 asientos del usuario; las que exceden responden `422 lock_limit_exceeded`. Otro usuario no queda bloqueado.

### TC-CONC-06 Conjuntos de asientos que se solapan
- **Trazabilidad:** RN-21 · ADR-004 · *Int.*
- **Pasos:** A pide {1,2,3} y B pide {3,2,1} a la vez; luego A pide {1,2} y B pide {2,3}. Diez repeticiones con usuarios nuevos.
- **Resultado esperado:** un solo ganador (`200`) y un `409`, sin ningún `500` (sin interbloqueos); el asiento exclusivo del perdedor queda `Available`.

### TC-CONC-07 La limpieza libera un bloqueo justo mientras otro usuario lo toma
- **Trazabilidad:** RN-23 · ADR-005 · *Int.* (carrera inyectada de forma determinista)
- **Pasos:** el bloqueo de A venció; entre que B lo lee y lo escribe, (a) la limpieza lo deja `Available`, (b) un tercero se lo queda.
- **Resultado esperado:** (a) B recibe `200` (se reintenta una vez: no hay ningún dueño). (b) B recibe `409 seat_unavailable` y el bloqueo del tercero no se pisa.

### TC-CONC-08 El índice único filtrado como última defensa
- **Trazabilidad:** RN-31 · ADR-004 · *Int.*
- **Pasos:** se inserta, por fuera de la lógica de la aplicación, una reserva activa sobre un asiento que A tiene bloqueado, y A confirma.
- **Resultado esperado:** `409 seat_unavailable`; A no crea una segunda reserva activa del mismo asiento, aunque la `RowVersion` no haya intervenido.

### TC-SEAT-11 Liberar: todo o nada e idempotente
- **Trazabilidad:** RN-22 · *Int.*
- **Resultado esperado:** liberar un asiento ya libre responde `204` sin avisar; si alguno de los pedidos es de otra persona o está vendido, `404 lock_not_found` y **no se libera ninguno**.

### TC-BOOK-07 Idempotencia: variantes
- **Trazabilidad:** RN-27 · *Int.*
- **Resultado esperado:** misma clave y misma solicitud → `200` con la misma reserva; misma clave con otra solicitud → `422 idempotency_key_reused`; clave de más de 24 h → `409 idempotency_key_expired`; la misma clave de dos usuarios → dos reservas independientes; clave de más de 64 caracteres → `400`.

### TC-BOOK-08 Frontera de la ventana de cancelación
- **Trazabilidad:** RN-30 · *Int.* (con reloj controlado)
- **Resultado esperado:** 1 ms antes del límite y exactamente 2 h antes → `200`; 1 ms dentro de la ventana → `422 cancellation_window_closed`. El administrador cancela hasta el inicio; después, `422 showtime_already_started`.

### TC-OPS-01 Los valores inseguros de desarrollo no llegan a producción
- **Trazabilidad:** ADR-014 · *Unit* + *Int.*
- **Resultado esperado:** `appsettings.json` es seguro por defecto (cookie `Secure`, sin migración ni seed automáticos, sin Swagger, sin cadenas ni claves). Fuera de Development, la API **no arranca** con la cookie sin `Secure` ni conectada como `sa`, salvo `Local:AllowInsecureSettings=true`.

### TC-OPS-02 Límite de solicitudes de asientos
- **Trazabilidad:** RN-40 · *Int.*
- **Resultado esperado:** al superar el cupo por minuto, `429 rate_limited` con `Retry-After`; el cupo es **por usuario** (otro usuario desde la misma IP no se ve afectado) y consultar el mapa no cuenta.

### TC-SEC-12 Ruta inexistente
- **Trazabilidad:** ADR-015 · *Int.*
- **Resultado esperado:** sin token, `401` (igual que una ruta protegida: no se pueden enumerar rutas); con token, `404` ProblemDetails.
