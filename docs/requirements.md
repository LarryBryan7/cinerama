# Requerimientos

## 1. Contexto

Un cine de una sola sede, con varias salas, vende entradas en boletería y quiere venderlas también desde el celular. Hoy la venta presencial no ofrece:

- **Ver los asientos libres antes de llegar.** Los clientes llegan y la función preferida ya no tiene buenos lugares.
- **Garantizar que un asiento no se venda dos veces** cuando hay más de un canal de venta al mismo tiempo.
- **Saber cómo va el negocio** (ventas por día, ocupación por función y por sala) sin armar planillas a mano.

Cinerama resuelve esos tres problemas con:

- una app móvil para clientes;
- un panel web para la administración;
- una API que es la única fuente de verdad del estado de cada asiento.

> Cinerama es un **proyecto personal de portafolio**. El cine, sus datos, películas y usuarios son ficticios.

## 2. Actores

| Actor | Descripción | Canal |
|---|---|---|
| Visitante | Persona sin sesión iniciada. | App móvil |
| Cliente (`Customer`) | Usuario registrado que reserva entradas. | App móvil |
| Administrador (`Admin`) | Personal del cine que gestiona catálogo, salas, funciones y reportes. | Panel web |
| Sistema | Procesos automáticos, como la liberación de bloqueos vencidos. | API |

## 3. Glosario

| Término | Significado |
|---|---|
| Función (`Showtime`) | Proyección de una película en una sala, en una fecha y hora. |
| Asiento (`Seat`) | Posición física fija dentro de una sala (fila y número). |
| Asiento de función (`ShowSeat`) | Estado de un asiento para una función concreta: `Available`, `Locked` o `Booked`. |
| Bloqueo | Reserva temporal de un asiento (5 minutos) mientras el cliente completa la compra. |
| Reserva (`Booking`) | Compra confirmada de uno o más asientos de una función. |
| Cartelera | Películas activas con funciones futuras. |

## 4. Requerimientos funcionales

La prioridad sigue el método MoSCoW: **M** = Must, **S** = Should, **C** = Could. La fase corresponde al [roadmap](roadmap.md).

### Autenticación y cuenta

| ID | Requerimiento | Prioridad | Fase | Reglas |
|---|---|---|---|---|
| RF-AUTH-01 | Un visitante puede registrarse con nombre, email y contraseña. | M | 1 | RN-01, RN-02, RN-04 |
| RF-AUTH-02 | Un usuario puede iniciar sesión con email y contraseña. | M | 1 | RN-03, RN-06 |
| RF-AUTH-03 | La sesión se renueva sin pedir credenciales mientras el refresh token sea válido. | M | 1 | RN-05 |
| RF-AUTH-04 | Un usuario puede cerrar sesión, lo que invalida su refresh token. | M | 1 | RN-05 |
| RF-AUTH-05 | Un usuario puede consultar y editar su nombre (perfil). | S | 3 | — |

### Catálogo

| ID | Requerimiento | Prioridad | Fase | Reglas |
|---|---|---|---|---|
| RF-CAT-01 | Listar la cartelera con búsqueda por título, filtro por género y paginación. | M | 1 | RN-07 |
| RF-CAT-02 | Ver el detalle de una película con sus próximas funciones. | M | 1 | — |
| RF-CAT-03 | El administrador crea, edita y da de baja películas. | M | 4 | RN-08, RN-09 |
| RF-CAT-04 | El administrador gestiona géneros. | S | 4 | — |
| RF-CAT-09 | El administrador crea, edita y desactiva cines. | M | 4 | RN-49, RN-50 |
| RF-CAT-05 | Cada película tiene un estado de estreno (cartelera, preventa o próximamente) y la cartelera se puede filtrar por él. | M | 3.5 | RN-41 |
| RF-CAT-06 | Cines y ciudades: ver en qué cines se proyecta cada película, listar ciudades y filtrar por ciudad. | M | 3.5 | RN-49, RN-50 |
| RF-CAT-07 | Cada película muestra su sinopsis, póster, calificación promedio y una opinión destacada. | M | 3.5 | RN-44, RN-45, RN-48 |
| RF-CAT-08 | Ver el tráiler de la película en la tarjeta de Inicio (en el espacio del póster), a pantalla completa en horizontal si se quiere. | M | 3.5 | RN-51, RN-54 |
| RF-REV-01 | Leer las opiniones de una película, sin cuenta. | M | 3.5 | RN-44 |
| RF-REV-02 | Publicar, cambiar y borrar la opinión propia. | M | 3.5 | RN-43, RN-46, RN-47 |
| RF-REV-04 | Cumplir las condiciones de YouTube para el reproductor y para los comentarios (tamaño mínimo, nada superpuesto, no modificarlo, atribución, no mezclar fuentes, caché de 30 días como máximo). | M | 3.5 | RN-52, RN-54, RN-55 |
| RF-REV-03 | La hoja de comentarios muestra **solo** los comentarios del tráiler en YouTube (hasta 20; si hay menos, los que haya). Los comentarios de demostración de la base de datos no se muestran. | S | 3.5 | RN-52, RN-53 |

