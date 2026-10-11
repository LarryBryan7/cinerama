# ADR-017: Estreno derivado, cines como entidad, opiniones y medios (fase 3.5)

- **Estado:** Aceptada
- **Fecha:** 2026-10-07

## Contexto

El diseño de la pantalla de inicio de la app (Figma) muestra cosas que el backend no tenía: calificación, opinión destacada,
cines con su ubicación, ciudad, pestañas *Cartelera / Preventa / Próximamente* y pósters. La regla anterior era dibujarlas con
datos de muestra. El autor decidió completar el backend: **todo lo que muestra la pantalla debe estar en la base de datos** y
salir de la API, para que las imágenes y los textos reales solo tengan que reemplazar archivos o filas.

Decisiones del autor para esta fase: seguir con el nombre **Cinerama** (la cabecera de la app muestra "Cinerama⚡"), cualquier persona con cuenta puede opinar, y en preventa **sí se reservan asientos** antes del estreno.

## Decisión

1. **El estado de una película se deriva de dos fechas** (`ReleaseDate`, `PreSaleStartsOn`) y de "hoy" en el calendario del
   cine. No se guarda una columna `Status`: no puede quedar desactualizada ni contradecir las fechas. Un `CHECK` impide una
   preventa posterior al estreno. La venta sigue la misma regla (RN-42): abre con la preventa o, sin ella, con el estreno.
2. **El cine es una entidad** (`Cinemas`) y cada sala le pertenece. Las ciudades salen de los cines activos (columna `City`).
   Las coordenadas viajan en la API, pero **la distancia se calcula en el dispositivo**: la ubicación de una persona no
   debe viajar ni guardarse en el servidor.
3. **Las opiniones son una tabla** con una por persona y película (índice único). El promedio y la cantidad **se calculan al
   consultar**, sin columnas desnormalizadas. La opinión destacada es la más reciente con comentario. El autor se muestra
   abreviado ("Lucía R.").
4. **Las imágenes se guardan como ruta relativa o URL absoluta y la API las entrega siempre absolutas**, desde una carpeta
   de medios (`/media`) servida como archivos estáticos, solo para tipos de imagen. Con una URL base configurable.
5. **Cada tarjeta se arma con un número fijo de consultas** (películas, y un lote por cada dato extra de todas las películas
   de la página), no con una consulta por película.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Columna `Movies.Status` | Se desincroniza con las fechas (hay que cambiarla cada día) y exige un proceso que la actualice. Derivarla es una comparación de fechas. |
| Calificación y cantidad desnormalizadas en `Movies` | Pueden quedar mal si una escritura falla a medias o se borra una opinión. Con el índice `(MovieId, CreatedAt) INCLUDE (Rating)` el cálculo es barato; se revisa con mediciones en la fase 6. |
| Tabla `Cities` | Con un puñado de sedes es una tabla más sin comportamiento propio. Se normaliza si aparece la administración de ciudades. |
| Enviar la ubicación del usuario para calcular distancias en el servidor | Expone un dato personal sin necesidad. El dispositivo ya tiene su ubicación y las coordenadas del cine. |
| Guardar las imágenes en la base (`varbinary`) o en un servicio externo | Complica copias y consultas para unos pocos archivos. Una carpeta montada permite reemplazar una imagen sin reconstruir nada. Un servicio de almacenamiento de objetos es la evolución natural en producción. |
| Opinar solo con una reserva de esa película | Más creíble pero más trabajo. El autor decidió que opine cualquier persona con cuenta. |

## Consecuencias

- **Una película próxima no tiene funciones** (el seed solo programa dos semanas) y por eso `cinemas` viene vacío en esa pestaña.
- **Dos barreras en las opiniones**, como en las reservas (ADR-004): la búsqueda previa y el índice único `(MovieId, UserId)`.
  Si dos primeras opiniones de la misma persona compiten, la perdedora recupera la excepción y actualiza. Esto se prueba de
  forma **determinista** (ver más abajo): con hilos reales la carrera casi nunca ocurre.
