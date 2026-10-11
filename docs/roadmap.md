# Roadmap

Cada fase termina con algo **funcional y demostrable**, con sus tests en verde y la documentación actualizada.

## Fases

| Fase | Contenido | Criterio de salida | Estado |
|---|---|---|---|
| **0. Análisis** | Requerimientos, historias, casos de uso, reglas de negocio, diseño funcional, arquitectura, modelo de datos, API, ADRs, plan y casos de prueba, esqueleto del repo. | Documentación revisada y aprobada por el autor. | ✅ Aprobada |
| **1. Base del backend** | Solución .NET 10 con 4 proyectos + 2 de tests. Dominio y migraciones con constraints. Seed. Auth completa (registro, login, refresh con rotación, logout, cookie del panel web). Políticas. ProblemDetails. Rate limiting. Swagger. Cartelera y funciones de solo lectura. Docker Compose (SQL Server + API). Testcontainers. CI en GitHub Actions. | Tests de auth, refresh, IDOR base, CORS y seguridad por defecto en verde. `docker compose up` levanta todo. | ✅ Aprobada |
| **2. Núcleo de reservas** | Mapa de asientos, bloqueo, liberación, confirmación con idempotencia, cancelación. `sp_ReleaseExpiredSeatLocks` + `SeatLockCleanupService`. Recomendación de asientos. | **TC-CONC-01 a 03 en verde**: un éxito y N−1 respuestas 409. Suite TC-SEAT, TC-BOOK y TC-SEC en verde. | ✅ Aprobada |
| **3. Tiempo real + app móvil** | Hub de SignalR con grupos y notificaciones después del commit. App Flutter: login, cartelera, detalle, funciones, mapa en vivo y accesible, checkout, mis reservas, ticket con QR y perfil. | Dos dispositivos ven el mismo bloqueo en vivo (TC-RT-01). Flujo login → cartelera → función → mapa en vivo → bloqueo → confirmación → ticket con QR probado de punta a punta en un emulador contra el backend real. | 🟡 Implementada, pendiente de aprobación |
| **3.5 Catálogo completo** | Estado de estreno derivado (cartelera, preventa, próximamente) y reservas en preventa. Cines y ciudades. Opiniones y calificaciones. Imágenes servidas por la API. Seed con todos los datos que muestra la pantalla de inicio. App: pestañas, ciudad, tarjeta con datos reales, pantalla de opiniones y cartelera sin iniciar sesión. | Cada elemento de la pantalla de inicio sale de la base de datos ([cobertura](ui-data-coverage.md)). `CatalogCompletenessTests` en verde. | 🟡 Implementada, pendiente de aprobación |
| **4. Panel web + reportes** | API de administración (catálogo, cines, salas y layout, funciones, reservas, usuarios, reportes) y panel React: login con cookie, dashboard, películas, géneros, cines, salas con editor de layout, funciones, reservas, usuarios y reportes. `vw_ShowtimeSales` + `sp_GetSalesSummary`. Tests del panel con Vitest + MSW. ([ADR-018](adr/ADR-018-admin-panel.md)) | TC-REP-01 en verde. KPIs coherentes con el seed. Build de producción sin errores. Recorrido real contra la API y SQL Server ([verificación](testing/phase-4-verification.md)). | ✅ Implementada y verificada (aprobada) |
| **5. Sin conexión** | Drift por usuario, caché de cartelera y reservas, ticket sin conexión, indicador de conectividad, reconciliación y reintentos. | TC-OFF-01 a 03 ejecutados y aprobados. | ⏳ |
| **6. Redis + rendimiento** | Backplane de SignalR, perfil `scale` con 2 instancias, script de demo. Seed de volumen. Medición de índices y SP con planes de ejecución. k6 sobre el mapa, el bloqueo y la cartelera. HybridCache solo si la medición lo justifica. | TC-RT-02 reproducible. `docs/performance.md` y `docs/demo-two-instances.md` con mediciones reales. | ⏳ |
| **7. Cierre** | Colección Postman + Newman en CI. README final con diagramas y capturas. Revisión completa de la documentación. | Checklist de la Definición de Done completo. | ⏳ |

## Definición de Done del proyecto

**Backend**
- [ ] ASP.NET Core Web API en C# / .NET 10
- [ ] Entity Framework Core + SQL Server
- [ ] JWT, refresh con rotación y roles
- [ ] Endpoints REST documentados en Swagger
- [ ] Stored procedures con justificación (ADR-009)
- [ ] Transacciones y concurrencia con RowVersion
- [ ] Bloqueo temporal de asientos con BackgroundService
- [ ] SignalR
- [ ] Redis con uso justificado (ADR-008) y demo de 2 instancias
- [ ] Algoritmo de recomendación de asientos con tests

**Clientes**
- [ ] Panel React + TypeScript
- [ ] App Flutter integrada por REST y SignalR
- [ ] Persistencia local y manejo de conexión y desconexión

**Calidad y documentación**
- [ ] Tests unitarios y de integración, incluido el de concurrencia
- [ ] Casos de prueba funcionales y documentación de QA
- [ ] Requerimientos, historias de usuario y criterios de aceptación
- [ ] KPIs en el dashboard
- [ ] Docker Compose
- [ ] README con diagramas de arquitectura

## Funcionalidades opcionales (solo si sobra tiempo)

- Check-in: el administrador escanea el QR y valida la reserva en línea.
- ETags (`304 Not Modified`) en la cartelera.
- Dashboard en Power BI sobre `vw_ShowtimeSales` (formación aparte; requiere Windows).
