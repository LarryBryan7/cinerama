# Matriz de trazabilidad

Relaciona cada requerimiento con sus historias, reglas, casos de prueba y tests automatizados.

- Los tests de integración llevan `[Trait("Case", "…")]` con el id del caso o la regla que verifican, así que se pueden ejecutar por caso: `dotnet test --filter "Case=TC-SEC-07"`.
- Los nombres de la columna *Test automatizado* son clases de `backend/tests/Cinerama.IntegrationTests` (o `Cinerama.UnitTests` cuando se indica). En las fases pendientes son nombres planificados.
- *Estado*: ⏳ pendiente · ✅ pasa · ❌ falla.

## Fase 1 (implementada)

| Requerimiento | Historia | Reglas | Casos de prueba | Test automatizado | Estado |
|---|---|---|---|---|---|
| RF-AUTH-01 | HU-01 | RN-01, RN-02, RN-04 | — | `Auth/RegisterTests`, `RegisterRequestValidatorTests` (Unit), `IdorTests.ACustomer_CannotPromoteThemselves…` | ✅ |
| RF-AUTH-02 | HU-02 | RN-03, RN-05, RN-06 | TC-SEC-08 | `Auth/LoginTests`, `Platform/PlatformTests.AuthEndpoints_AreRateLimitedPerIp` | ✅ |
| RF-AUTH-03 | HU-03 | RN-05 | TC-SEC-02, TC-SEC-07 | `Auth/RefreshTokenTests` (incluye doble refresh simultáneo), `AuthorizationTests.ProtectedEndpoint_WithExpiredToken…`, `TokenServiceTests` (Unit) | ✅ |
| RF-AUTH-04 | HU-04 | RN-05 | TC-SEC-07 | `Auth/LogoutTests` | ✅ |
| RF-AUTH-05 | HU-05 | RN-34 | TC-SEC-06 (parte) | `Security/IdorTests` (`/me` y `PUT /me` ignoran cualquier identidad del request) | ✅ |
| RF-CAT-01 | HU-06 | RN-07 | — | `Catalog/CatalogTests.Movies_*`, `Genres_*`, `PagingTests` (Unit) | ✅ |
| RF-CAT-02, RF-SHOW-01 | HU-07 | RN-23, RN-38 | TC-SEAT-10 (parte) | `Catalog/CatalogTests.MovieDetail_*`, `Showtime*`, `Dates_AreSerializedAsUtc…` | ✅ |
| RF-USR-01 (API) | HU-20 | RN-03, RN-39 | TC-SEC-03, TC-SEC-08 | `Security/UserAdministrationTests`, `AuthorizationTests.AdminEndpoints_*` | ✅ |
| RNF-03 | — | RN-33, RN-34 | TC-SEC-11 | `AuthorizationTests.OnlyTheExpectedEndpointsArePublic`, `EveryNonPublicEndpoint_Returns401WithoutAToken` | ✅ |
| RNF-05 | — | — | TC-SEC-09 | `LoginTests.Login_WithUseCookie…`, `RefreshTokenTests.Refresh_ViaCookie…`, `Refresh_WithARejectedCookie_ClearsIt` | ✅ |
| RNF-06 | — | — | TC-SEC-10 | `Platform/PlatformTests.Cors_*` | ✅ |
| RNF-04, RNF-17 | — | — | — | `Platform/PlatformTests.Startup_*` (configuración inválida impide arrancar) | ✅ |
| RNF-01 (barreras de la base) | — | RN-27, RN-31 | TC-BOOK-04 (parte) | `Database/DatabaseConstraintsTests` (RowVersion, índice único filtrado de `BookingSeats`, CHECK de coherencia de `ShowSeats`) | ✅ |
| RNF-16 | — | — | — | `Platform/SeedTests` (datos coherentes, ficticios y cargados una sola vez) | ✅ |

> Los casos TC-SEC-04 a 06 sobre **reservas** ajenas, TC-SEC-03 sobre rutas de películas y TC-SEAT-10 sobre `/seats` se completan en las fases 2 y 4, cuando existan esos endpoints.

## Fase 2 (implementada)

