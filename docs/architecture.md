# Arquitectura

## 1. Vista general

```mermaid
flowchart TB
    subgraph Clientes
        Admin["Panel web<br/>React + TypeScript + Vite"]
        Mobile["App móvil<br/>Flutter + BLoC"]
    end

    subgraph API["ASP.NET Core (.NET 10) — monolito modular"]
        REST["Controllers REST /api/v1"]
        Hub["SignalR Hub /hubs/seats"]
        Job["BackgroundService<br/>liberación de bloqueos"]
    end

    SQL[("SQL Server<br/>fuente de verdad")]
    Redis[("Redis<br/>backplane SignalR + caché L2<br/>(fase 6)")]

    Admin -- "REST + cookie de refresh" --> REST
    Mobile -- "REST + Bearer" --> REST
    Mobile -- "WebSocket" --> Hub
    Admin -. "WebSocket (opcional)" .-> Hub
    REST --> SQL
    Job --> SQL
    Job --> Hub
    Hub <-. "solo con ≥2 instancias" .-> Redis
    REST -. "HybridCache L2 (si se justifica)" .-> Redis
```

**Principio rector:** SQL Server es la única fuente de verdad del estado de los asientos. SignalR y Redis solo **comunican** cambios que ya se confirmaron en la base. Si fallan, ningún dato se corrompe: lo único que se pierde es la inmediatez de la notificación.

## 2. Capas del backend

```mermaid
flowchart LR
    Api["Cinerama.Api<br/>controllers, hub, auth, ProblemDetails, Swagger"]
    App["Cinerama.Application<br/>servicios por feature, DTOs, validadores, interfaces"]
    Dom["Cinerama.Domain<br/>entidades, enums, reglas"]
    Inf["Cinerama.Infrastructure<br/>EF Core, SQL, Redis, tokens, BackgroundService"]

    Api --> App
    Api --> Inf
    Inf --> App
    App --> Dom
```

| Proyecto | Contiene | No contiene |
|---|---|---|
| Domain | Entidades con comportamiento (`ShowSeat.Lock()`, `ShowSeat.Confirm()`, `Booking.Cancel()`), enums y excepciones de dominio. | Referencias a EF Core o ASP.NET Core. |
| Application | Un servicio por caso de uso (`SeatLockService`, `BookingService`), DTOs, validadores (FluentValidation), `SeatRecommendationService` (lógica pura) e interfaces (`IAppDbContext`, `ISeatNotifier`, `ITokenService`). | Controllers, SQL escrito a mano, Redis. |
| Infrastructure | `CineramaDbContext`, configuraciones Fluent API, migraciones, archivos `.sql` (SPs y vistas), `TokenService`, `SeatLockCleanupService` y caché. | Reglas de negocio. |
| Api | Controllers delgados, `SeatHub`, `SignalRSeatNotifier`, políticas de autorización, manejo global de errores, rate limiting, CORS y Swagger. | Lógica de negocio. |

**Organización interna por feature**: dentro de Application e Infrastructure el código se agrupa por módulo, no por tipo técnico:

```
Application/
├── Auth/          (AuthService, DTOs, validadores)
├── Catalog/       (películas y géneros)
├── Rooms/         (salas y layout)
├── Showtimes/
├── Seating/       (mapa, bloqueo, liberación, recomendación)
├── Bookings/
├── Reports/
└── Common/        (paginación, errores, interfaces compartidas)
```

**Compromiso consciente:** Application depende del paquete de EF Core a través de `IAppDbContext`, para usar LINQ y proyecciones sin una capa de repositorio intermedia. Ver [ADR-011](adr/ADR-011-no-mediatr-automapper-repository.md).

## 3. Flujos críticos

### 3.1 Bloqueo de asientos con concurrencia

```mermaid
sequenceDiagram
    autonumber
    participant A as Cliente A
    participant B as Cliente B
    participant API
    participant DB as SQL Server
    participant H as SignalR

    par mismo asiento, mismo instante
        A->>API: POST /showtimes/42/seats/lock {F7}
    and
        B->>API: POST /showtimes/42/seats/lock {F7}
    end
    API->>DB: SELECT ShowSeat F7 (RowVersion = 0x01) para A
    API->>DB: SELECT ShowSeat F7 (RowVersion = 0x01) para B
    API->>DB: UPDATE ... WHERE Id=F7 AND RowVersion=0x01 (A)
    DB-->>API: 1 fila → commit
    API->>DB: UPDATE ... WHERE Id=F7 AND RowVersion=0x01 (B)
    DB-->>API: 0 filas → DbUpdateConcurrencyException → rollback
    API-->>A: 200 {lockedUntil}
    API-->>B: 409 seat_unavailable [F7]
    API->>H: SeatStatusChanged(42, F7=Locked) después del commit
    H-->>B: el mapa de B marca F7 como no disponible
```

