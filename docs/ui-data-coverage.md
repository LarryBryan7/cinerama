# Cobertura de datos de la pantalla de inicio

> **Origen de los datos (2026-10-07).** El catálogo es la cartelera real de Cinerama Chimbote, importada con
> `backend/tools/import_cartelera.py` desde el endpoint público que usa su página (solo texto, en minúsculas con la primera letra en mayúscula y "mayores de 14" como "+14", sin imágenes).
> Lo ficticio es: las cuentas, las opiniones, los pósters (marcadores generados con `make_placeholder_posters.py`), la distribución
> de las 7 salas y los precios. **Cinestar Chimbote todavía no está**: su página tiene una verificación anti-bots y no se lee
> automáticamente; falta su cartelera (ver "Lo que sigue pendiente").
> Director, reparto y distribuidora se importan al archivo `cartelera.json`, pero el modelo de datos aún no tiene dónde guardarlos.

Qué muestra cada elemento de la pantalla **Inicio** (Figma: "Cartelera - dark - 1"), de dónde sale y dónde queda en la base de
datos. La regla desde la fase 3.5: **nada de lo que dibuja la tarjeta es de maqueta**; si un dato falta, el test
`CatalogCompletenessTests` falla.

Leyenda de la columna *Origen*: **BD** = columna o tabla de SQL Server, **Calc.** = se calcula al consultar, **App** = el
dispositivo.

## Cabecera y pestañas

| Elemento | Origen | Dónde | Seed |
|---|---|---|---|
| "Cinerama⚡" | Fijo en la app | Título de la cabecera | — |
| Ciudad ("Chimbote") | BD | `Cinemas.City` → `GET /cities`. La cabecera muestra solo la ciudad. | Chimbote |
| Selector de ciudad (chevron) | BD | Mismas filas: una opción por ciudad con cines activos | 1 ciudad, 1 cine |
| Pestañas Cartelera / Preventa / Próximamente | Calc. | `Movies.ReleaseDate` + `Movies.PreSaleStartsOn` + hoy (RN-41) → `GET /movies?status=` | 8 / 1 / 0 películas (según la fecha de carga) |

## Tarjeta de cada película

| Elemento | Origen | Dónde | Seed |
|---|---|---|---|
| Póster (y los de los costados) | BD + archivo | `Movies.PosterUrl` → archivo en `media/posters/` | 9 pósters de marcador |
| Video corto en la tarjeta activa del carrusel y en el detalle (autoplay, sin sonido, en bucle) | BD + archivo | `Movies.PreviewVideoUrl` → `media/posters/<título>.mp4`; hoy solo "Verity: la sombra de un engaño" | 1 video de 6 s |
| Botón de reproducir (tráiler) → tráiler dentro de la tarjeta (zona de 261 × 200), con pantalla completa en horizontal desde el botón de YouTube | BD | `Movies.TrailerUrl`: código de YouTube validado (RN-51); el botón solo existe si hay uno válido | 5 de 9 películas |
| Comentarios (hoja): **solo** los del tráiler en YouTube; los de demostración de la base no se muestran | YouTube vía el servidor | `GET /movies/{id}/trailer-comments` (hasta 20; RN-52). Se ven solo con clave de YouTube | requiere `YOUTUBE__APIKEY` |
| Insignia "Preventa · estreno 11 oct" | Calc. | Estado + `Movies.ReleaseDate` | 2 en preventa, 2 próximamente |
| Título | BD | `Movies.Title` | 9 |
| Géneros · duración · clasificación | BD | `Genres.Name` (vía `MovieGenres`), `Movies.DurationMinutes`, `Movies.Classification` | 1 a 2 géneros por película |
| Sinopsis | BD | `Movies.Synopsis` | 9 |
| Calificación ("4.5") | Calc. | Promedio de `Reviews.Rating`, un decimal (RN-45) | 8 a 20 opiniones por película estrenada |
| Opinión destacada: autor, día, texto | BD + Calc. | `Reviews` (la más reciente con comentario) + `Users.FullName` abreviado (RN-44) | ~80 % de las opiniones traen comentario |
| Avatar de quien opinó | BD | `Users.AvatarUrl` (opcional; sin foto se muestra la inicial) | vacío |
| "Ver N opiniones" | Calc. | Cantidad de `Reviews` de la película → `GET /movies/{id}/reviews` | ~110 opiniones de demostración |
| "Película disponible en N cines" | Calc. | Cines con funciones próximas (RN-49) | 1 cine |
| Nombre y logo del cine | BD + archivo | `Cinemas.Name`, `Cinemas.LogoUrl` → `media/cinemas/` | Cinerama con su inicial |
| Dirección del cine | BD | `Cinemas.Address` | 2 |
| "A 1.1 km cerca de ti" | **App** | Distancia entre `Cinemas.Latitude/Longitude` y la ubicación del dispositivo (permiso del sistema, nunca se envía al servidor). Sin permiso se muestra la dirección (`Cinemas.Address`). "Cerca de ti" solo hasta 25 km. | 1 cine con coordenadas reales |
| Botón "Ver Precios y Horarios" | BD | `Showtimes` (precio, hora, sala, asientos libres) | ~475 funciones: los horarios reales de la página, repetidos 14 días |

