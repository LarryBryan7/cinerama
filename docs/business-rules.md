# Reglas de negocio

Cada regla tiene un identificador estable (`RN-xx`) que se referencia desde historias de usuario, casos de uso, casos de prueba y, en el código, desde los mensajes de error y los nombres de los tests. Si una regla cambia, se actualiza aquí primero.

**Convenciones**

- *Dónde se aplica* indica la capa responsable. "BD" significa que existe además una restricción en SQL Server que la garantiza aunque la aplicación falle.
- Los valores numéricos (5 minutos, 10 asientos, 2 horas) son configurables; el valor indicado es el predeterminado.

---

## Parámetros de negocio del proyecto

Valores confirmados por el autor como reglas de negocio de Cinerama (proyecto personal). Son configurables (`CinemaOptions`, `JwtOptions`, `SeatingOptions`) y estos son los predeterminados.

| Parámetro | Valor | Regla | Configuración |
|---|---|---|---|
| Duración del bloqueo de asientos | 5 minutos | RN-19 | `Seating:LockMinutes` |
| Máximo de asientos por cliente y función | 10 | RN-20, RN-24 | `Seating:MaxSeatsPerUser` |
| Limpieza entre funciones | 15 minutos | RN-12 | `Cinema:CleaningMinutes` |
| Cancelación del cliente | Hasta 2 horas antes de la función | RN-30 | `Cinema:CancellationHours` |
| Vida del access token | 15 minutos | RN-05 | `Jwt:AccessTokenMinutes` |
| Vida del refresh token | 7 días | RN-05 | `Jwt:RefreshTokenDays` |
| Frecuencia de limpieza de bloqueos vencidos | Cada 30 segundos | RN-23, CU-06 | `Seating:CleanupSeconds` |

---

## Cuenta y sesión

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-01 | El email es único. Se normaliza (espacios recortados y minúsculas) antes de guardarlo y antes de compararlo. | Application + BD (índice único) | 409 `email_already_registered` |
| RN-02 | La contraseña tiene al menos 8 caracteres, con al menos una letra y un número. | Application (validador) | 400 |
| RN-03 | Un usuario inactivo no puede iniciar sesión ni renovar la sesión. Al desactivarlo se revocan todos sus refresh tokens. | Application | 401 |
| RN-04 | El registro público crea siempre usuarios con rol `Customer`. Los usuarios `Admin` solo se crean por seed o por otro administrador. | Application | — |
| RN-05 | El access token dura 15 minutos y el refresh token 7 días. Cada uso del refresh token lo reemplaza por uno nuevo (rotación). Si se presenta un refresh token que ya fue rotado, se revoca toda la cadena de esa sesión. | Application | 401 `refresh_token_reused` |
| RN-06 | Los endpoints de autenticación aceptan como máximo 10 solicitudes por minuto por IP. | API (rate limiter) | 429 |
| RN-39 | Un administrador no puede desactivar su propia cuenta, para no quedar fuera del sistema. | Application | 422 `cannot_deactivate_self` |

## Catálogo

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-07 | Solo las películas activas aparecen en la cartelera y admiten nuevas funciones. | Application | 422 `movie_inactive` |
| RN-08 | Eliminar una película es una baja lógica. Se rechaza si la película tiene funciones futuras con reservas confirmadas. | Application | 409 `movie_has_active_bookings` |
| RN-09 | Una película tiene título (1–200 caracteres), duración entre 1 y 400 minutos y al menos un género. | Application + BD (CHECK duración) | 400 |

## Salas y asientos

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-10 | El nombre de la sala es único. El layout tiene de 1 a 26 filas (A–Z) y de 1 a 40 posiciones por fila. Una posición sin asiento representa un pasillo. | Application + BD (único) | 400 / 409 |
| RN-11 | El layout de una sala no puede modificarse si la sala tiene funciones futuras programadas. | Application | 409 `room_has_future_showtimes` |

