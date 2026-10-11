# API REST

- **Base:** `/api/v1`. La versión va en la ruta: es explícita y fácil de probar con Postman.
- **Formato:** JSON en camelCase. Fechas en ISO 8601 UTC (`2026-10-02T23:00:00Z`).
- **Errores:** `application/problem+json` con un `code` estable (ver [architecture.md §5](architecture.md#5-manejo-de-errores)).
- **Documentación:** la referencia viva es Swagger/OpenAPI en `/swagger` (en Development, o con `Swagger:Enabled=true`). Este documento resume el diseño y las convenciones.

## 1. Convenciones

| Tema | Convención |
|---|---|
| Recursos | Sustantivos en plural y kebab-case: `/cinema-rooms`, `/showtimes`. |
| Acciones que no son CRUD | `POST /{recurso}/{id}/{acción}` (`/bookings/{id}/cancel`, `/showtimes/{id}/seats/lock`). Se elige por coherencia y legibilidad frente a modelar cada acción como sub-recurso. |
| Paginación | `?page=1&pageSize=20` (máximo 100). Respuesta: `{ items, page, pageSize, totalCount }`. |
| Filtros | Por query string con nombres explícitos: `?genreId=3&search=noche&date=2026-10-05`. |
| Creación | `201 Created` con encabezado `Location` y el recurso creado. |
| Sin contenido | `204` en DELETE, logout y release. |
| Seguridad por defecto | Todo endpoint exige autenticación salvo los marcados como públicos (`[AllowAnonymous]`). Una ruta que no existe, sin token, también responde `401` (un anónimo no puede averiguar qué rutas hay); con token responde `404`. |
| Fechas | ISO 8601 UTC con sufijo `Z`. El parámetro `date` de `/showtimes` es una **fecha local del cine** (`yyyy-MM-dd`, zona `Cinema:TimeZone`), no una fecha UTC. |
| Paginación inválida | `page < 1`, `pageSize < 1` o `pageSize > 100` responden `400` `validation_failed` con el campo en `errors`. |
| Identidad | Siempre del token (RN-34). Ningún endpoint recibe `userId` o `role` por parámetro. |
| Idempotencia | `POST /bookings` acepta el encabezado `Idempotency-Key` (RN-27). |

## 2. Endpoints

**Acceso:** 🌐 público · 👤 Customer o Admin autenticado · 🛡️ solo Admin

### Autenticación

| Método | Ruta | Acceso | Respuestas | Notas |
|---|---|---|---|---|
| POST | `/auth/register` | 🌐 | 201, 400, 409 | Crea un Customer y devuelve tokens. |
| POST | `/auth/login` | 🌐 | 200, 400, 401, 429 | `useCookie: true` (panel web) → refresh solo en `Set-Cookie`. |
| POST | `/auth/refresh` | 🌐 | 200, 401 | Lee el refresh de la cookie o del cuerpo. Siempre rota. |
| POST | `/auth/logout` | 👤 | 204 | Revoca el refresh token actual y borra la cookie. |
| GET | `/me` | 👤 | 200 | Perfil. |
| PUT | `/me` | 👤 | 200, 400 | Edita el nombre. |

### Catálogo

| Método | Ruta | Acceso | Respuestas |
|---|---|---|---|
| GET | `/movies?search&genreId&status&city&page&pageSize` | 🌐 | 200, 400 |
| GET | `/movies/{id}` | 🌐 | 200, 404 |
| GET | `/movies/{id}/reviews?page&pageSize` | 🌐 (con token marca la propia) | 200, 400, 404 |
| PUT | `/movies/{id}/reviews/mine` | 👤 | 201 (primera vez), 200, 400, 404, 422, 429 |
| DELETE | `/movies/{id}/reviews/mine` | 👤 | 204, 404, 429 |
| GET | `/movies/{id}/trailer-comments?limit` | 🌐 | 200, 400, 404 |
| GET | `/cities` | 🌐 | 200 |
| GET | `/cinemas?city` | 🌐 | 200 |
| GET | `/media/{ruta}` | 🌐 | 200 (imagen), 401/404 |
| POST | `/movies` | 🛡️ | 201, 400, 403 |
| PUT | `/movies/{id}` | 🛡️ | 200, 400, 404 |
| DELETE | `/movies/{id}` | 🛡️ | 204, 404, 409 |
| GET | `/genres` | 🌐 | 200 |
| POST / PUT / DELETE | `/genres[/{id}]` | 🛡️ | 201/200/204, 400, 404, 409 |

### Administración del catálogo (fase 4)

Las lecturas del administrador viven bajo `/admin` (incluyen lo dado de baja y los conteos que necesita el panel); las escrituras usan las rutas de siempre.

| Método | Ruta | Acceso | Respuestas | Notas |
|---|---|---|---|---|
| GET | `/admin/movies?search&isActive&status&page&pageSize` | 🛡️ | 200, 400 | Incluye películas dadas de baja. `status`: `nowShowing`, `presale`, `comingSoon`. |
| GET | `/admin/movies/{id}` | 🛡️ | 200, 404 | Con `genreIds`, medios y estado. |
| GET | `/admin/genres` | 🛡️ | 200 | Con `movieCount`. |
| GET | `/admin/cinemas` | 🛡️ | 200 | Con `roomCount` y `upcomingShowtimes`; incluye los inactivos. |
| GET | `/admin/cinemas/{id}` | 🛡️ | 200, 404 | |
| POST | `/cinemas` | 🛡️ | 201, 400, 403, 409 | `cinema_name_taken`. |
| PUT | `/cinemas/{id}` | 🛡️ | 200, 400, 404, 409 | Desactivar un cine cancela sus funciones futuras sin ventas; con reservas confirmadas: `cinema_has_active_bookings`. |
| DELETE | `/movies/{id}` | 🛡️ | 204, 404, 409 | Baja lógica (RN-08). `movie_has_active_bookings`. |
| DELETE | `/genres/{id}` | 🛡️ | 204, 404, 409 | `genre_in_use`. |

### Salas

| Método | Ruta | Acceso | Respuestas |
|---|---|---|---|
| GET | `/cinema-rooms?cinemaId` | 🛡️ | 200 |
| POST | `/cinema-rooms` | 🛡️ | 201, 400, 409 |
| PUT | `/cinema-rooms/{id}` | 🛡️ | 200, 400, 404, 409 |
| GET | `/cinema-rooms/{id}/seats` | 🛡️ | 200, 404 |
| PUT | `/cinema-rooms/{id}/layout` | 🛡️ | 200, 400, 404, 409 |

`GET /cinema-rooms` devuelve por sala: `cinemaName`, `seatCount`, `rows`, `positions` y `upcomingShowtimes` (el panel bloquea el editor de layout si es mayor que 0; la API lo vuelve a comprobar y responde `409 room_has_future_showtimes`). `PUT .../layout` recibe `{ rows, positions, emptyCells }` (`emptyCells`: celdas que son pasillo, como `"A3"`); `GET .../seats` devuelve la cuadrícula y sus asientos.

### Funciones y asientos

| Método | Ruta | Acceso | Respuestas | Notas |
|---|---|---|---|---|
| GET | `/showtimes?movieId&date&page&pageSize` | 🌐 | 200 | Solo `Scheduled` y futuras para el público. |
| GET | `/admin/showtimes?movieId&cinemaId&roomId&status&from&to&upcomingOnly&page&pageSize` | 🛡️ | 200, 400 | Todas, también canceladas y pasadas. `status`: `scheduled`, `cancelled`. `from`/`to`: fechas locales del cine. Con `seatsSold`, `confirmedBookings`, `hasBookings`, `started`. |
| GET | `/admin/showtimes/{id}` | 🛡️ | 200, 404 | |
| GET | `/showtimes/{id}` | 🌐 | 200, 404 | |
| POST | `/showtimes` | 🛡️ | 201, 400, 404, 409, 422 | `startsAt` en UTC con `Z`. Genera los asientos de la función en la misma transacción. Dos administradores en la misma sala se serializan: una función se crea y la otra recibe `409`. |
| PUT | `/showtimes/{id}` | 🛡️ | 200, 400, 404, 409, 422 | Solo si no empezó y no tiene reservas (ni canceladas). |
| POST | `/showtimes/{id}/cancel` | 🛡️ | 200, 404, 422 | Cancela las reservas confirmadas y libera asientos y bloqueos (RN-17). |
| GET | `/showtimes/{id}/seats` | 👤 | 200, 404 | Incluye `isMine`. |
| POST | `/showtimes/{id}/seats/lock` | 👤 | 200, 400, 404, 409, 422, 429 | Todo o nada. |
| POST | `/showtimes/{id}/seats/release` | 👤 | 204, 400, 404 | Solo los propios. |
| GET | `/showtimes/{id}/seats/recommendations?count=2` | 👤 | 200, 400, 404 | GET porque es una lectura sin efectos. |

### Reservas

| Método | Ruta | Acceso | Respuestas | Notas |
|---|---|---|---|---|
| POST | `/bookings` | 👤 | 201 (nueva), 200 (repetición idempotente), 400, 404, 409, 422 | Cabecera opcional `Idempotency-Key` (hasta 64 caracteres). |
| GET | `/bookings?status&page&pageSize` | 👤 | 200, 400 | Siempre las del usuario del token (un `userId` en la URL se ignora). El listado general del administrador es `GET /admin/bookings`. |
| GET | `/bookings/{id}` | 👤 | 200, 404 | 404 si no existe **o no es del usuario** (idéntico en ambos casos). Un administrador puede consultar cualquiera. |
| POST | `/bookings/{id}/cancel` | 👤 | 200, 404, 409, 422 | El cliente cancela hasta 2 h antes; el administrador, hasta el inicio. |

### Administración y reportes

| Método | Ruta | Acceso | Respuestas | Notas |
|---|---|---|---|---|
| GET | `/admin/bookings?search&status&movieId&cinemaId&from&to&page&pageSize` | 🛡️ | 200, 400 | Reservas de todos los clientes. `search`: código, nombre o email. `from`/`to`: fecha local del cine de la **función**, no de la compra. `status`: `confirmed`, `cancelled`. |
| GET | `/users?search&role&isActive&page&pageSize` | 🛡️ | 200, 400 | `role`: `Admin` o `Customer`. |
| PUT | `/users/{id}/status` | 🛡️ | 200, 404, 422 | `{ isActive }`. Desactivar revoca sus sesiones. `cannot_deactivate_self`. |
| GET | `/reports/summary?from&to` | 🛡️ | 200, 400 | Ingresos, reservas, entradas, ticket promedio, ocupación promedio y funciones del periodo. |
| GET | `/reports/sales-daily?from&to` | 🛡️ | 200, 400 | Un registro por día del rango (con ceros), por el día de la función en el calendario del cine. |
| GET | `/reports/sales-by-movie?from&to` | 🛡️ | 200, 400 | De mayor a menor ingreso; el empate se desempata por título. |
| GET | `/reports/occupancy?from&to&groupBy=showtime\|room&page&pageSize` | 🛡️ | 200, 400 | Paginado. Por función o por sala. |

Los reportes aceptan `from` y `to` como fechas del calendario del cine (`yyyy-MM-dd`, ambas incluidas) y un máximo de 366 días (RN-57).

### Infraestructura

| Método | Ruta | Acceso | Notas |
|---|---|---|---|
| GET | `/health` | 🌐 | Estado de SQL Server (y de Redis en la fase 6). La usa el healthcheck de Docker. |

### Códigos de negocio

Los clientes deciden por el `code`, nunca por el texto. Los que llevan `conflicts` informan qué asientos fallaron.

| `code` | HTTP | Cuándo |
|---|---|---|
| `seat_unavailable` | 409 | Al bloquear: algún asiento está vendido, bloqueado por otra persona, o lo tomaron mientras se procesaba. Incluye `conflicts`. No se bloquea ninguno. |
| `lock_expired` | 409 | Al confirmar: algún asiento ya no está bloqueado por ti con el bloqueo vigente. Incluye `conflicts`. |
| `lock_limit_exceeded` | 422 | Más de 10 asientos bloqueados en la función (RN-20). |
| `seat_limit_exceeded` | 422 | Una reserva de más de 10 asientos (RN-24). |
| `sales_closed` / `showtime_cancelled` | 422 | La función ya empezó / fue cancelada (RN-16). |
| `seat_not_found` | 404 | Un asiento no pertenece a la función. |
| `lock_not_found` | 404 | Al liberar: algún asiento no es tuyo (RN-22). |
| `booking_not_found` | 404 | La reserva no existe o no es tuya (RN-32). |
| `booking_already_cancelled` | 409 | La reserva ya estaba cancelada. |
| `cancellation_window_closed` / `showtime_already_started` | 422 | Fuera del plazo de cancelación del cliente / del administrador (RN-30). |
| `idempotency_key_reused` | 422 | La misma clave con una solicitud distinta (RN-27). |
| `idempotency_key_expired` | 409 | La clave tiene más de 24 horas (RN-27). |
| `concurrency_conflict` | 409 | Otra solicitud modificó el recurso a la vez y no se pudo resolver sola. Es seguro reintentar. |
| `sales_not_open` | 422 | La película todavía no abrió su preventa (ni se estrenó): no se puede bloquear ni reservar (RN-42). |
| `cinema_closed` | 422 | El cine de la función está inactivo (RN-50). |
| `movie_not_released` | 422 | Al opinar sobre una película que aún no se estrenó (RN-46). |
| `review_not_found` | 404 | Al borrar: no tienes una opinión sobre esa película. |
| `movie_not_found` / `cinema_not_found` / `room_not_found` / `showtime_not_found` / `genre_not_found` / `user_not_found` | 404 | El recurso no existe. |
| `genre_name_taken` / `cinema_name_taken` / `room_name_taken` | 409 | Nombre repetido (género y cine globales; sala dentro de su cine) (RN-10). |
| `genre_in_use` | 409 | Borrar un género que tiene películas. |
| `movie_has_active_bookings` / `cinema_has_active_bookings` | 409 | Dar de baja una película o desactivar un cine con funciones futuras con reservas confirmadas (RN-08). |
| `room_has_future_showtimes` | 409 | Cambiar el layout o desactivar una sala con funciones futuras (RN-11). |
| `showtime_overlap` | 409 | La sala ya tiene otra función en ese horario, incluida la limpieza (RN-13). |
| `showtime_has_bookings` | 409 | Editar una función que ya tiene reservas: se cancela en su lugar. |
| `showtime_in_past` / `showtime_before_release` | 422 | Programar una función en el pasado / antes del estreno (RN-56). |
| `movie_inactive` / `room_inactive` / `room_has_no_seats` | 422 | La película está de baja / la sala o su cine están desactivados / la sala no tiene asientos. |
| `cannot_deactivate_self` | 422 | Un administrador intenta desactivar su propia cuenta (RN-39). |
| `rate_limited` | 429 | Superaste el límite de solicitudes (RN-06, RN-40, RN-47). Incluye `Retry-After`. |

## 3. Ejemplos de contrato

### Tarjeta de la cartelera (`GET /movies`)

Todo lo que dibuja la tarjeta del móvil sale de este objeto (ver [ui-data-coverage.md](ui-data-coverage.md)).
`status`: `NowShowing`, `PreSale` o `ComingSoon` (RN-41). `status` en la consulta: `nowShowing`, `presale` o `comingSoon`
(sin distinguir mayúsculas; otro valor es `400`). `city` deja las películas con funciones próximas en esa ciudad (RN-49).

```json
{
  "id": 5,
  "title": "Marea Alta",
  "synopsis": "Dos hermanos heredan un faro…",
  "durationMinutes": 121,
  "classification": "+14",
  "posterUrl": "http://10.0.2.2:8090/media/posters/marea-alta.jpg",
  "trailerUrl": null,
  "genres": ["Aventura", "Drama"],
  "releaseDate": "2026-09-25",
  "preSaleStartsOn": null,
  "status": "NowShowing",
  "averageRating": 4.5,
  "reviewCount": 20,
  "featuredReview": { "id": 91, "authorName": "David F.", "authorAvatarUrl": null, "rating": 5,
                      "comment": "Excelente película, me encantó", "createdAt": "2026-10-07T15:00:00Z", "isMine": false },
  "previewVideoUrl": null,
  "cinemas": [{ "id": 1, "name": "Cinerama - Chimbote", "address": "Av. José Pardo 123", "city": "Chimbote",
                "region": "Áncash", "latitude": -9.0745, "longitude": -78.5936, "logoUrl": null }]
}
```

- `averageRating` es `null` si nadie opinó (no se inventa un 0). Una película "próximamente" trae `cinemas: []`.
- Las URL de imagen son **siempre absolutas**: se arman con la dirección por la que llegó la solicitud (así sirven en el emulador, `10.0.2.2`) o con `Media:PublicBaseUrl` si está configurada. Una imagen sin cargar es `null`.
- `previewVideoUrl` (en la lista y en el detalle): un `.mp4` corto (URL absoluta, como las imágenes) o `null` si la película no tiene. La app lo reproduce sin sonido y en bucle dentro de la tarjeta activa del carrusel y en el detalle.
- El detalle (`GET /movies/{id}`) agrega `upcomingShowtimes`, cada una con `cinemaId` y `cinemaName`.
- Distancia: **no viene del servidor**. La app la calcula con las coordenadas del cine y la ubicación del dispositivo.

### Comentarios del tráiler (`GET /movies/{id}/trailer-comments?limit=20`)

`limit` de 1 a 20 (por defecto 20; otro valor es `400` con el campo `limit`). Siempre responde `200`: el `status` dice qué pasó.

```json
{ "status": "ok", "videoId": "1McLG8FgNaI",
  "items": [{ "authorName": "@ana", "authorAvatarUrl": "https://yt3.ggpht.com/…", "text": "Qué buen tráiler",
              "likeCount": 12, "publishedAt": "2026-10-01T12:00:00Z" }] }
```

| `status` | Significa | `items` |
|---|---|---|
| `ok` | Hay comentarios (hasta `limit`; si el video tiene 2, vienen 2) | con datos |
| `empty` | El video no tiene comentarios | vacío |
| `disabled` | El dueño cerró los comentarios o el video ya no existe | vacío |
| `noTrailer` | La película no tiene un tráiler de YouTube válido | vacío |
| `unavailable` | El servidor no tiene clave de YouTube, se agotó la cuota o YouTube no respondió | vacío |

### Opinar (`PUT /movies/{id}/reviews/mine`)

```json
{ "rating": 4, "comment": "Muy buena" }
```

`201` la primera vez y `200` al reemplazarla. La identidad sale del token: un `userId` en el cuerpo se ignora (RN-34).

### Bloquear asientos

```http
POST /api/v1/showtimes/42/seats/lock
Authorization: Bearer eyJ...
Content-Type: application/json

{ "seatIds": [1207, 1208] }
```

```json
200 OK
{
  "showtimeId": 42,
  "lockedUntil": "2026-10-02T23:05:00Z",
  "seats": [
    { "seatId": 1207, "label": "F7", "status": "Locked", "isMine": true },
    { "seatId": 1208, "label": "F8", "status": "Locked", "isMine": true }
  ]
}
```

```json
409 Conflict
{
  "type": "https://cinerama.example/errors/seat-unavailable",
  "title": "Uno o más asientos no están disponibles",
  "status": 409,
  "code": "seat_unavailable",
  "conflicts": [{ "seatId": 1207, "label": "F7" }],
  "traceId": "00-..."
}
```

### Confirmar reserva

```http
POST /api/v1/bookings
Authorization: Bearer eyJ...
Idempotency-Key: 6f1c2a0e-6c0b-4a49-9d6f-1b2f1d2e3a4b

{ "showtimeId": 42, "seatIds": [1207, 1208] }
```

```json
201 Created
Location: /api/v1/bookings/315
{
  "id": 315,
  "code": "K7QH3MXP",
  "status": "Confirmed",
  "showtime": { "id": 42, "movieId": 3, "movieTitle": "Noche en el Puerto", "roomName": "Sala 2", "startsAt": "2026-10-03T01:00:00Z" },
  "seats": [{ "seatId": 1207, "label": "F7", "price": 18.00 }, { "seatId": 1208, "label": "F8", "price": 18.00 }],
  "totalAmount": 36.00,
  "ticketPayload": "K7QH3MXP.Zk9vX2...",
  "createdAt": "2026-10-02T23:01:12Z",
  "cancelledAt": null
}
```

## 4. SignalR

| Elemento | Valor |
|---|---|
| Ruta | `/hubs/seats` |
| Autenticación | JWT. En WebSockets viaja como `?access_token=` (el *upgrade* no admite cabeceras personalizadas). **Solo se lee en rutas `/hubs`**: la API REST lo ignora. Un token vencido o inválido se rechaza con 401. |
| Cliente → servidor | `JoinShowtime(showtimeId)`, `LeaveShowtime(showtimeId)`. `JoinShowtime` falla con `showtime_not_found` o `too_many_subscriptions` (máximo 5 funciones por conexión). |
| Servidor → cliente | `SeatStatusChanged { showtimeId, changes: [{ seatId, status }] }` con `status` ∈ `Available`, `Locked`, `Booked` |
| Grupos | `showtime-{id}` |
| Regla | El hub **solo emite**: no tiene métodos para bloquear, liberar ni reservar. Todas las escrituras pasan por REST. Los avisos se envían después del commit. |
| Vencimiento del token | El servidor cierra la conexión cuando vence el token (`CloseOnAuthenticationExpiration`); el cliente se reconecta con un token renovado. |
| Reconexión | Los grupos se pierden y los mensajes emitidos mientras no había conexión **no se repiten**. El cliente vuelve a unirse y pide `GET /showtimes/{id}/seats`. El estado REST es la referencia. Si SignalR no está disponible, todo sigue funcionando por REST. |

Ver [ADR-016](adr/ADR-016-signalr-realtime.md).

**El evento no incluye quién bloqueó el asiento.** Todos los miembros del grupo reciben el mismo mensaje, y enviar ids de usuario expondría datos de otros clientes. Cada cliente ya sabe cuáles son sus propios bloqueos, porque los recibió en la respuesta `200` de `/seats/lock`. Por eso un evento `Locked` sobre un asiento propio no cambia su representación como "Seleccionado".
