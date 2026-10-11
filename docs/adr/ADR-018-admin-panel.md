# ADR-018: Panel de administración: API de administración separada y panel sin lógica de negocio

- **Estado:** Aceptada
- **Fecha:** 2026-10-08
- **Tipo:** decisión de producto y de arquitectura

## Contexto

La fase 4 entrega el panel web. Al empezar se comprobó que la documentación prometía endpoints de administración (altas de funciones, listado de reservas, reportes) que la API todavía no tenía: solo existían lecturas públicas, las altas básicas de catálogo y el cambio de estado de usuarios. Había que construir ambas partes y decidir cómo mantenerlas coherentes con las decisiones anteriores (ADR-006 sesión, ADR-009 SP, ADR-011 sin MediatR/AutoMapper/repositorio).

## Decisión

**API**
- Las lecturas del administrador (`/admin/...`) son controladores **separados** de los públicos. ASP.NET Core da prioridad a `[AllowAnonymous]` de la clase sobre `[Authorize]` de la acción, así que mezclar rutas públicas y de administrador en un mismo controlador dejaría las de administrador abiertas por accidente. Cada controlador de administración lleva `[Authorize(Policy = Policies.AdminOnly)]` a nivel de clase y un test recorre todas las rutas con y sin token.
- Las escrituras mantienen las rutas de siempre (`POST /showtimes`, `PUT /movies/{id}`…) para no romper el contrato ni a la app móvil.
- La lógica vive en servicios de `Application/Admin` (ADR-011). Cancelar funciones, ya sea una a una o porque se da de baja una película o se desactiva un cine o una sala, pasa por **un único** `ShowtimeCanceller` para que ninguna ruta deje una función viva sin sentido.
- **Concurrencia al programar funciones:** crear, editar y cambiar el layout de una sala toman el mismo candado de aplicación sobre la sala (`sp_getapplock` dentro de la transacción). Dos administradores que programan a la vez se serializan y solo se confirma una función; la otra recibe `409`. Si no se obtiene el candado se responde `409 concurrency_conflict` (seguro de reintentar).
- **Reportes:** la definición de "vendida", "ingreso" y "ocupación" vive en la vista `vw_ShowtimeSales`; los KPIs salen de `sp_GetSalesSummary` (ADR-009). La vista y el SP se versionan como archivos `.sql` embebidos y se aplican desde la migración `AddSalesReporting`. El periodo es el **día de la función** en el calendario del cine, no el de la compra.

**Panel** (`web/`)
- React + TypeScript + Vite, React Router (rutas con carga bajo demanda), TanStack Query para el estado del servidor, React Hook Form + Zod para formularios, Axios con interceptores, Tailwind y Recharts. Vitest + Testing Library + MSW para las pruebas.
- **Sesión** según ADR-006: el access token solo en una variable del módulo, el refresh en cookie HttpOnly. Un único refresh en curso a la vez: las solicitudes que reciben `401 token_expired` esperan el mismo resultado y se reintentan una vez.
- **Sin lógica de negocio en el panel** (RN-58). Valida lo mínimo para ahorrar un viaje (campos obligatorios, formatos) y traduce el `code` de la API a un mensaje. Los botones deshabilitados son ayuda, no seguridad.
- **Zona horaria** (RN-59): lo que se escribe y se muestra es la hora del cine, no la del navegador. Las pruebas corren en `Asia/Tokyo` para que una conversión que use la zona del navegador falle.
- El panel nunca habla con SQL Server: todo pasa por la API.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Un solo controlador por recurso con rutas públicas y de administrador | `[AllowAnonymous]` de clase anula `[Authorize]` de acción: riesgo de exponer rutas de administrador. |
| Rutas nuevas para todas las escrituras (`/admin/showtimes` POST…) | Rompería el contrato existente sin beneficio. |
| Calcular los reportes en el panel a partir de las listas | Duplicaría la definición de ingresos y ocupación y traería miles de filas al navegador. |
| Estado global con Redux | TanStack Query cubre el estado del servidor; el estado local de cada pantalla es pequeño. |
| Probar con un backend real en cada test del panel | Lento y frágil; el contrato lo cubren los tests de integración del backend y las pruebas del panel con MSW. |

## Consecuencias

- Hay una API de administración completa y probada contra SQL Server real, y un panel con pruebas de los flujos críticos.
- Las pruebas del panel usan una API simulada: hace falta además un recorrido con navegador contra el backend real (pendiente).
- Si cambia una regla, se cambia en la API; el panel solo ajusta el texto del mensaje.
- El gráfico de Recharts pesa (~110 kB comprimido) y por eso se carga solo al abrir Reportes o el dashboard.

## Motivación de portafolio

Muestra una frontera de seguridad cuidada (controladores separados y un test que recorre todas las rutas), concurrencia resuelta con candados de aplicación y probada con carreras reales, reportes definidos una sola vez en SQL, y un SPA con sesión segura, accesibilidad (diálogos con foco atrapado, cuadrícula de asientos con teclado) y pruebas de comportamiento.