## Funciones

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-12 | Una función se programa en el futuro. Su fin se calcula como `inicio + duración de la película + 15 minutos de limpieza`. | Application + BD (CHECK fin > inicio) | 422 `showtime_in_past` |
| RN-13 | Dos funciones de la misma sala no pueden solaparse. | Application (transacción serializable) | 409 `showtime_overlap` |
| RN-14 | El precio es mayor o igual a 0, con dos decimales, en una única moneda configurable. | Application + BD (CHECK) | 400 |
| RN-15 | Al crear una función se genera un registro de asiento por función (`ShowSeat`) en estado `Available` por cada asiento activo de la sala. | Application (misma transacción) | — |
| RN-16 | La venta (bloquear y confirmar) cierra al inicio de la función. Una función cancelada no vende. | Application | 422 `sales_closed` / `showtime_cancelled` |
| RN-17 | Un administrador puede cancelar una función antes de su inicio. Al hacerlo se cancelan sus reservas confirmadas y se liberan sus bloqueos. | Application | 422 `showtime_already_started` |

## Bloqueo temporal de asientos

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-18 | Solo se pueden bloquear asientos `Available`, o `Locked` con el bloqueo vencido. | Domain + BD (RowVersion) | 409 `seat_unavailable` |
| RN-19 | Un bloqueo dura 5 minutos desde que se crea. Volver a bloquear un asiento propio no extiende el plazo: la operación es idempotente y conserva el vencimiento original. | Domain | — |
| RN-20 | Un usuario puede tener como máximo 10 asientos bloqueados a la vez en una misma función, **sumando todas sus solicitudes**, incluso las simultáneas (se serializan por usuario y función). Una sola solicitud de más de 10 asientos también se rechaza. | Application + lock de aplicación en SQL Server | 422 `lock_limit_exceeded` |
| RN-21 | Bloquear varios asientos es todo o nada: si uno no está disponible, no se bloquea ninguno y la respuesta indica cuáles fallaron. | Application (una transacción) | 409 `seat_unavailable` |
| RN-22 | Un usuario puede liberar sus propios bloqueos en cualquier momento, pero nunca los de otro usuario ni asientos vendidos. Es todo o nada: si alguno de los pedidos no es suyo, no se libera ninguno. Liberar un asiento que ya está libre no es un error. | Application | 404 `lock_not_found` |
| RN-23 | Un bloqueo vencido equivale a un asiento disponible para cualquier operación, aunque el proceso de limpieza todavía no lo haya liberado. | Domain | — |

## Reserva

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-24 | Confirmar una reserva requiere de 1 a 10 asientos, todos de la misma función, bloqueados por el mismo usuario y con el bloqueo vigente. Si alguno no cumple, no se confirma ninguno. | Domain + Application | 409 `lock_expired` (el bloqueo venció o no es tuyo), 409 `seat_unavailable` (alguien modificó los asientos mientras confirmabas), 422 `seat_limit_exceeded` (más de 10) |
| RN-25 | Al confirmar, los asientos pasan a `Booked`. El precio vigente se copia a cada asiento de la reserva y el total es la suma. | Application | — |
| RN-26 | El pago es simulado. El sistema no recibe ni almacena datos de tarjetas. | Alcance | — |
| RN-27 | Si se repite una confirmación con la misma `Idempotency-Key` del mismo usuario dentro de 24 horas, se devuelve la reserva original (200) y no se crea otra. La clave es por usuario. Con la misma clave y una solicitud distinta, 422 `idempotency_key_reused`; con una clave de más de 24 horas, 409 `idempotency_key_expired`. | Application + BD (índice único filtrado) | — |
| RN-28 | El código de reserva es único y no secuencial: 8 caracteres alfanuméricos sin caracteres ambiguos (sin 0/O ni 1/I). | Application + BD (único) | — |
| RN-29 | Una reserva confirmada no se modifica. Para cambiar de asientos se cancela y se reserva de nuevo. | Alcance | — |
| RN-30 | El cliente puede cancelar hasta 2 horas antes del inicio de la función y el administrador hasta el inicio. Al cancelar, los asientos vuelven a `Available`. | Domain | 422 `cancellation_window_closed` |
| RN-31 | Un asiento de función solo puede pertenecer a una reserva activa a la vez. | **BD (índice único filtrado)**, última línea de defensa | 409 |

## Límites de uso

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-40 | Bloquear y liberar asientos acepta como máximo 60 solicitudes por minuto **por usuario** (no por IP: tras un NAT varias personas comparten dirección). Consultar el mapa no cuenta. | API (rate limiter) | 429 |

## Acceso a datos

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-32 | Un cliente solo ve y opera sus propias reservas. Una reserva ajena responde igual que una inexistente. | Application (filtro por dueño en la consulta) | 404 |
| RN-33 | Los endpoints de administración requieren el rol `Admin`. | API (policy) | 403 |
| RN-34 | La identidad y el rol se obtienen siempre del token firmado, nunca de parámetros, cabeceras personalizadas ni del cuerpo del request. | API | — |

