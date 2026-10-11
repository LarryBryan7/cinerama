# Interfaz móvil a partir de Figma

Cómo se construye la UI de la app Flutter y qué reglas se aplican cuando el diseño y el backend no coinciden.

## Método

1. Se lee el nodo real de Figma (posición, tamaño, rellenos, trazos, radios, fuentes, interlineado). Las capturas son solo referencia visual.
2. Cada medida se aplica tal cual: márgenes, radios, colores exactos, tamaños y pesos de fuente.
3. Si Figma cambió desde la última vez, se vuelve a medir. No se reutilizan medidas de memoria.
4. Tras cada cambio se corre `flutter analyze` y se reporta el resultado completo.
5. No se compila ni se ejecuta la app sin avisar antes.

## Regla fija: contenido de Figma sin respaldo en el backend

Cuando el diseño muestra algo que el modelo de datos todavía no expone, **se maqueta tal cual el diseño con valores de muestra**
y no se espera al backend. Todo el contenido de muestra vive en un solo archivo por pantalla, con un comentario que lo declara
maqueta, y los widgets no contienen textos inventados.

**Estado desde la fase 3.5 (decisión del autor, 2026-10-07: el backend se completa y la regla deja de aplicarse a la tarjeta de
cartelera).** El backend ya expone sinopsis, póster, calificación, opinión destacada, cines, ciudad y estado de estreno, y la
tarjeta los muestra con datos reales ([cobertura](ui-data-coverage.md)). `cartelera_mock.dart` quedó solo con los pósters de
respaldo para una película sin imagen o con imagen que no se descarga. Un test comprueba que ningún texto de maqueta
sobrevive en pantalla.

La **distancia** ("A 1.1 km cerca de ti") ya sale de la ubicación del dispositivo (ver [cobertura](ui-data-coverage.md)). Siguen
pendientes las pantallas **Opiniones** y **Comunidad**, que no están en Figma (la primera tiene una versión provisional; la
segunda es un marcador).

## Convenciones de implementación

- `core/theme/cine_tokens.dart`: colores exactos de Figma y `cineText()` (Inter, interlineado automático).
- `core/theme/figma_icons.dart`: íconos con la geometría exportada de Figma, dibujados con `CustomPainter` (sin dependencias nuevas).
- `core/widgets/design_canvas.dart`: lienzo de 390 px con `Positioned` en coordenadas absolutas de Figma.
  - Pantalla más ancha: el lienzo queda centrado y a tamaño real.
  - Pantalla más angosta: se reduce de forma proporcional.
  - El texto no sigue la escala de fuente del sistema dentro del lienzo, porque las cajas de Figma son fijas.
- Fuente: Inter 3.19 (la versión de Figma, con eje `slnt`) en `mobile/assets/fonts/`. Licencia OFL.

## Adaptaciones respecto al archivo de Figma

| Elemento | Figma | Flutter | Motivo |
|---|---|---|---|
| Cabecera | Esquinas superiores de 36 px | Sin radio; se extiende bajo la barra de estado | El radio de 36 es el del marco del dispositivo del mockup |
| Barra de estado y gesto inferior | No existen en el frame | Se suma el inset del sistema arriba y abajo | Los frames miden 390×736 sin áreas seguras |
| "A 7.2 km **cercs** de ti" | Error de tipeo | "cerca" | Corrección ortográfica |
| Nombre "Cinerama - Chimbote" | Texto convertido a trazos | Texto Inter SemiBold 10 | Misma fuente y tamaño; ajustado 0.62 px en Y y 0.53 px en X |
| Duración | "1hrs 57min" | `Formatters.durationCompact` produce el mismo formato | Tal cual el diseño; cambiar el formato es una línea |
| Pestañas Entradas / Comunidad / Perfil activas | Solo existe Inicio activa | Mismo tratamiento (píldora, ícono lima, etiqueta lima oscuro) | Suposición hasta tener el diseño |
| Pantalla Comunidad | Solo existe la pestaña | Marcador con el texto "Comunidad" | Sin diseño ni funcionalidad |
| Insignia "Preventa · estreno 11 oct" | No existe | Etiqueta lima sobre el póster | Preventa y próximamente necesitan mostrar la fecha de estreno |
| Segunda línea de cada cine | "A 7.2 km cerca de ti" | Distancia real si hay ubicación; si no, la dirección. "cerca de ti" hasta 25 km | La distancia necesita el permiso de ubicación; sin él no se inventa |
| Película y sus vecinas | Tres rectángulos de póster fijos | Carrusel de tarjetas (`PageView`): se arrastran, las vecinas asoman a 19 px con menos opacidad (65 %) y tamaño (95 %), y tocar una la centra | Figma muestra un estado; el carrusel es la interacción del diseño |
| Pestañas Cartelera / Preventa / Próximamente | Solo dibujadas | Tocables: cada una pide su estado | Filtran de verdad desde la fase 3.5 |
| Pantalla Opiniones | No existe | Versión provisional con los colores del diseño | Hace falta para leer y escribir opiniones |