- Las URL de imagen se arman con la dirección de la solicitud. Detrás de un proxy hay que configurar `Media:PublicBaseUrl` (o
  reenviar las cabeceras `X-Forwarded-*`), o las URL apuntarían al host interno.
- El cine **Cine Star** y su logo corresponden a una cadena real; el autor decidió conservarlos tal como aparecen en el
  diseño. El logo está en `media/cinemas/cine-star.png` y su dirección es referencial. Es una decisión a revisar si el
  repositorio se publica con fines comerciales.
- La cabecera de la app muestra "Cinerama⚡" y, junto al icono de ubicación, la ciudad con su región ("Chimbote, Áncash").
- Los pósters del seed son imágenes generadas (título sobre un degradado), no arte real: se reemplazan copiando archivos
  con el mismo nombre a `backend/src/Cinerama.Api/media/posters/`.

## Evidencia

| Qué se prueba | Dónde |
|---|---|
| Estado derivado, frontera ("estrena hoy" es cartelera), combinaciones válidas | `MovieStatusTests` (Unit), `MovieStatusAndMediaTests` |
| Venta cerrada antes de la preventa, abierta durante ella, sin cambios en base ni avisos | `PreSaleSalesTests` |
| Cines por película, ciudades, filtro por ciudad que no oculta "próximamente", cines inactivos | `CinemaTests` |
| Promedio con un decimal, destacada, privacidad del autor, una opinión por persona, validaciones, película sin estrenar | `ReviewTests` |
| Carrera de primeras opiniones (determinista) | `ReviewRaceTests` |
| Todos los datos que necesita cada pantalla existen y sus imágenes también | `CatalogCompletenessTests` |
| La app muestra los datos reales y ninguno de maqueta; pestañas, ciudad y opiniones | `cartelera_slide_test`, `reviews_and_home_test`, `movies_cubits_test` (Flutter) |

**Mutaciones** (se rompió cada mecanismo y se midió qué tests fallan):

| Mutación | Resultado |
|---|---|
| La guarda de venta no hace nada | 3 tests fallan (los de "antes de abrir la preventa") |
| "Estrena hoy" deja de ser cartelera | 1 test (`EachFilter_ReturnsOnlyItsOwnState…`) |
| Se puede opinar sin estreno | 1 test (`OnlyAMovieThatAlreadyPremiered…`) |
| El promedio ignora las puntuaciones de 1 | 1 test (`DeletingAReview_RemovesItFromTheAverage`) |
| Falta el archivo del póster de una película | 1 test, con el nombre de la película y la URL |
| Se quita la búsqueda previa de la opinión | **0 fallos, y es correcto**: el índice único y la recuperación hacen que el resultado sea el mismo (defensa en profundidad; la búsqueda es una optimización) |
| Se quita la recuperación ante el índice único | 0 fallos con ocho solicitudes simultáneas (la carrera no se produce) → **se agregó la prueba determinista, que sí falla** |

Esa última fila repite la lección de ADR-004: una prueba con hilos puede pasar sin que la protección funcione. La carrera se
provoca con un interceptor que inserta la opinión competidora justo antes del guardado.

## Actualización: ubicación del dispositivo y carrusel

- La distancia a cada cine **ya se calcula en el dispositivo** (haversine) con la ubicación aproximada del sistema
  (`geolocator`, precisión baja). El permiso se pide al abrir Inicio; sin permiso se muestra la dirección y nada se inventa. La
  posición no viaja al servidor ni se guarda. "Cerca de ti" se dice solo hasta 25 km; el aviso "disponible en N cines cerca de
  ti" cuenta los cines de la ciudad elegida.
- La cabecera muestra solo la ciudad. La ciudad se elige a mano: con una sola ciudad no hay nada que detectar; elegir la más
  cercana a la posición es agregar coordenadas a `/cities` cuando haya varias.