## Ticket

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-35 | El QR contiene el código de reserva y una firma HMAC. Un QR alterado se rechaza. La validación de una reserva cancelada requiere conexión. | Application | 422 `invalid_ticket` |

## Reportes

| ID | Regla | Dónde se aplica |
|---|---|---|
| RN-36 | Ocupación de una función = asientos `Booked` / total de asientos de la función. La ocupación de una sala o de un periodo es el promedio ponderado por capacidad. | Vista SQL |
| RN-37 | Ingresos = suma de los precios de los asientos de reservas `Confirmed`. Las reservas canceladas no cuentan. | Vista SQL / SP |
| RN-38 | Las fechas se almacenan en UTC y con precisión de milisegundos (`datetime2(3)`); la API las trunca a milisegundos para que lo devuelto al crear coincida con lo consultado después. Los reportes "por día" y el filtro `date` de las funciones usan la zona horaria del cine (`Cinema:TimeZone`). | Application / SQL (`AT TIME ZONE`) |

## Estreno, preventa y cines (fase 3.5)

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-41 | El estado de una película **se deriva de dos fechas y de "hoy"** en el calendario del cine (RN-38); nunca se guarda. Si hoy ≥ `ReleaseDate`: *Cartelera*. Si no, y la preventa ya abrió (`PreSaleStartsOn` ≤ hoy): *Preventa*. Si no: *Próximamente*. La preventa abre antes o el mismo día del estreno (CHECK en la base). | Domain (`Movie.StatusOf`) / Application (filtros) | — |
| RN-42 | Se pueden bloquear y reservar asientos desde que abre la preventa (o desde el estreno, si no hay preventa). Antes, no se vende. Liberar y cancelar siguen funcionando. | Application (`ShowtimeSalesGuard`) | 422 `sales_not_open` |
| RN-49 | Cada sala pertenece a un cine. Las ciudades salen de los cines activos. Una película "está en" un cine si tiene funciones próximas en él. El filtro por ciudad deja las películas con funciones en esa ciudad y **no oculta las "próximamente"** (aún no tienen funciones). La distancia a la persona **se calcula en el dispositivo**: su ubicación nunca viaja al servidor. | Application / app | — |
| RN-50 | Las funciones de un cine inactivo no se ofrecen y no se pueden vender. | Application | 422 `cinema_closed` |

## Opiniones y calificaciones (fase 3.5)

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-43 | Cualquier persona con cuenta (también un administrador) puede opinar. **Una opinión por persona y película**: publicar de nuevo la reemplaza (`PUT`, idempotente) y la persona puede borrar la suya. Puntuación de 1 a 5; comentario opcional de hasta 500 caracteres, sin caracteres de control (salvo saltos de línea y tabulaciones). | Application / índice único `(MovieId, UserId)` / CHECK | 400 / 404 `review_not_found` |
| RN-44 | **Privacidad del autor:** las opiniones muestran el nombre y la inicial del último apellido ("Lucía R."). Nunca el nombre completo, el email ni el id. | Application | — |
| RN-45 | La calificación es el promedio con **un decimal**; sin opiniones es `null` (no "0"). La **opinión destacada** es la más reciente que tiene comentario (no la mejor puntuada: no es publicidad). | Application | — |
| RN-46 | Solo se opina sobre una película que ya se estrenó. | Application | 422 `movie_not_released` |
| RN-47 | Publicar, cambiar o borrar opiniones se limita a 10 por minuto **por usuario**. Leer no cuenta. | API (rate limiter) | 429 |
| RN-48 | Las imágenes (póster, logo del cine, foto de perfil) se guardan como ruta relativa a la carpeta de medios o como URL absoluta, y la API siempre las entrega como URL absoluta. Solo se sirven `.jpg`, `.jpeg`, `.png`, `.webp` y `.mp4` (el video corto del detalle, con soporte de rangos para reproducir sin bajarlo entero), de forma pública, desde `/media`. | API | — |