## Observaciones del diseño (se replican tal cual, por si se quieren corregir en Figma)

- Botón "Ver Precios y Horarios": x=53..345 (292 px). La tarjeta mide 53..337 y el chip de cines 53..339. El botón queda 4 px a la derecha del centro.
- Contenido de la tarjeta: margen izquierdo 9 px y derecho 14 px (imagen de 261 px dentro de 284).
- Barra inferior: el centro de cada etiqueta no coincide con el de su ícono (Comunidad: 8 px; Entradas: 3 px; Perfil: 1.5 px).
- Chip "Ver 20 opiniones más": contiene un "Hoy" oculto fuera del frame. No se dibuja.

## Cambios del 2026-10-08 (frames "Cartelera - dark - 2" y "BottonSheet - Comment")

Tarjeta de cartelera (Figma "Cartelera - dark - 2"):

- La tarjeta mide 328 px de alto (antes 347). La opinión destacada, el aviso de cines, el chip de cines y el botón se reubicaron con las coordenadas nuevas.
- Botón de reproducir: disco de 28 px con efecto "Glass" (relleno #D9D9D9 al 50 %) y triángulo #E8E8E8. **El efecto de vidrio no existe en Flutter**: se aproxima con el relleno translúcido y un borde de luz de 1 px (más intenso arriba a la izquierda y abajo a la derecha). No hay refracción ni dispersión.
- Contador de video "1:37" sobre la imagen (píldora de 42×16, #0E0E0E al 95 %). **Valor de maqueta** (`CarteleraMock.videoLength`): el servidor no guarda la duración del video ni del tráiler. Se muestra solo en películas con video o tráiler. Decisión del autor: "solo diseña, lo veré luego".
- La duración se escribe "1h 57m" (antes "1hrs 57min").
- La sinopsis se corta a tres líneas y termina en "... Ver más información" (Regular 12, subrayado). Si cabe completa, se muestra entera y el enlace va a continuación. El enlace abre el detalle de la película; Figma no define su destino (el frame "BottonSheet - Detail" no se aplicó).
- Figma escribe ".. " (dos puntos) antes del enlace; se usan tres.

Comentarios (Figma "BottonSheet - Comment"), `ReviewsPage`:

- Es una hoja que sube sobre la pantalla anterior, **incluida la barra inferior**, por eso la ruta `/movies/:id/reviews` es transparente y de pantalla completa. Detrás: 70 % de #0E0E10 y desenfoque de 30 (sigma 15). Se cierra tocando el fondo o arrastrando la cabecera hacia abajo.
- Muestra comentarios (opiniones con texto); una opinión que solo puntúa no aparece. Antigüedad como en Figma: "2d", "1 sem" (`Formatters.commentAge`; las demás unidades siguen ese estilo).
- El corazón con contador es **maqueta** (`CarteleraMock.commentLikes`): no existen "me gusta" en el servidor.
- La barra de abajo es el acceso a escribir. Hoy lleva al login (sin sesión) o al formulario con estrellas (con sesión), porque una opinión exige puntuación. **Pendiente de decisión y trabajo de backend:** el autor eligió "comentarios sin puntuación", es decir, comentarios independientes de la calificación (nueva tabla, endpoints, reglas, semilla, pruebas y qué muestra la tarjeta como opinión destacada).
- Avatar propio: la inicial del nombre (la cuenta todavía no tiene foto).

Adaptaciones y observaciones de este lote:

| Elemento | Figma | Flutter | Motivo |
|---|---|---|---|
| Hoja, esquinas inferiores | Radio 20 en las cuatro | Radio 20 solo arriba | La hoja llega al borde de la pantalla; el radio de abajo es del mockup |
| Placeholder del campo | "Que te pareció esta película?" | "¿Qué te pareció esta película?" | Corrección ortográfica |
| Pastilla de arrastre | x=176..211 | Igual | Queda 1.5 px a la izquierda del centro de la pantalla |
| Antigüedad junto al nombre | Posición fija (x=114 y x=104) | Separación de 6 px tras el nombre | El nombre es dato real; Figma la fija a mano, hasta 1 px de diferencia |
