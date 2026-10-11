# Verificación de la fase 4: recorrido real

Fecha: 2026-10-09. Qué se ejecutó de verdad, con qué resultado y qué quedó fuera. Complementa las pruebas automatizadas (que usan una API simulada en el panel) con un recorrido **sin mocks**: Chrome → panel (Vite, `localhost:5173`) → API (Docker Compose, `localhost:8090`) → SQL Server.

## Entorno

- `docker compose up -d --build`: API y SQL Server sanos. La migración `AddSalesReporting` se aplicó al arrancar sobre la base existente **sin borrar datos** (9 películas, 490 funciones y 21 usuarios antes y después; la vista `vw_ShowtimeSales` y el procedimiento `sp_GetSalesSummary` presentes).
- Chrome 100 % real, en modo sin interfaz, dirigido con Playwright. El navegador se configuró en la zona `Asia/Tokyo` para comprobar que las horas se escriben y se muestran en la hora del cine (Lima) y no en la del navegador.
- Las comprobaciones de persistencia se hicieron con consultas directas a SQL Server.
- Los datos de prueba llevan el prefijo `ZZ E2E` y se borraron al terminar.

## Qué se verificó (resultados)

| Área | Verificado en el recorrido real | Resultado |
|---|---|---|
| Sesión | Ruta protegida → login y vuelta a la ruta pedida; credenciales incorrectas (mensaje genérico); campos vacíos; recarga conserva la sesión; cerrar sesión; sin cookie la recarga lleva al login | ✔ |
| Seguridad de la sesión | Ningún token en `localStorage`/`sessionStorage`; cookie `cinerama_rt` `HttpOnly`, `SameSite=Strict`, `Path=/api/v1/auth`; no legible desde JavaScript; la cookie se borra al salir | ✔ |
| Roles | Cuenta de cliente rechazada en el panel y su sesión revocada; con token de cliente 6 rutas de administración → `403`; sin token → `401`; CORS no responde a un origen ajeno | ✔ |
| Renovación de sesión | Un `401 token_expired` (inyectado en una solicitud) provoca un refresh **real** con la cookie, que rota, y la solicitud se repite; dos solicitudes expiradas a la vez comparten un solo refresh | ✔ |
| Géneros | Alta, nombre repetido con otra capitalización (error en el campo), renombrar, no borrar uno con películas | ✔ |
| Cines | Alta, latitud fuera de rango rechazada antes de enviar, nombre repetido | ✔ |
| Salas | Alta con pasillo (12 asientos en SQL, ninguno en la columna del pasillo), renombrar | ✔ |
| Películas | Validación, alta con género y fechas, edición, baja lógica (oculta de la cartelera pública, aparece en «Solo de baja»), tráiler con adornos guardado como enlace limpio, tráiler ajeno a YouTube → `400` | ✔ |
| Funciones | Validación; **20:00 en Lima con el navegador en Tokio = 01:00 UTC** en SQL y 20:00 en pantalla; fin = inicio + duración + 15 min; 12 asientos generados; traslape, antes del estreno y en el pasado con el error junto al campo y nada guardado; límite exacto de la limpieza (21:54 se rechaza, 21:55 se permite); edición sin reservas | ✔ |
| Restricción de salas | Con una función futura: editor de layout bloqueado, `PUT layout` → `409 room_has_future_showtimes`, desactivar la sala → error y sigue activa en SQL; al cancelar la función el layout vuelve a ser editable | ✔ |
| Reservas | Reservas reales de un cliente; la función con reservas no se edita (`409 showtime_has_bookings`); buscar por código; cancelar desde el panel (SQL: reserva `Cancelled`, asientos `Available`); cancelar de nuevo → `409` | ✔ |
| Cancelar función | Advertencia con el número de reservas; SQL: función, reserva y asientos coherentes; el cliente ve su reserva cancelada | ✔ (tras corregir el defecto 1) |
| Editor de asientos | Cuadrícula, teclado (flechas y Enter), guardar y revertir con efecto en SQL, rango de filas | ✔ |
| Usuarios | Búsqueda y filtros; no se puede desactivar la propia cuenta (UI y `422` en la API); desactivar → `IsActive = 0`, refresh tokens revocados, ya no inicia sesión; reactivar | ✔ |
| Reportes | KPIs del panel = API = vista SQL (ingresos, reservas, entradas, funciones, ocupación) con una reserva cancelada que no cuenta; día de la función en la hora del cine; rango inválido y de más de 366 días; ocupación por sala; ventas por película | ✔ |
| Dashboard | Tarjetas del día y próximas funciones en la hora del cine | ✔ |
| Responsive | 390 px: botón Menú, sin desborde horizontal | ✔ |
| Servidor | Registro de la API: 0 entradas de nivel Error; solo errores de dominio esperados (`409`/`422`/`401`) | ✔ |