- La tarjeta es un carrusel (`PageView`) y no una sola tarjeta que cambia: las vecinas se arrastran y asoman a los lados.

## Actualización (2026-10-07): catálogo con datos reales

- Por decisión del autor, el catálogo deja de ser inventado: son las películas y los horarios reales de **Cinerama Chimbote**,
  leídos del endpoint público que usa su propia página (`backend/tools/import_cartelera.py`), solo texto, en minúsculas con la primera letra en mayúscula ("mayores de 14" se muestra como "+14"). El
  resultado queda en `cartelera.json` (recurso incrustado) para que el seed no dependa de internet.
- Cinerama publica los horarios solo del día de la consulta: el seed los repite 14 días y asigna cada uno a la primera sala libre.
  Hacen falta 7 salas para que no se solapen (con 6 se pierden 3 funciones por día); la distribución de las salas y los precios
  son de demostración. La preventa se marca en el título de la página ("(preventa)"); sus funciones empiezan en el estreno.
- **Cinestar** (cadena real) no se importa: su página está detrás de una verificación anti-bots de Cloudflare y no se evade.
  Se agrega con `--extra <archivo.json>` cuando haya contenido copiado desde el navegador.
- Las coordenadas del cine son las reales (OpenStreetMap, nodo "Cinerama", Av. José Pardo, Chimbote), con 6 decimales (~10 cm).
- El importador **no descarga imágenes**: los pósters siguen siendo marcadores generados.

## Actualización (2026-10-08): tráiler y comentarios de YouTube

- **Reproducción:** el tráiler se muestra con el reproductor **oficial** de YouTube (IFrame, privacidad mejorada). Es la forma permitida de insertar un tráiler: la app no descarga ni guarda el video, solo la URL (`TrailerUrl`). Un código de video solo se acepta si tiene 11 caracteres válidos; la fuente (Cinerama) a veces trae basura (`e`, `dsaf`), y esas películas quedan sin tráiler y sin botón.
- **El tráiler suena dentro de la tarjeta del carrusel, en un reproductor de 261 × 200 (decisión del autor, 2026-10-08).** Las condiciones de YouTube (Required Minimum Functionality, leídas ese día) exigen un reproductor de **al menos 200 × 200 px**, prohíben mostrar *"overlays, frames, or other visual elements in front of any part of a YouTube embedded player, including player controls"* y no permiten ocultar su logo (`modestbranding` está deprecado; el título y el logo siempre aparecen). El póster mide 261 × 140, por debajo del mínimo; por eso, **mientras suena el tráiler, la zona del póster crece hacia abajo a 261 × 200** y tapa el título y parte de la sinopsis (al cerrar, la tarjeta vuelve a verse entera). El botón de cerrar va **debajo** del reproductor, no encima. El video corto propio (`PreviewVideoUrl`), sin marcas de YouTube, sigue siendo lo que se ve en reposo. Historia: una primera versión puso el reproductor en 261 × 140 con botones encima (incumplía el tamaño y la superposición, y el video, que en móvil se dibuja en la capa superior de la app, tapaba esos botones); una segunda pasó directo a pantalla completa; el autor prefirió que el tráiler siguiera en el carrusel.
- **La pantalla completa es de la app, no la del paquete, y no toca el reproductor:** el paquete (v6) hace la suya dentro de la misma pantalla y **no gira el dispositivo**; la app necesitaba girar a horizontal, ocultar barras y devolver todo al salir. Una versión anterior interceptaba el botón de pantalla completa de YouTube (un controlador que avisaba en vez de maximizar), pero eso es "modificar la funcionalidad del reproductor" (III.I.6). Ahora el botón de YouTube se apaga con su parámetro documentado `fs=0` y la app ofrece **su propio botón debajo del reproductor**, que abre la ruta propia (horizontal, barras ocultas, X en una franja lateral) con un segundo reproductor que sigue desde el segundo en que iba. Inicio queda debajo, intacto.
- **El WebView queda tras una interfaz (`TrailerPlayer`)**: un WebView no existe en las pruebas; así se prueban los recorridos (abrir, salir, terminar, error) con uno simulado.
- **La hoja de comentarios muestra solo los de YouTube (decisión del autor, 2026-10-08):** los comentarios de la comunidad que genera el seed son de demostración y no son de nadie, así que no se listan. Lo que escribe una persona se guarda (cuenta en la puntuación y en la opinión destacada) pero no aparece en la hoja. Las opiniones de demostración siguen alimentando la calificación y la opinión destacada de la tarjeta.
- **Comentarios:** los trae el servidor (`commentThreads.list`, 1 unidad de cuota), nunca la app: la clave no debe viajar al móvil. Va en la cabecera `x-goog-api-key` y no en la URL, para que no caiga en los registros. Se piden siempre 20 y se guardan 6 h (5 min si falló), de modo que 5, 10 o 20 comentarios cuestan una sola consulta. Los términos de YouTube piden no conservar comentarios por mucho tiempo; el caché vive solo en memoria.
- **YouTube nunca es un error:** sin clave, cuota agotada, comentarios cerrados o red caída se informan en `status` y la app oculta la sección.
- **Privacidad de los autores:** a diferencia de las opiniones de la app (RN-44, nombre abreviado), aquí se muestra el nombre público que YouTube publica (casi siempre un alias `@usuario`), con su foto solo si viene por https. Es información que su autor ya hizo pública en YouTube. Si se prefiere abreviar o anonimizar, es un cambio en `YouTubeCommentsClient.Parse`.
- **No verificado de punta a punta:** la llamada real a YouTube y el WebView en un dispositivo se probaron solo con simulacros; necesitan una clave y un emulador.

