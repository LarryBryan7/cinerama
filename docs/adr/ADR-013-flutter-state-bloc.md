# ADR-013: BLoC/Cubit para el estado en Flutter

- **Estado:** Aceptada
- **Fecha:** 2026-10-02

## Contexto

La pantalla más compleja de la app es el mapa de asientos. Su estado cambia por dos fuentes que pueden llegar en cualquier orden:

1. las acciones del usuario (tocar un asiento, pedir una recomendación);
2. los eventos del servidor (`SeatStatusChanged` por SignalR, la reconexión, el vencimiento del bloqueo).

## Decisión

- **flutter_bloc**:
  - `Bloc` para `SeatSelectionBloc`, donde las dos fuentes se modelan como eventos (`SeatTapped`, `RemoteSeatsChanged`, `HubReconnected`, `LockExpired`) y se procesan en orden.
  - `Cubit` para pantallas simples (cartelera, perfil).
- Estructura por feature: `features/<feature>/{data, bloc, ui}` y un `core/` compartido (`api`, `db`, `auth`, `realtime`).
- Navegación con go_router, con una redirección por estado de autenticación.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Riverpod | Igual de válido técnicamente. Se descarta solo porque el autor ya tiene experiencia práctica con BLoC, lo que permite dedicar el tiempo a lo nuevo del proyecto: SignalR y offline. |
| `setState` / Provider simple | Insuficiente para ordenar eventos concurrentes y probarlos con `bloc_test`. |

## Consecuencias

- El mapa se prueba con `bloc_test`: se emiten secuencias de eventos (por ejemplo, un toque y un evento remoto sobre el mismo asiento) y se verifican los estados resultantes.
- Solo se reconstruyen los widgets de los asientos afectados (`BlocSelector` por asiento), no la grilla completa.

## Implementación (fase 3)

**Paquetes de la app y por qué cada uno** (versiones verificadas con `flutter pub get` sobre Flutter 3.44, sin actualizar Flutter ni hacer *upgrades* globales):

| Paquete | Para qué | Nota |
|---|---|---|
| `flutter_bloc`, `equatable` | Estado | ADR-013 |
| `dio` | HTTP con un interceptor que renueva el token **una sola vez** aunque fallen varias solicitudes a la vez, y solo ante `token_expired` | |
| `go_router` | Navegación con redirección según el estado de la sesión y pestañas con estado propio | |
| `flutter_secure_storage` | El *refresh token* (Keychain / Keystore). El access token vive solo en memoria | |
| `drift` + `drift_flutter` | Caché local de reservas y tickets, una base por usuario que se borra al cerrar sesión (ADR-010) | |
| `signalr_netcore` | Cliente de SignalR | **No oficial**, mantenimiento moderado. Detrás de la interfaz `SeatRealtime`; ver ADR-016 |
| `connectivity_plus` | Aviso rápido de cambios de red | No decide por sí solo: la conectividad real se confirma con `GET /health` |
| `qr_flutter` | Dibuja el QR en el dispositivo | El contenido lo firma el servidor |
| `youtube_player_iframe` | Reproductor oficial de YouTube para el tráiler (WebView con la API IFrame) | La forma permitida de insertar tráileres. Se esconde tras `TrailerPlayer` para poder probarlo sin WebView. Arrastra `webview_flutter` y `url_launcher` |
| `video_player` | Video corto del detalle (autoplay, sin sonido, en bucle) | Plugin oficial de Flutter. Se prueba con un controlador falso, sin plataforma |
| `geolocator` | Ubicación aproximada para calcular la distancia a cada cine | Solo en el dispositivo: no se envía al servidor. Precisión baja. Fase 3.5 |

**Hallazgo de compatibilidad.** `drift` y `drift_dev` 2.35 no resolvían junto con `bloc_test` en este Flutter (conflicto con el *pin* de `test_api`), por lo que se fijaron en `>=2.34.0 <2.35.0`; y `intl` se fijó en `^0.20.2`, la versión que exige `flutter_localizations`. Son restricciones de resolución, no de funcionalidad: se revisan al actualizar Flutter.

**Dos decisiones del flujo de compra:**
- La clave de idempotencia (UUID v4 con `Random.secure`) se genera **una vez por intento de compra** y se reutiliza en cada reintento, para que un reintento tras un corte nunca produzca una segunda reserva.
- Elegir un asiento **bloquea de inmediato en el servidor** (con cuenta regresiva basada en la hora del servidor, no la del teléfono); no hay selección "local" que luego falle al confirmar.

## Motivación de portafolio

**Producto.** El patrón encaja con el problema. Reutilizar un patrón conocido es una decisión de eficiencia, no de presentación.