## Defectos hallados y corregidos

1. **Advertencia de cancelación desactualizada (panel).** El diálogo de cancelar una función contaba las reservas con los datos de la lista, que podía llevar horas abierta sin refrescarse: si un cliente reservaba después, decía «No tiene reservas confirmadas» y al confirmar se cancelaban igualmente. **Corrección:** el diálogo consulta la función al abrirse y no permite confirmar hasta tener ese dato; además las listas se actualizan al volver a la pestaña. **Pruebas:** dos nuevas en `ShowtimesPage.test` (con mutación comprobada).
2. **Advertencia de EF Core al arrancar (API).** `SalesSummaryRow` no declaraba la precisión de sus `decimal` y EF avisaba de un posible truncamiento. Los valores salían bien (la consulta no pasa por una columna), pero el aviso indicaba una trampa futura. **Corrección:** `[Precision]` en las propiedades. **Prueba:** `TheAverageOccupancyKeepsItsSixDecimals_AndTheMoneyItsTwo`. La advertencia ya no aparece.
3. **Texto engañoso (panel).** El campo del tráiler decía «se guarda solo el código»; en realidad se guarda el enlace limpio. Texto corregido, y también la descripción de TC-ADM-04.

## Observaciones sin cambio de código

- **Limitador de `/auth/*`:** ver la sección siguiente («Revisión del limitador de `/auth/*`»). Sin cambios de código.
- **Una función nueva no se ve en la primera página de la lista** (orden por fecha, 20 por página, hay cientos de funciones): hay que filtrar por película. Posible mejora de usabilidad; no es un error.
- El resumen «Hoy» del dashboard y los reportes agregan **todos los cines**: la ocupación promedio de un día con una sola función vendida es baja porque su capacidad se suma a la de todas las demás.
- En un viewport de 1280 px la tabla de ventas por película quedaba estrecha: las dos columnas de reportes ahora solo se ponen lado a lado desde 1536 px.

## Revisión del limitador de `/auth/*` (decisión: sin cambios)

**Pregunta:** ¿el refresh en cada carga del panel puede provocar cierres de sesión en recargas normales?

**Medido en el navegador real** (`t7`, sobre la API en Docker):
- Una carga o recarga hace **una** solicitud a `/auth/refresh`, también en desarrollo con `StrictMode`: el refresh de un solo vuelo (`refreshing ??=`) hace que la segunda ejecución del efecto reutilice la solicitud en curso. (Una versión anterior de este documento decía «dos en desarrollo»; era incorrecto.)
- El límite es de 10 solicitudes por minuto **por IP**, compartido por `register`, `login`, `refresh` y `logout` (RN-06). Una persona necesita unas 9 recargas en un minuto para alcanzarlo.
- Si se alcanza, el refresh recibe `429` y el panel muestra el login, pero **la sesión no se pierde**: la solicitud se rechaza antes de llegar al controlador, así que el refresh token no se rota ni se revoca (la cookie conservó el mismo valor). Pasado el minuto, recargar recupera la sesión sin escribir la contraseña.

**Decisión:** no se modifica. Recargas normales no llegan al límite; el efecto de llegar es transitorio y no destruye la sesión; y subir el límite o sacar `refresh` de la política cambiaría una defensa de seguridad (RN-06) sin que haga falta.

**Riesgos que se anotan para el despliegue (fase 7), no para esta fase:**
- La partición es la IP remota de la conexión y la API **no configura `ForwardedHeaders`**. Detrás de un proxy inverso todas las personas compartirían la IP del proxy y el límite de 10/min sería global. Antes de publicar detrás de un proxy hay que configurar los encabezados reenviados.
- Tras un NAT (oficina, red universitaria) varias personas comparten un límite; con un solo administrador no ocurre.
- Mejora opcional si algún día molesta: mostrar en el login un aviso específico cuando el refresh inicial recibe `429`, en lugar de la pantalla de login sin explicación.

## Qué no se verificó

- Renovación por **vencimiento natural** del access token (15 min): se simuló inyectando el `401 token_expired` en una solicitud; el refresh y la rotación de la cookie son reales.
- Carreras de dos administradores desde dos navegadores: cubiertas por las pruebas de integración del backend (2 y 8 administradores contra SQL Server real), no repetidas en el navegador.
- Navegadores distintos de Chrome, pantallas táctiles reales y lectores de pantalla.
- Cinerama móvil (Flutter) y SignalR: fuera del alcance de esta verificación.
- Los scripts de Playwright no están en el repositorio: dependen de la cartelera de demostración, esperan entre inicios de sesión por el limitador y escriben en la base de desarrollo. Convertirlos en una suite de punta a punta (con una base propia) es trabajo aparte.
