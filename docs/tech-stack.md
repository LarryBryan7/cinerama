# Stack tecnológico y justificación

Regla del proyecto: **cada tecnología tiene un motivo técnico documentado.** Cuando la razón principal es demostrar una competencia de portafolio y no una necesidad del producto, se indica explícitamente en la columna *Motivación*:

- **Producto**: el producto la necesita y se elegiría igual sin consideraciones de portafolio.
- **Producto + portafolio**: el producto necesita algo de esa categoría; entre alternativas equivalentes, se eligió esta por su valor de portafolio.
- **Portafolio**: no es estrictamente necesaria a la escala del producto. Se incluye para demostrar una competencia y su ADR lo dice.

## Backend

| Tecnología | Uso | Por qué | Alternativa descartada | Motivación | ADR |
|---|---|---|---|---|---|
| C# / .NET 10 LTS | Lenguaje y runtime | Tipado fuerte y soporte LTS hasta noviembre de 2028. | .NET 9 (fin de soporte el 10/11/2026) | Producto + portafolio | [002](adr/ADR-002-dotnet-10-lts.md) |
| ASP.NET Core Web API | API REST | Middleware, DI, autenticación y OpenAPI integrados. | Minimal APIs puras (se usan controllers por legibilidad y agrupación) | Producto + portafolio | [001](adr/ADR-001-modular-monolith.md) |
| Entity Framework Core | ORM y migraciones | Concurrencia optimista con `rowversion`, LINQ con proyecciones, migraciones versionadas. | Dapper (requiere más código para un CRUD y no gestiona concurrencia ni migraciones) | Producto | [004](adr/ADR-004-seat-concurrency-strategy.md) |
| SQL Server | Base de datos | Transaccional y relacional, con `rowversion` nativo, índices filtrados y T-SQL. | PostgreSQL (habría servido igual de bien) | **Producto + portafolio**: se necesita una base relacional; SQL Server se elige por el perfil buscado. | [003](adr/ADR-003-sql-server-no-nosql.md) |
| Stored procedures | Liberación de bloqueos y KPIs | Solo donde aportan atomicidad con resultado o un plan afinable. | LINQ en todo | Uno de producto y uno de **portafolio** | [009](adr/ADR-009-stored-procedures-criteria.md) |
| JWT + refresh con rotación | Autenticación stateless | Funciona igual en móvil y web. La rotación limita el daño de un token robado. | Cookies de sesión en servidor (no encajan bien en la app móvil) | Producto | [007](adr/ADR-007-refresh-token-rotation.md) |
| `PasswordHasher<T>` (Identity) | Hash de contraseñas | PBKDF2 con sal y versionado, mantenido por Microsoft. Evita implementar criptografía propia. | ASP.NET Identity completo (más tablas y flujos de los necesarios) | Producto | [007](adr/ADR-007-refresh-token-rotation.md) |
| SignalR | Mapa de asientos en vivo | Push en tiempo real con grupos por función y reconexión. | Polling cada N segundos (más carga y más latencia) | Producto | [005](adr/ADR-005-seat-lock-expiration.md) |
| BackgroundService | Liberar bloqueos vencidos | Proceso periódico dentro de la API, sin infraestructura extra. | Job de SQL Agent (no notifica por SignalR y no existe en todas las ediciones) | Producto | [005](adr/ADR-005-seat-lock-expiration.md) |
| Redis | Backplane de SignalR y caché L2 | Propagar eventos entre instancias. | Sin Redis (válido con una sola instancia) | **Portafolio**: a la escala de un cine alcanza con una instancia. | [008](adr/ADR-008-redis-usage.md) |
| HybridCache | Caché de cartelera y funciones | Caché L1 + L2 con protección contra estampida. | `IMemoryCache` | Condicionado a medición | [008](adr/ADR-008-redis-usage.md) |
| FluentValidation | Validación de DTOs | Reglas legibles y testeables de forma aislada. | DataAnnotations (difícil para reglas condicionales) | Producto | — |
| Swashbuckle (Swagger UI) | Documentación interactiva | Prueba de endpoints con JWT desde el navegador. | Scalar | Producto | — |
| Rate limiter integrado | Proteger login y bloqueo | Viene en ASP.NET Core, sin paquetes externos. | — | Producto | — |

## Pruebas