## Detalle de la película y funciones

| Elemento | Origen | Dónde |
|---|---|---|
| Cine y sala de cada función | BD | `Showtimes → CinemaRooms → Cinemas` |
| Precio, hora, asientos libres | BD / Calc. | `Showtimes.Price`, `StartsAt`, `ShowSeats` disponibles (con la regla de bloqueos vencidos, RN-23) |
| Aviso de preventa / "se estrena el…" | Calc. | Estado + `ReleaseDate` |

## Lo que sigue pendiente (y por qué)

| Pendiente | Motivo | Quién decide |
|---|---|---|
| Cartelera de Cinestar Chimbote | Su página responde 403 (Cloudflare). Hay que copiar su contenido desde el navegador y pasarlo con `--extra` al importador. | Autor |
| Película "próximamente" | Cinerama hoy no publica estrenos futuros (su lista está vacía), así que esa pestaña sale vacía. | — |
| Director, reparto, distribuidora | Están en `cartelera.json`; agregarlos al modelo implica migración, API, app y pruebas. | Autor |
| Tráileres de 4 películas | La fuente (Cinerama) trae códigos vacíos ("e", "dsaf"): esas películas quedan sin tráiler y sin botón de play. | Autor (decir cuál es el tráiler correcto, o usar la API de búsqueda de YouTube con la clave) |
| Clave de la API de YouTube | Sin ella la sección de comentarios del tráiler no aparece. Ver `.env.example`. | Autor |
| Fotos de perfil de quienes opinan | La columna existe; falta la pantalla de perfil para subirlas. | Pendiente (perfil de usuario en la app) |
| Pantalla "Opiniones" | **Figma no la define.** Existe una versión provisional con los colores del diseño. | Autor (diseño) |
| Pantalla "Comunidad" | Solo existe la pestaña en el diseño. | Autor (diseño y alcance) |
| Subir imágenes desde el panel | **No incluido en la fase 4:** el panel guarda la *ruta* del póster, del video corto y del logo (RN-48), no sube el archivo. Los archivos se colocan en `backend/src/Cinerama.Api/media/`. Subirlos desde el panel requiere un endpoint de carga con validación de tipo y tamaño. | Autor (decidir si se hace) |

## Ubicación del dispositivo (privacidad)

- Al abrir Inicio la app pide el permiso de ubicación del sistema (aproximada, precisión baja). Si la persona lo niega o la
  ubicación está apagada, **todo sigue funcionando**: cada cine muestra su dirección en lugar de la distancia.
- La posición **solo se usa para calcular distancias en el dispositivo**. No se envía al servidor, no se guarda y no va en
  ninguna solicitud (la API solo recibe el nombre de la ciudad elegida).
- Una vez respondido el permiso no se vuelve a preguntar solo; el selector de ciudad ofrece "Usar mi ubicación" para pedirla de
  nuevo o, si el sistema la bloqueó, abrir los ajustes de la app.
- En el emulador de Android la ubicación por defecto es Mountain View (California): las distancias saldrán de miles de km. Se
  cambia en *Extended controls → Location* (por ejemplo a Chimbote: -9.0745, -78.5936).

## Video corto (tarjeta del carrusel y detalle)

- En Inicio, cuando la tarjeta activa tiene video, este reemplaza al póster (que se ve mientras carga o si falla). Solo reproduce la tarjeta activa: al pasar a Verity arranca, y al irse se libera. Si abres el detalle desde la tarjeta, el video de Inicio se pausa mientras está cubierto y continúa al volver.
- Pon el archivo en `backend/src/Cinerama.Api/media/posters/` con el nombre `<título>.mp4` (el mismo que el póster: por ejemplo `vertigo-2.mp4`), vuelve a ejecutar `python3 backend/tools/import_cartelera.py` y reinicia la base (`docker compose down -v` y `up -d`). El importador detecta los `.mp4` por nombre.
  O, sin reiniciar: `UPDATE Movies SET PreviewVideoUrl = N'posters/vertigo-2.mp4' WHERE Title = N'Vertigo 2';`
- Solo `.mp4` con H.264 (el que reproducen todos los Android y iOS). Mejor 16:9; si no lo es, se recorta para llenar el recuadro.
- El video no se registra en `pubspec.yaml`: no viaja dentro de la app, la app lo descarga de la API como los pósters.

## Cómo reemplazar las imágenes de muestra

1. Copia tu archivo a `backend/src/Cinerama.Api/media/posters/` con el **mismo nombre** que tiene hoy (por ejemplo `marea-alta.jpg`), o
   cambia `Movies.PosterUrl` en la base por la ruta de tu archivo (relativa a `media/`) o por una URL absoluta.
2. Con Docker Compose la carpeta está montada: no hace falta reconstruir. Solo `.jpg`, `.jpeg`, `.png` y `.webp`.
3. Las proporciones recomendadas: póster vertical 2:3 (el de muestra mide 600 × 900).