| Requerimiento | Historia | Reglas | Casos de prueba | Test automatizado | Estado |
|---|---|---|---|---|---|
| RF-SEAT-01 | HU-08 | RN-23 | TC-SEAT-04, TC-SEAT-10 | `Seating/SeatMapTests` | ✅ |
| RF-SEAT-02 | HU-09 | RN-16, RN-18 a RN-21 | TC-SEAT-01, 02, 03, 06, 07, 09, 10 | `Seating/SeatLockTests`, `ShowSeatTests` (Unit) | ✅ |
| RF-SEAT-03 | HU-10 | RN-22 | TC-SEAT-08, TC-SEAT-11 | `Seating/SeatReleaseTests` | ✅ |
| RF-SEAT-04 | HU-11 | — | — | `Seating/SeatRecommendationEndpointTests`, `SeatRecommendationServiceTests` (Unit, 15 casos) | ✅ |
| RF-BOOK-01 | HU-12 | RN-24 a RN-28, RN-31 | TC-BOOK-01, 02, 03, 07 | `Bookings/BookingFlowTests`, `BookingCodeGeneratorTests` y `TicketSignerTests` (Unit) | ✅ |
| RF-BOOK-02 | HU-13 | RN-32, RN-34 | TC-SEC-06 | `Bookings/BookingAuthorizationTests.List_*`, `BookingFlowTests.List_*` | ✅ |
| RF-BOOK-03 | HU-14 | RN-32, RN-35 | TC-SEC-04 | `BookingAuthorizationTests.Get_*`, `BookingFlowTests.Confirm_ReturnsATicketPayload…` | ✅ |
| RF-BOOK-04 | HU-15 | RN-30, RN-32 | TC-BOOK-04, 05, 06, 08 · TC-SEC-05 | `Bookings/BookingCancelTests`, `BookingCancellationTests` (Unit) | ✅ |
| RF-SYS-01 (liberación) | HU-16 | RN-23 | TC-SEAT-05, CU-06 | `Seating/SeatLockCleanupTests` (procedimiento, lotes, frontera, `BackgroundService`) | ✅ |
| RNF-01 | — | RN-18, RN-19, RN-20, RN-27, RN-31 | TC-CONC-01 a 08 | `Concurrency/SeatConcurrencyTests`, `Concurrency/RaceInjectionTests`, `Database/DatabaseConstraintsTests` | ✅ |
| RN-40 | — | RN-40 | TC-OPS-02 | `Platform/Phase2PlatformTests.SeatEndpoints_AreRateLimitedPerUser…` | ✅ |
| ADR-014 (valores locales) | — | — | TC-OPS-01 | `ProductionConfigurationTests` (Unit), `Phase2PlatformTests.NonDevelopment_*` | ✅ |
| ADR-015 (401 / 404) | — | — | TC-SEC-12 | `Platform/PlatformTests.UnknownRoute_*` | ✅ |

> TC-SEC-04, 05 y 06 (reservas ajenas) quedaron completos en esta fase. **RF-BOOK-05** (listado del administrador) sigue en la fase 4. En esta fase los avisos se verificaban con un notificador de pruebas (`RecordingSeatNotifier`); la fase 3 los prueba con SignalR real.

## Fase 3 (implementada)

| Requerimiento | Historia | Reglas | Casos de prueba | Test automatizado | Estado |
|---|---|---|---|---|---|
| RF-SEAT-05 (backend) | HU-16 | RN-34 | TC-RT-01, 03 | `Realtime/SeatHubTests` (dos clientes, aislamiento por función, el evento no revela quién, cancelación y limpieza avisan) | ✅ |
| ADR-016 (autenticación del hub) | — | — | TC-RT-04, 05 | `Realtime/SeatHubSecurityTests` (sin token, basura, manipulado, otra clave, vencido; cierre al vencer; token en la URL solo en `/hubs`) | ✅ |
| ADR-016 / ADR-005 (reconexión) | — | — | TC-RT-06 | `Realtime/SeatHubResilienceTests` (reconexión), `seat_selection_bloc_test` (Flutter: nuevo mapa completo al reconectar) | ✅ |
| ADR-005 (después del commit) | — | — | TC-RT-07 | `SeatHubCommitOrderTests` | ✅ |
| ADR-016 (SignalR caído) | — | — | TC-RT-08 | `SeatHubDownTests` | ✅ |
| Secretos fuera de registros y respuestas | — | — | TC-SEC-13 | `Realtime/SecretsNeverLeakTests`, `LoggingConfigurationTests` (Unit) | ✅ |
| RF-AUTH-02/03 (app) | HU-02, HU-03 | RN-05 | — | Flutter: `auth_bloc_test`, `auth_interceptor_test` (refresh único y solo con `token_expired`), `token_store_test`, `login_page` (widget) | ✅ |
| RF-CAT-01/02 (app) | HU-06, HU-07 | — | — | Flutter: E2E en emulador (`integration_test/purchase_flow_test`) | ✅ |
| RF-SEAT-01 a 04 (app) | HU-08 a HU-11 | RN-18 a RN-21 | TC-RT-01 | Flutter: `seat_selection_bloc_test`, `seat_grid_test` (etiquetas, íconos, pasillo, 44 px), E2E | ✅ |
| RF-BOOK-01 (app) | HU-12 | RN-27 | — | Flutter: `checkout_cubit_test` (misma clave de idempotencia en los reintentos), E2E | ✅ |
| RF-BOOK-02 a 04 (app), ticket QR | HU-13 a HU-15 | RN-32 | — | Flutter: `bookings_repository_test`, `bookings_local_store_test`, `ticket_and_bookings_cubit_test`, `ticket_page_test` (QR sin conexión, cancelada sin QR) | ✅ |
| Conectividad real | — | — | — | Flutter: `connectivity_service_test` (`/health`, no solo la red del teléfono), `auth_and_banner_test` | ✅ |