## Tráiler y comentarios de YouTube

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-51 | El tráiler es un video de YouTube que se guarda como URL (`Movies.TrailerUrl`, el código de 11 caracteres). Solo se acepta un código válido: lo demás ("e", "dsaf", otro sitio) cuenta como "sin tráiler". Se reproduce con el reproductor **oficial** de YouTube (IFrame, modo de privacidad mejorada): la app no descarga ni guarda el video. | Importador / Application (`YouTubeVideoId`) / app | — |
| RN-52 | Los comentarios del tráiler salen de la API de datos de YouTube v3. La clave vive **solo en el servidor** (`YouTube__ApiKey`), viaja en una cabecera (nunca en la URL) y no aparece en ninguna respuesta ni registro. Se devuelven hasta **20** comentarios (si el video tiene menos, los que haya), en texto plano, de 500 caracteres como máximo, con su nombre público tal como lo publica YouTube. | Infrastructure (`YouTubeCommentsClient`) / Application | 400 si `limit` no está entre 1 y 20 |
| RN-53 | YouTube nunca provoca un error de la API: sin clave, cuota agotada, comentarios cerrados o video borrado se informan en `status` (`unavailable`, `disabled`, `empty`, `noTrailer`) y la app oculta esa sección. Una respuesta correcta se recuerda 6 horas y un fallo 5 minutos, para no gastar la cuota diaria ni hacer esperar a nadie. | Application (`TrailerCommentsService`, `TrailerCommentsCache`) | — |
| RN-54 | El botón de play de la tarjeta activa reproduce el tráiler **dentro del carrusel**. Mientras suena, la zona del póster crece de 261 × 140 a **261 × 200** (YouTube exige al menos 200 × 200), tapando el título y parte de la sinopsis; al cerrar, la tarjeta vuelve a verse entera. **El reproductor no se modifica ni se le dibuja nada encima**: solo se usan parámetros documentados de su API, el botón de pantalla completa de YouTube está apagado (`fs=0`) y la app ofrece el suyo, junto al de cerrar, **debajo** del reproductor. Se cierra con ese botón, con el botón atrás, al terminar el video, si falla (con un aviso) o al pasar a otra tarjeta (y volver no lo reanuda). La pantalla completa abre una pantalla aparte en horizontal desde el segundo en que iba, con la X en una franja fuera del video; al salir se vuelve a la tarjeta con el tráiler en pausa, y si el video termina, Inicio queda como estaba. El logo y el título de YouTube no se pueden ocultar. | App | — |
| RN-55 | **YouTube es una fuente separada**: la hoja de comentarios muestra solo comentarios del tráiler en YouTube, con el rótulo "Comentarios del tráiler en YouTube" y la nota "Fuente: YouTube. Se muestran tal como los publicaron." Nunca se mezclan con los de la comunidad ni con cuentas de la app: el botón que abre la hoja dice "Ver comentarios" (sin la cuenta de opiniones de la app) y la calificación de la app se muestra aparte, en su propia fila ("4.2 · 16 opiniones en la app"). | App | — |

## Panel de administración (fase 4)

| ID | Regla | Dónde se aplica | Error |
|---|---|---|---|
| RN-56 | No se programa una función **antes del estreno** de la película: el día de la función, en el calendario del cine (RN-38), debe ser igual o posterior a `ReleaseDate`. La preventa vende entradas para funciones desde el estreno en adelante. | Application (`ShowtimeAdminService`) | 422 `showtime_before_release` |
| RN-57 | Un reporte abarca de 1 a **366 días** (ambas fechas incluidas, calendario del cine). `from` y `to` son obligatorias: faltar una, que `from` sea posterior a `to` o que el rango sea mayor se rechaza (el panel propone por defecto los últimos 30 días). | Application (`ReportService`) | 400 `validation_failed` |
| RN-58 | El panel **no decide nada de negocio**: muestra y envía; toda regla (solapamientos, plazos, límites, permisos) se aplica en la API y el panel solo traduce su `code` a un mensaje. Que un botón esté deshabilitado es una ayuda, no una barrera: la API vuelve a validar. | Panel / API | — |
| RN-59 | Las horas que escribe el administrador son **la hora del cine** (`VITE_CINEMA_TIMEZONE`, por defecto `America/Lima`), no la del navegador. El panel las convierte a UTC con esa zona antes de enviarlas y muestra las que recibe en la misma zona. | Panel (`lib/time.ts`) | — |
| RN-60 | Solo entra al panel una cuenta con rol Admin. Si una cuenta de cliente inicia sesión, la sesión recién abierta se revoca (token y cookie) y se informa que no tiene acceso. Si la sesión no se puede renovar, se vuelve al login y no queda ningún dato de administración en memoria. | Panel / API | `not_admin` (solo en el panel) |
