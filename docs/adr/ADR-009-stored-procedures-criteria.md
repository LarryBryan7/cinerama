# ADR-009: Criterio para usar stored procedures

- **Estado:** Aceptada
- **Fecha:** 2026-10-02

## Contexto

EF Core cubre la mayoría de las consultas con LINQ, proyecciones y `ExecuteUpdate`. Los stored procedures (SP) mueven lógica a la base, lo que dificulta los tests y la refactorización. Se necesita un criterio explícito para no crearlos de forma artificial ni evitarlos por dogma.

## Decisión

Un SP o una vista se justifica si cumple **al menos uno** de estos criterios:

1. **Atomicidad con resultado:** una sola sentencia que modifica y devuelve lo modificado (`OUTPUT`), algo que EF Core no expresa.
2. **Definición compartida:** una métrica que deben calcular igual la API y otras herramientas (BI, consultas manuales).
3. **Consulta de conjunto cuyo plan conviene afinar** a nivel SQL (agregaciones sobre varias tablas con volumen).

Y **nunca** se usa para lógica de negocio que necesita tests unitarios (por ejemplo, la recomendación de asientos).

| Objeto | Criterio | Motivación |
|---|---|---|
| `sp_ReleaseExpiredSeatLocks` | 1 | **Producto** |
| `vw_ShowtimeSales` | 2 | **Producto** |
| `sp_GetSalesSummary` | 3 | **Portafolio** (ver abajo) |

Los SP se versionan como archivos `.sql` aplicados desde migraciones de EF Core, se invocan con `FromSql`/`SqlQuery` usando parámetros (nunca concatenando cadenas) y tienen tests de integración contra SQL Server real.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Todo en LINQ | Para la liberación de bloqueos habría que hacer `SELECT` y luego `UPDATE`, lo que abre una carrera, o bien perder la información de qué se liberó. |
| Lógica de negocio en SP (como en sistemas *legacy*) | Reglas repartidas entre C# y T-SQL, difíciles de probar y de revisar. |

## Consecuencias

- La lista de SP es corta y cada uno tiene su justificación en [database.md](../database.md).
- Los descartados (`sp_GetAvailableSeatsByShowtime`, `sp_GetRecommendedSeats`, `sp_CreateShowtime`) quedan documentados con su motivo.

## Motivación de portafolio

**Mixta, y se declara.**
- `sp_ReleaseExpiredSeatLocks` y `vw_ShowtimeSales` responden a necesidades técnicas reales.
- `sp_GetSalesSummary` **podría escribirse en LINQ** con un resultado equivalente. Se implementa como SP **principalmente para demostrar T-SQL, agregaciones y el análisis de planes de ejecución**. Su existencia se acompaña de una medición del plan en `docs/performance.md`, para que aporte algo más que la sintaxis.