> El offline de la fase 3 es solo el necesario para ver el ticket: el catálogo y la cola de reintentos llegan en la fase 5 (ADR-010).

## Fase 3.5: estreno, cines, opiniones y datos de la pantalla (implementada)

| Requerimiento | Historia | Reglas | Casos de prueba | Test automatizado | Estado |
|---|---|---|---|---|---|
| RF-CAT-05 | — | RN-41 | TC-PRE-01 | `MovieStatusTests` (Unit), `Catalog/MovieStatusAndMediaTests` | ✅ |
| RF-BOOK-06 | — | RN-42 | TC-PRE-02 | `Seating/PreSaleSalesTests` | ✅ |
| RF-CAT-06 | — | RN-49, RN-50 | TC-CIN-01 | `Catalog/CinemaTests`, `PreSaleSalesTests.AShowtimeInAClosedCinema…` | ✅ |
| RF-CAT-07 | — | RN-44, RN-45, RN-48 | TC-REV-01 | `Catalog/ReviewTests` (promedio, destacada, privacidad), `MovieStatusAndMediaTests` (imágenes y `/media`) | ✅ |
| RF-REV-01, RF-REV-02 | — | RN-43, RN-46 | TC-REV-02, TC-REV-03 | `Catalog/ReviewTests`, `Concurrency/ReviewRaceTests` | ✅ |
| Seguridad de opiniones | — | RN-34, RN-47 | TC-SEC-14, TC-OPS-03 | `ReviewTests.TheAuthorIsAlwaysTheCaller…`, `DeleteOnlyEverTouches…`, `WritingIsRateLimited…` | ✅ |
| Datos completos | — | — | TC-DATA-01 | `Platform/CatalogCompletenessTests` (6), `SeedTests` | ✅ |
| Inicio sin sesión (app) | — | — | TC-UI-01 | Flutter: `app_router_test` (8), `auth_interceptor_test`, `integration_test/purchase_flow_test` | ✅ |
| Tarjeta, pestañas, ciudad y opiniones (app) | — | RN-41, RN-44, RN-45 | TC-UI-02 | Flutter: `cartelera_slide_test`, `reviews_and_home_test`, `movies_cubits_test`, `movie_models_test`, `catalog_repository_test` | ✅ |
| Carrusel de tarjetas | — | — | TC-UI-03 | Flutter: `cartelera_slide_test` (grupo *carrusel*: centrado, vecinas, arrastre, toque, precarga, gesto vertical), `reviews_and_home_test` (reinicio al cambiar de pestaña) | ✅ |
| Video corto del detalle | — | RN-48 | TC-UI-04 | Backend: `MovieStatusAndMediaTests` (mp4 y rangos, `previewVideoUrl`), `CatalogCompletenessTests.ThePreviewVideos…`. Flutter: `movie_preview_video_test` (13 + detalle), `cartelera_slide_test` (grupo *video en la tarjeta activa*) | ✅ |
| Tráiler en el carrusel y pantalla completa | — | RN-51, RN-54 | TC-UI-05 | Flutter: `cartelera_slide_test` (grupo *tráiler de YouTube*), `youtube_id_test` | ✅ (reproductor simulado) |
| Comentarios del tráiler | — | RN-52, RN-53 | TC-REV-04 | Backend: `YouTubeTests` (Unit, 40), `TrailerCommentsTests` (Int.). Flutter: `reviews_and_home_test`, `movies_cubits_test`, `movie_models_test`, `catalog_repository_test` | ✅ (YouTube simulado) |
| Ubicación, distancia y "cerca de ti" | — | RN-49 | TC-LOC-01 | Flutter: `geo_test` (haversine, formato, `NearbyCinemas`), `movies_cubits_test` (grupo *LocationCubit*), `reviews_and_home_test` (permiso, negado, bloqueado) | ✅ |

