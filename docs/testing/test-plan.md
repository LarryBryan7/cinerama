# Plan de pruebas

## 1. Objetivo

Verificar que Cinerama cumple sus requerimientos funcionales y no funcionales, con foco en tres riesgos:

1. **Integridad:** la doble venta de un asiento.
2. **Seguridad:** el acceso a datos ajenos y la escalada de privilegios.
3. **Disponibilidad funcional:** la consulta del ticket sin conexión.

## 2. Alcance

| Incluido | Excluido |
|---|---|
| API REST (todas las rutas de [api.md](../api.md)) | Pagos reales (no existen, RN-26) |
| Reglas de negocio RN-01 a RN-38 | Pruebas de penetración formales |
| Concurrencia en el bloqueo y la confirmación | Compatibilidad con navegadores antiguos |
| Tiempo real (SignalR), con 1 y con 2 instancias | Pruebas de estrés hasta la saturación |
| App móvil: flujo de reserva, sesión y modo sin conexión | Distribución en las tiendas |
| Panel web: login, CRUD y dashboard | |

## 3. Niveles y tipos de prueba

| Nivel | Responsable de ejecución | Herramienta | Criterio |
|---|---|---|---|
| Unitario | CI en cada push | xUnit, Moq / bloc_test | 100 % de las reglas RN con lógica cubiertas por al menos un test |
| Integración | CI en cada push | WebApplicationFactory + Testcontainers | Todos los casos marcados como *Automatizado (Int.)* |
| Funcional de API | Manual + Newman antes de cada cierre de fase | Postman | Casos de [test-cases.md](test-cases.md) |
| Funcional de UI | Manual al cerrar las fases 3, 4 y 5 | Emulador Android / navegador | Casos marcados como *Manual* |
| Seguridad | CI (IDOR, roles, tokens) + revisión manual | Integración + checklist OWASP API Top 10 | TC-SEC-* sin fallos |
| Rendimiento | Fase 6 | k6 + `SET STATISTICS IO/TIME` | Medición documentada; los objetivos de RNF-07 se validan, no se asumen |
| Regresión | CI + Newman | — | Todas las suites en verde antes de un merge a `main` |

## 4. Entornos y datos

| Entorno | Composición | Datos |
|---|---|---|
| Tests de integración | Contenedor efímero de SQL Server por ejecución | Cada test crea sus datos. Respawn limpia entre tests. |
| Local (manual / Newman) | `docker compose up` | Seed determinista ([functional-design.md §6](../functional-design.md#6-datos-semilla)) |
| Rendimiento | `docker compose` + script de volumen | Un año de funciones, generado con una semilla fija |

**Todos los datos son ficticios.** Las cuentas de prueba usan el dominio reservado `.test`.

## 5. Criterios de entrada y salida

**Entrada a pruebas funcionales de una fase:**
- el build compila sin warnings nuevos;
- las suites unitarias y de integración están en verde;
- los endpoints de la fase están documentados en Swagger.

**Salida (cierre de fase):**
- 0 defectos abiertos de severidad Crítica o Alta;
- los casos de la fase están en estado *Pasa*;
- la [matriz de trazabilidad](traceability-matrix.md) está actualizada.

## 6. Clasificación de defectos

| Severidad | Definición | Ejemplo |
|---|---|---|
| Crítica | Pérdida de integridad o de seguridad | Doble venta; un usuario lee la reserva de otro |
| Alta | Un flujo principal no se puede completar | No se puede confirmar ninguna reserva |
| Media | Un flujo alternativo falla o una regla se aplica mal | El límite de 10 asientos no se respeta |
| Baja | Problema cosmético o de texto | Mensaje con una errata |

Cada defecto se registra como *issue* de GitHub con: pasos para reproducir, resultado esperado, resultado obtenido, severidad, caso de prueba y regla afectada. Cuando se corrige, se agrega un test que lo reproduce.

## 7. Riesgos de las pruebas

| Riesgo | Mitigación |
|---|---|
| Un test de concurrencia pasa por casualidad (las solicitudes no coincidieron) | `Barrier` para sincronizar el arranque, varias repeticiones por test y verificación del estado final en la base, no solo de los códigos HTTP. |
| SQL Server emulado en Apple Silicon es lento | Un contenedor por ejecución (*collection fixture*). CI corre en Linux x64. |
| Pruebas de tiempo real inestables por la latencia | Esperas con un tiempo máximo explícito (no `sleep` fijo) y verificación por evento recibido. |
| La expiración depende del reloj | `TimeProvider` inyectado y `FakeTimeProvider` en los tests. |
