# ADR-011: Sin MediatR, AutoMapper ni repositorio genérico

- **Estado:** Aceptada
- **Fecha:** 2026-10-02

## Contexto

Muchas plantillas de "Clean Architecture" en .NET incluyen por defecto MediatR (CQRS), AutoMapper y un repositorio genérico con unidad de trabajo encima de EF Core. Cada uno agrega indirección, y hay que evaluar si en este proyecto resuelven algo.

## Decisión

| Patrón / librería | Decisión | Qué se usa en su lugar |
|---|---|---|
| MediatR | No | Servicios de aplicación por feature (`BookingService.ConfirmAsync`) que los controllers invocan directamente. Lo transversal (validación, errores, logging) se resuelve con filtros y middleware de ASP.NET Core. |
| AutoMapper | No | Proyecciones explícitas con `Select(x => new MovieDto(...))`. El mapeo es visible, se traduce a SQL (solo se consultan las columnas necesarias) y un error de mapeo es un error de compilación. |
| Repositorio genérico + UoW | No | `IAppDbContext` (expone los `DbSet` y `SaveChangesAsync`). `DbContext` ya implementa ambos patrones. |

## Alternativas consideradas

- **MediatR:** desacopla controllers de handlers y ofrece *pipeline behaviors*. Con pocos casos de uso y un solo punto de entrada (HTTP), ese desacoplamiento no se aprovecha, y dificulta navegar el código ("¿quién maneja este comando?"). Además, desde 2025 MediatR y AutoMapper tienen licencia comercial para ciertos usos, otro costo sin beneficio aquí.
- **Repositorio genérico:** su beneficio teórico es cambiar de ORM, cosa que no se hará. A cambio, oculta `Include`, proyecciones y `AsNoTracking`, que son las herramientas de rendimiento de EF Core.

## Consecuencias

- **Compromiso aceptado:** `Application` depende del paquete de EF Core a través de `IAppDbContext`. La lógica que debe probarse sin base de datos (reglas de dominio, recomendación, validadores) vive en Domain o en servicios puros que no tocan el contexto. Los servicios con acceso a datos se prueban con tests de integración (ADR-012).
- Si un caso de uso creciera hasta necesitar un pipeline (por ejemplo, auditoría de cada comando), se reevalúa esta decisión con ese caso concreto.

## Motivación de portafolio

**Producto.** Además, poder explicar *por qué no* se usa un patrón popular es parte de lo que el proyecto quiere demostrar.