### Cumplimiento de las condiciones de YouTube (revisado 2026-10-08)

| Regla | Qué se hace |
|---|---|
| Reproductor ≥ 200 × 200 | 261 × 200 en la tarjeta mientras suena; en pantalla completa, 16:9 a la altura de la pantalla. |
| Nada encima del reproductor ni de su logo | Los botones (pantalla completa, cerrar, X de la pantalla completa) están fuera del video. Un reproductor en 261 × 140 con botones encima se descartó. |
| No modificar el reproductor (III.I.6) | Solo parámetros documentados (`controls`, `fs=0`, `iv_load_policy=3`, `rel=0`, privacidad mejorada). Ya no se intercepta ningún botón. Lo comprueba una prueba sobre la constante `trailerPlayerParams`. |
| No ocultar el logo/título | No se tocan: se ven. |
| Atribución (III.F.2) | La hoja dice "Comentarios del tráiler en YouTube" y "Fuente: YouTube. Se muestran tal como los publicaron." No se agregó el **logo** de YouTube: sus directrices de marca piden usar los archivos oficiales sin modificar y no se dibujó uno propio. |
| No mezclar fuentes (III.C.5) | La hoja es solo de YouTube. El botón que la abre y la fila del detalle ya no llevan la cuenta de opiniones de la app, y la calificación de la app va aparte (RN-55). |
| No descargar ni separar audio/video (III.E.1, III.I.7) | Solo se inserta el reproductor. |
| Caché ≤ 30 días (III.E.4.c) | 6 horas, en memoria. |
| Un reproductor con autoplay a la vez | El de la tarjeta se pausa mientras el de pantalla completa suena. |

*Pendiente:* la opinión destacada de la tarjeta y la calificación siguen saliendo de las opiniones de demostración de la app (otra fuente, rotuladas como "de la app" donde conviene); si algún día se publica, conviene quitar esos datos generados.

## Motivación de portafolio

**Producto.** Es lo que pide el diseño. Las decisiones de privacidad (autor abreviado, ubicación en el dispositivo) y de
integridad (estado derivado, índice único) son las que se tomarían en producción.
