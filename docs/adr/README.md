# Architecture Decision Records

Cada ADR registra una decisión con alternativas reales: el contexto, lo que se decidió, lo que se descartó y sus consecuencias.

Todos incluyen la sección **Motivación de portafolio**, que indica si la decisión responde a una necesidad del producto o si se tomó principalmente para demostrar una competencia. Es una regla del proyecto: ninguna tecnología se agrega solo para sumarla a la lista.

| ADR | Decisión | Estado | Motivación |
|---|---|---|---|
| [001](ADR-001-modular-monolith.md) | Monolito modular en 4 proyectos | Aceptada | Producto |
| [002](ADR-002-dotnet-10-lts.md) | .NET 10 LTS | Aceptada | Producto + portafolio |
| [003](ADR-003-sql-server-no-nosql.md) | SQL Server como única base; sin NoSQL | Aceptada | Producto + portafolio |
| [004](ADR-004-seat-concurrency-strategy.md) | Concurrencia de asientos: RowVersion + transacción + índice filtrado | Aceptada | Producto |
| [005](ADR-005-seat-lock-expiration.md) | Expiración de bloqueos en dos niveles + BackgroundService + SignalR | Aceptada | Producto |
| [006](ADR-006-web-session-cookies.md) | Sesión del panel web: access en memoria, refresh en cookie HttpOnly | Aceptada | Producto |
| [007](ADR-007-refresh-token-rotation.md) | Refresh tokens con hash, rotación y detección de reutilización | Aceptada | Producto |
| [008](ADR-008-redis-usage.md) | Redis solo como backplane de SignalR y caché L2 condicionada | Aceptada | **Portafolio** (documentado) |
| [009](ADR-009-stored-procedures-criteria.md) | Criterio para stored procedures | Aceptada | Mixta (documentado) |
| [010](ADR-010-mobile-offline-strategy.md) | Offline parcial en la app: lectura sí, escrituras no | Aceptada | Producto |
| [011](ADR-011-no-mediatr-automapper-repository.md) | Sin MediatR, AutoMapper ni repositorio genérico | Aceptada | Producto |
| [012](ADR-012-testing-strategy.md) | Testcontainers en lugar de EF InMemory | Aceptada | Producto |
| [013](ADR-013-flutter-state-bloc.md) | BLoC/Cubit para el estado en Flutter | Aceptada | Producto |
| [014](ADR-014-local-environment-and-startup.md) | Entorno local y arranque: migraciones, seed y Compose | Aceptada | Producto |
| [015](ADR-015-unknown-routes-401-vs-404.md) | Rutas inexistentes: 401 sin sesión, 404 con sesión | Aceptada | Producto (seguridad) |
| [016](ADR-016-signalr-realtime.md) | SignalR solo para avisar; token en la URL solo para el hub | Aceptada | Producto (seguridad) |
| [017](ADR-017-catalog-release-cinemas-reviews.md) | Estreno derivado, cines como entidad, opiniones y medios | Aceptada | Producto |
| [018](ADR-018-admin-panel.md) | Panel de administración: API separada y panel sin lógica de negocio | Aceptada | Producto (arquitectura) |

Plantilla: Estado · Fecha · Contexto · Decisión · Alternativas consideradas · Consecuencias · Motivación de portafolio.
