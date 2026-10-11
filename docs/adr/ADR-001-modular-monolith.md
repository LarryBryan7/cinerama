# ADR-001: Monolito modular en 4 proyectos

- **Estado:** Aceptada
- **Fecha:** 2026-10-02

## Contexto

Cinerama tiene un solo dominio transaccional (asientos, reservas y funciones) cuyas operaciones críticas tocan varias tablas dentro de la misma transacción. Lo desarrolla una sola persona y debe poder levantarse en local con un comando.

## Decisión

Un monolito modular con cuatro proyectos (`Domain`, `Application`, `Infrastructure`, `Api`) y organización por feature dentro de cada uno (`Auth`, `Catalog`, `Rooms`, `Showtimes`, `Seating`, `Bookings`, `Reports`).

Las dependencias van hacia adentro: `Api → Application → Domain` e `Infrastructure → Application`. `Domain` no referencia EF Core ni ASP.NET Core.

Cada feature registra sus servicios con un método de extensión (`services.AddSeating()`), que es el equivalente a un módulo de NestJS.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Microservicios | Confirmar una reserva requiere atomicidad entre asientos y reservas. Separarlos obligaría a sagas o transacciones distribuidas sin ningún beneficio para un solo equipo. |
| Un único proyecto | Más rápido al principio, pero nada impide que la lógica de negocio dependa de EF Core o de HTTP, y las reglas dejan de poder probarse de forma aislada. |
| Vertical slices puras (un proyecto, carpetas por feature) | Válido. Se prefiere la separación por proyectos porque hace visibles las dependencias y el compilador las controla. |

## Consecuencias

- **A favor:** una sola transacción para las operaciones críticas, un solo despliegue y reglas de dominio testeables sin infraestructura.
- **En contra:** más ceremonia que un único proyecto. Se mitiga evitando capas sin propósito (ver ADR-011).
- Si un módulo necesitara escalar por separado en el futuro, los límites por feature facilitan extraerlo.

## Motivación de portafolio

**Producto.** Es la arquitectura que se elegiría para este problema sin consideraciones de portafolio.