### Salas y funciones

| ID | Requerimiento | Prioridad | Fase | Reglas |
|---|---|---|---|---|
| RF-ROOM-01 | El administrador crea y edita salas. | M | 4 | RN-10 |
| RF-ROOM-02 | El administrador define el layout de una sala (filas, posiciones, pasillos). | M | 4 | RN-10, RN-11 |
| RF-SHOW-01 | Listar funciones por película y por fecha. | M | 1 | — |
| RF-SHOW-02 | El administrador programa funciones. | M | 4 | RN-12, RN-13, RN-14, RN-15, RN-56 |
| RF-SHOW-03 | El administrador cancela una función. | C | 4 | RN-17 |

> En las fases 1 a 3, las salas, películas y funciones se cargan con datos semilla. La administración llega en la fase 4. Así el flujo de reserva, que es el núcleo del producto, se puede probar antes.

### Asientos y reserva

| ID | Requerimiento | Prioridad | Fase | Reglas |
|---|---|---|---|---|
| RF-SEAT-01 | Ver el mapa de asientos de una función con el estado de cada asiento y cuáles bloqueó el propio usuario. | M | 2 | — |
| RF-SEAT-02 | Bloquear uno o más asientos durante 5 minutos. | M | 2 | RN-16, RN-18 a RN-21 |
| RF-SEAT-03 | Liberar asientos bloqueados por uno mismo. | M | 2 | RN-22 |
| RF-SEAT-04 | Obtener una recomendación de N asientos contiguos. | S | 2 | — |
| RF-SEAT-05 | Ver en tiempo real los cambios de estado de los asientos sin recargar la pantalla. | M | 3 | — |
| RF-BOOK-01 | Confirmar una reserva con los asientos bloqueados (pago simulado). | M | 2 | RN-24 a RN-28, RN-31 |
| RF-BOOK-02 | Listar mis reservas. | M | 2 | RN-32 |
| RF-BOOK-03 | Ver el detalle de una reserva con ticket y QR. | M | 2 | RN-32, RN-35 |
| RF-BOOK-04 | Cancelar una reserva. | M | 2 | RN-30, RN-32 |
| RF-BOOK-05 | El administrador lista todas las reservas con filtros (fecha, película, estado). | S | 4 | RN-33 |
| RF-BOOK-06 | Reservar asientos durante la preventa, antes del estreno. | M | 3.5 | RN-42 |

### Administración y reportes

| ID | Requerimiento | Prioridad | Fase | Reglas |
|---|---|---|---|---|
| RF-USR-01 | El administrador lista usuarios (con filtros por texto, rol y estado) y los activa o desactiva. | S | 1 (API), 4 (panel) | RN-03, RN-39 |
| RF-REP-01 | Dashboard con KPIs del periodo: ingresos, reservas, entradas vendidas y ocupación promedio. | M | 4 | RN-36 a RN-38, RN-57 |
| RF-REP-02 | Ventas por día en un rango de fechas. | M | 4 | RN-37, RN-38 |
| RF-REP-03 | Ventas e ingresos por película. | S | 4 | RN-37 |
| RF-REP-04 | Ocupación por función y por sala. | S | 4 | RN-36 |

### Conectividad limitada (app móvil)