Hay tres barreras independientes contra la doble venta (detalle en [ADR-004](adr/ADR-004-seat-concurrency-strategy.md)):

1. **Concurrencia optimista con `RowVersion`** al bloquear y al confirmar.
2. **Transacción** que hace que una operación con varios asientos sea todo o nada.
3. **Índice único filtrado** en `BookingSeats(ShowSeatId) WHERE ReleasedAt IS NULL`, que rechaza en la base una segunda reserva activa aunque la aplicación tuviera un error.

### 3.2 Confirmación

```mermaid
sequenceDiagram
    autonumber
    participant C as Cliente
    participant API
    participant DB as SQL Server
    participant H as SignalR

    C->>API: POST /bookings {showtimeId, seatIds} + Idempotency-Key
    API->>DB: ¿existe una reserva con esa clave para el usuario?
    alt ya existe
        API-->>C: 200 reserva original
    else nueva
        API->>DB: BEGIN TRAN
        API->>DB: cargar ShowSeats (verificar Locked, mío, vigente)
        API->>DB: UPDATE ShowSeats → Booked (con RowVersion)
        API->>DB: INSERT Booking + BookingSeats
        API->>DB: COMMIT
        API->>H: SeatStatusChanged(..., Booked)
        API-->>C: 201 Booking
    end
```

### 3.3 Liberación de bloqueos vencidos

```mermaid
sequenceDiagram
    participant J as SeatLockCleanupService
    participant DB as SQL Server
    participant H as SignalR

    loop cada 30 s
        J->>DB: EXEC sp_ReleaseExpiredSeatLocks
        DB-->>J: filas liberadas (UPDATE ... OUTPUT)
        J->>H: un SeatStatusChanged por función afectada
    end
```

El `BackgroundService` es un singleton y el `DbContext` es *scoped*, así que el servicio crea un scope por ciclo con `IServiceScopeFactory`. El reloj se inyecta con `TimeProvider` para poder probar la expiración sin esperar 5 minutos.

## 4. Despliegue local

### Una instancia (fases 1–5)

```mermaid
flowchart LR
    Dev["React (vite dev) :5173"] --> API1["api :8090"]
    App["Flutter (emulador)"] --> API1
    API1 --> SQL[("sqlserver :1433")]
```

### Dos instancias (fase 6)

```mermaid
flowchart LR
    CA["Cliente A"] -- "WebSocket" --> I1["api-1 :8081"]
    CB["Cliente B"] -- "WebSocket" --> I2["api-2 :8082"]
    I1 --> SQL[("sqlserver")]
    I2 --> SQL
    I1 <--> R[("redis")]
    I2 <--> R
```

La demostración conecta a cada cliente **directamente** a una instancia distinta. Así queda a la vista que, sin backplane, el cliente B no se entera de un bloqueo hecho en la instancia 1, y que con Redis sí. No hace falta un balanceador ni sesiones persistentes (*sticky sessions*). Ver [ADR-008](adr/ADR-008-redis-usage.md).

## 5. Manejo de errores

Todos los errores se devuelven como `application/problem+json` (RFC 9457):

```json
{
  "type": "https://cinerama.example/errors/seat-unavailable",
  "title": "Uno o más asientos no están disponibles",
  "status": 409,
  "code": "seat_unavailable",
  "traceId": "00-4bf9...-01",
  "conflicts": [{ "seatId": 1234, "label": "F7" }]
}
```

| Origen | Código HTTP |
|---|---|
| Validación de formato (FluentValidation) | 400 con `errors` por campo |
| Sin token, token inválido o vencido | 401 (`token_expired` cuando aplica) |
| Rol insuficiente | 403 |
| No existe, o no es del usuario | 404 |
| `DbUpdateConcurrencyException`, violación de índice único, conflicto de estado | 409 |
| Regla de negocio (`DomainRuleException`) | 422 |
| Rate limit | 429 |
| Excepción no controlada | 500 con `traceId`, sin stack trace |

Las excepciones de dominio llevan el `code` estable. El manejador global (`IExceptionHandler`) las traduce a HTTP en un único lugar.

## 6. Justificación del stack

Cada tecnología y su motivo están en [tech-stack.md](tech-stack.md). Las decisiones con alternativas reales están en [`adr/`](adr/).
