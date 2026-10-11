# ADR-003: SQL Server como única base de datos, sin NoSQL

- **Estado:** Aceptada
- **Fecha:** 2026-10-02

## Contexto

Los datos de Cinerama son relacionales y transaccionales: un asiento pertenece a una sala, una función tiene un asiento por cada asiento de la sala y una reserva agrupa asientos de una función. La regla más importante del sistema ("un asiento no se vende dos veces") es una restricción de integridad entre filas, y necesita transacciones ACID.

## Decisión

- **SQL Server** como única base de datos y fuente de verdad.
- **No se incorpora una base NoSQL documental** (MongoDB, Cosmos DB, etc.).
- Redis (ADR-008) no se considera "la base NoSQL del proyecto": es un medio de comunicación y caché, nunca almacena datos de negocio.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| PostgreSQL | Habría servido igual de bien: tiene MVCC, `xmin` para concurrencia optimista e índices parciales. Se descarta solo por el perfil profesional buscado (ver la motivación). |
| MongoDB para el catálogo de películas | El catálogo es pequeño, estructurado y se une con funciones y reportes. Un segundo motor agregaría sincronización y operación sin resolver ningún problema. |
| MongoDB para logs o auditoría | No hay requerimiento de auditoría. Los logs estructurados van a la salida estándar. |

## Consecuencias

- Las restricciones de integridad (`CHECK`, `UNIQUE` filtrados, FKs) viven en la base y protegen incluso ante errores de la aplicación.
- La experiencia con NoSQL **queda fuera de este proyecto**. No se presenta Redis como sustituto.
- En macOS con Apple Silicon, la imagen oficial de SQL Server (`linux/amd64`) corre emulada con Rosetta en Docker Desktop. Funciona, pero más lenta. En CI (Linux x64) corre de forma nativa.

## Motivación de portafolio

**Producto + portafolio.** El producto necesita una base relacional transaccional; eso es una decisión de producto. Entre SQL Server y PostgreSQL, que son equivalentes para este caso, se elige SQL Server **principalmente para demostrar experiencia práctica con SQL Server y T-SQL**.