## Fase 4: panel de administración y reportes (implementada)

| Requerimiento | Historia | Reglas | Casos de prueba | Test automatizado | Estado |
|---|---|---|---|---|---|
| RF-BOOK-05, RF-USR-01 (filtros) | — | RN-33 | TC-ADM-03 | Backend: `Admin/RoomShowtimeAdminTests` (reservas), `Admin/UserFilterTests`. Panel: `BookingsPage.test`, `UsersPage.test` | ✅ |
| RF-CAT-03, RF-CAT-04, RF-CAT-09 | HU-17 | RN-08, RN-09, RN-51 | TC-ADM-03, TC-ADM-04 | Backend: `Admin/CatalogAdminTests` (30). Panel: `MovieFormPage.test`, `GenresPage.test` | ✅ |
| RF-ROOM-01, RF-ROOM-02 | HU-18 | RN-10, RN-11 | TC-ADM-05 | Backend: `RoomShowtimeAdminTests`. Panel: `RoomLayoutPage.test` | ✅ |
| RF-SHOW-02, RF-SHOW-03 | HU-19 | RN-12 a RN-15, RN-17, RN-56 | TC-ADM-01, 02, 06, 07 | Backend: `RoomShowtimeAdminTests` (34, incluye las carreras de 2 y 8 administradores). Panel: `ShowtimesPage.test` | ✅ |
| RF-REP-01 a RF-REP-04 | HU-21 | RN-36 a RN-38, RN-57 | TC-REP-01 | Backend: `Admin/ReportsTests` (15: valores calculados a mano, canceladas, rango, días vacíos, desempate). Panel: `ReportsPage.test`, `DateRangeControl.test`, `DashboardPage.test` | ✅ |
| Sesión del panel | — | RN-58 a RN-60 | TC-WEB-01 | Panel: `api/client.test` (refresh único, sesión perdida), `auth/auth.test`, `lib/time.test` | ✅ |
| Panel completo | — | RN-58, RN-59 | TC-WEB-02 a 04 | Panel: 12 archivos, 68 pruebas (Vitest + Testing Library + MSW) | ✅ (API simulada) |
| Decimales de reportes | — | RN-36, RN-37 | TC-ADM-08 | Backend: `Admin/ReportsTests.TheAverageOccupancyKeepsItsSixDecimals…` | ✅ |
| Recorrido real (navegador + API + SQL Server) | — | todas las de la fase | — | Manual con Chrome y scripts de Playwright, no incluidos en el repositorio: ver [phase-4-verification.md](phase-4-verification.md) | ✅ (ver alcance y límites allí) |

> Las pruebas automatizadas del panel usan una API **simulada** con MSW: verifican que la interfaz envía y entiende el contrato. El navegador contra la API real y SQL Server se verificó aparte, a mano, y está documentado en [phase-4-verification.md](phase-4-verification.md).

## Fases siguientes (planificadas)

| Requerimiento | Historia | Reglas | Casos de prueba | Test automatizado (planificado) | Fase | Estado |
|---|---|---|---|---|---|---|
| RF-SEAT-05 (varias instancias), RNF-09 | HU-16 | — | TC-RT-02 | Script de demo de 2 instancias | 6 | ⏳ |
| RF-OFF-01 a RF-OFF-04 | HU-22 | — | TC-OFF-01, 02, 03 | `bloc_test`: `SeatSelectionBloc offline`, `BookingsRepository sync` | 5 | ⏳ |

## Reglas sin caso de prueba dedicado

Se verifican con tests unitarios de validadores o de dominio y no requieren un caso funcional aparte: RN-02, RN-09, RN-10, RN-12, RN-14, RN-28 y RN-38. Si alguna produce un defecto, se agrega su caso.
