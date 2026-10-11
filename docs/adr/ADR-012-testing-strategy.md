# ADR-012: Estrategia de pruebas con Testcontainers en lugar de EF InMemory

- **Estado:** Aceptada
- **Fecha:** 2026-10-02

## Contexto

Los comportamientos más importantes de Cinerama dependen del motor de base de datos: `rowversion`, transacciones, índices únicos filtrados, `CHECK` constraints, stored procedures y aislamiento. El proveedor EF Core InMemory no implementa ninguno de ellos. Un test de concurrencia que "pasa" en memoria no demuestra nada.

Una lección de un proyecto anterior: un test de integración apuntó por error a una base de desarrollo compartida, porque tomaba la cadena de conexión del entorno. Hizo falta agregar un guardia manual para evitarlo.

## Decisión

| Nivel | Herramientas | Qué cubre | Base de datos |
|---|---|---|---|
| **Unitario** (`Cinerama.UnitTests`) | xUnit, Moq, Shouldly, `FakeTimeProvider` | Reglas de dominio (`ShowSeat.Lock`, `Booking.Cancel`), `SeatRecommendationService`, validadores, `TokenService`, servicios con dependencias simuladas (`ISeatNotifier`, `IPasswordHasher`). | Ninguna |
| **Integración** (`Cinerama.IntegrationTests`) | `WebApplicationFactory`, Testcontainers.MsSql, Respawn | Endpoints de punta a punta: auth, IDOR, bloqueo, confirmación, cancelación, concurrencia, SP y reportes. | **SQL Server real en un contenedor efímero**, creado por la suite y destruido al terminar. Respawn limpia los datos entre tests. |
| **Funcional de API** | Postman + Newman | Recorridos de los casos de prueba de `docs/testing/test-cases.md`. | Entorno de Docker Compose |
| **Carga** (fase 6) | k6 | Mapa de asientos, bloqueo concurrente y cartelera. | Entorno de Docker Compose + seed de volumen |
| **Flutter** | bloc_test, mocktail | `SeatSelectionBloc` (toques + eventos remotos), repositorios offline e interceptor de refresh. | Drift en memoria |
| **React** | Vitest + Testing Library (opcional) | Interceptor de refresh y formularios críticos. | — |

**Reglas:**
- La cadena de conexión de los tests de integración la genera Testcontainers. **No existe ninguna forma de que apunten a otra base.**
- Cada regla `RN-xx` con lógica tiene al menos un test cuyo nombre la referencia (`Lock_WhenSeatLockedByOther_Throws_RN18`).
- El test de concurrencia usa usuarios distintos, `HttpClient`s independientes y un `Barrier` para que las solicitudes salgan a la vez. Se repite varias veces dentro del mismo test para reducir la posibilidad de un éxito por casualidad.
- CI (GitHub Actions, Ubuntu) ejecuta las dos suites en cada push.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| EF Core InMemory | No tiene transacciones, `rowversion` ni constraints. Da falsos positivos justo en lo crítico. |
| SQLite en memoria | No es SQL Server: no tiene `rowversion` ni los mismos SP o índices filtrados. |
| Base de desarrollo compartida | Datos residuales, tests no repetibles y riesgo de escribir donde no corresponde. |
| Mockear `DbSet` | Frágil, y prueba el mock en lugar de las consultas. |

## Consecuencias

- Los tests de integración necesitan Docker. En macOS con Apple Silicon, SQL Server corre emulado y la suite tarda más. El contenedor se crea una sola vez por ejecución (*collection fixture*) para amortizarlo.
- Un solo proyecto de tests mezclaría suites rápidas y lentas. Separarlas permite correr las unitarias en cada guardado.

## Motivación de portafolio

**Producto.** Es la forma confiable de probar este dominio.