| Tecnología | Uso | Por qué | Motivación | ADR |
|---|---|---|---|---|
| xUnit | Framework de tests | Estándar de facto en .NET y usado por el propio ASP.NET Core. | Producto | [012](adr/ADR-012-testing-strategy.md) |
| Moq | Dobles de prueba | Aislar servicios de sus dependencias (`ISeatNotifier`, `ITokenService`). | Producto | [012](adr/ADR-012-testing-strategy.md) |
| WebApplicationFactory | Tests de integración HTTP | Levanta la API en memoria con la configuración real. | Producto | [012](adr/ADR-012-testing-strategy.md) |
| Testcontainers (SQL Server) | Base real y efímera por ejecución | `rowversion`, transacciones e índices filtrados solo se prueban de verdad contra SQL Server. | Producto | [012](adr/ADR-012-testing-strategy.md) |
| Shouldly | Aserciones legibles | Sin restricciones de licencia comercial. | Producto | — |
| Postman + Newman | Pruebas funcionales de la API | Colección ejecutable y versionada; reutilizable en CI. | Producto | — |
| k6 | Medición de carga | Scripts versionados y reproducibles. | Producto (fase 6) | — |

## Panel web

| Tecnología | Uso | Por qué | Motivación |
|---|---|---|---|
| React + TypeScript + Vite | SPA de administración | Tipado del contrato de la API y desarrollo rápido. | Producto + portafolio |
| React Router | Navegación y rutas protegidas | Estándar. | Producto |
| Axios | Cliente HTTP | Interceptores para el refresh con una sola solicitud en curso. | Producto |
| TanStack Query | Estado del servidor | Caché, reintentos e invalidación sin escribir un store a mano. | Producto |
| react-hook-form + zod | Formularios | Validación tipada en el cliente. | Producto |
| Recharts | Gráficos del dashboard | Liviano y declarativo. | Producto |
| Tailwind CSS | Estilos | Velocidad. El editor de layout usa CSS Grid directamente. | Producto |

## App móvil

| Tecnología | Uso | Por qué | Motivación | ADR |
|---|---|---|---|---|
| Flutter + Dart | App del cliente | Una sola base de código para Android e iOS. | Producto + portafolio | — |
| flutter_bloc | Estado | El mapa recibe eventos de dos fuentes (toques y SignalR); el modelo de eventos de BLoC ordena ese flujo. | Producto | [013](adr/ADR-013-flutter-state-bloc.md) |
| Dio | HTTP | Interceptores para el refresh y los reintentos. | Producto | — |
| flutter_secure_storage | Refresh token | Keychain / Keystore del sistema operativo. | Producto | [007](adr/ADR-007-refresh-token-rotation.md) |
| Drift | Caché offline | SQLite tipado, con una base por usuario. | Producto | [010](adr/ADR-010-mobile-offline-strategy.md) |
| connectivity_plus | Detección de red | Complementado con un ping a `/health`, porque la interfaz activa no garantiza acceso a la API. | Producto | [010](adr/ADR-010-mobile-offline-strategy.md) |
| signalr_netcore | Cliente SignalR | No hay cliente oficial de Microsoft para Dart. Riesgo: mantenimiento comunitario, se verifica al iniciar la fase 3. | Producto | — |
| qr_flutter | Ticket | Genera el QR en el dispositivo, también sin conexión. | Producto | — |

## Infraestructura

| Tecnología | Uso | Por qué | Motivación |
|---|---|---|---|
| Docker + Compose | SQL Server, Redis y API en local | SQL Server no corre de forma nativa en macOS; con Compose el entorno se levanta con un comando. | Producto |
| GitHub Actions | CI: build y tests | Los tests de integración con Testcontainers corren en los runners de Linux. | Producto |

## Evaluadas y descartadas

| Tecnología | Motivo |
|---|---|
| MongoDB u otra NoSQL documental | No hay datos con forma de documento ni necesidad de esquema flexible. Ver [ADR-003](adr/ADR-003-sql-server-no-nosql.md). |
| MediatR, AutoMapper, repositorio genérico | Indirección sin beneficio a este tamaño. Ver [ADR-011](adr/ADR-011-no-mediatr-automapper-repository.md). |
| Microservicios, bus de mensajes | Un solo equipo y un solo dominio transaccional. Ver [ADR-001](adr/ADR-001-modular-monolith.md). |
| Lock distribuido de asientos en Redis | Duplicaría la fuente de verdad. Ver [ADR-008](adr/ADR-008-redis-usage.md). |
| Power BI, Power Automate, SharePoint | No forman parte del producto. Se tratan como formación separada. |
| IA para recomendar asientos | Un algoritmo determinista es suficiente, explicable y testeable. |