| ID | Requerimiento | Prioridad | Fase |
|---|---|---|---|
| RF-OFF-01 | Consultar sin conexión la cartelera y el detalle de las películas ya vistas, indicando la fecha de la última actualización. | M | 5 |
| RF-OFF-02 | Consultar sin conexión mis reservas y mostrar el ticket con su QR. | M | 5 |
| RF-OFF-03 | Mostrar un indicador visible cuando no hay conexión y deshabilitar las acciones que la requieren. | M | 5 |
| RF-OFF-04 | Al recuperar la conexión, actualizar los datos locales con los del servidor, que es la fuente de verdad. | M | 5 |

### Procesos del sistema

| ID | Requerimiento | Prioridad | Fase | Reglas |
|---|---|---|---|---|
| RF-SYS-01 | Liberar periódicamente los bloqueos vencidos y notificar el cambio a los clientes conectados. | M | 2 (liberación), 3 (notificación) | RN-23 |

## 5. Requerimientos no funcionales

| ID | Categoría | Requerimiento | Cómo se verifica |
|---|---|---|---|
| RNF-01 | Integridad | Un asiento nunca queda en dos reservas activas, ni siquiera ante solicitudes simultáneas. | Test de concurrencia + índice único filtrado |
| RNF-02 | Seguridad | Contraseñas con hash y sal (PBKDF2). Nunca se registran en logs. | Revisión + unit test |
| RNF-03 | Seguridad | Toda ruta exige autenticación salvo las marcadas explícitamente como públicas (seguro por defecto). | Test que recorre los endpoints registrados |
| RNF-04 | Seguridad | Los secretos (claves JWT y HMAC, contraseña de BD) se leen de variables de entorno o user-secrets, nunca del repositorio. | `.gitignore` + revisión |
| RNF-05 | Seguridad | Panel web: el access token vive solo en memoria y el refresh token en una cookie `HttpOnly`, `Secure`, `SameSite=Strict`. | ADR-006 + test manual |
| RNF-06 | Seguridad | CORS restringido a los orígenes configurados, con credenciales solo para ellos. | Test de integración |
| RNF-07 | Rendimiento | Objetivo a validar: el mapa de asientos responde en menos de 200 ms (p95) con el volumen del seed de rendimiento en el entorno local. | k6 (fase 6). No se publica ningún número sin medirlo. |
| RNF-08 | Rendimiento | Los listados están paginados (máximo 100 elementos por página) y usan proyecciones sin seguimiento de cambios (`AsNoTracking`). | Revisión de código |
| RNF-09 | Escalabilidad | La API funciona con más de una instancia sin perder notificaciones en tiempo real. | Demo de 2 instancias (fase 6) |
| RNF-10 | Disponibilidad | La API expone `/health` con el estado de SQL Server y, si está configurado, de Redis. | Healthcheck de Docker |
| RNF-11 | Mantenibilidad | Monolito modular por capas. Las reglas de dominio no dependen de EF Core ni de ASP.NET Core. | Referencias entre proyectos |
| RNF-12 | Testabilidad | Las reglas de negocio tienen tests unitarios y los flujos críticos tienen tests de integración contra SQL Server real (Testcontainers). | CI |
| RNF-13 | Accesibilidad | El estado de un asiento no se comunica solo por color: también usa ícono o patrón y etiqueta semántica para lectores de pantalla. | Revisión con TalkBack/VoiceOver |
| RNF-14 | Portabilidad | El backend completo (SQL Server, Redis y API) se levanta con `docker compose up`. | README |
| RNF-15 | Observabilidad | Logs estructurados con el identificador de correlación de cada request. Los errores 500 no exponen el stack trace al cliente. | Revisión + test |
| RNF-16 | Privacidad | Solo datos ficticios. No se almacenan datos de pago. | Revisión del seed |
| RNF-17 | Documentación | API documentada con OpenAPI/Swagger. Las decisiones técnicas relevantes están en un ADR. | `docs/adr` |

## 6. Fuera de alcance

- Pagos reales, pasarelas de pago y almacenamiento de tarjetas.
- Varias sedes o cadenas de cines (multi-tenant).
- Precios por tipo de asiento, promociones y combos.
- Notificaciones push y correo electrónico.
- Venta presencial en boletería (se asume que existe, pero no se modela).
- Reservas offline. Cualquier bloqueo o confirmación requiere conexión con el servidor.
