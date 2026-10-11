# Cinerama · app móvil

Cliente Flutter de Cinerama (proyecto personal de portafolio; usuarios, reservas y pagos ficticios; ver el aviso del [README principal](../README.md#datos-y-aviso)). Al abrirla se ve la **cartelera sin iniciar sesión** (pestañas Cartelera / Preventa / Próximamente, ciudad, calificaciones, cines y opiniones reales). La cuenta se pide solo al elegir asientos, ver tus entradas o entrar al perfil, y después vuelves a donde ibas. Incluye **mapa de asientos en tiempo real**, bloqueo, confirmación y **ticket con QR** que se sigue viendo sin conexión.

## Cómo ejecutarla

Necesita la API en marcha (`docker compose up -d` en la raíz del repositorio; escucha en el puerto 8090).

```bash
cd mobile
flutter pub get
flutter run -d <emulador> --dart-define=API_BASE_URL=http://10.0.2.2:8090   # 10.0.2.2 = tu máquina vista desde el emulador de Android
```

Sin `--dart-define`, el emulador de Android usa `http://10.0.2.2:8090`. En local usa `flutter run --profile` (compilación optimizada, sin banner de depuración): el modo `--release` **no** alcanza la API local porque Android bloquea el tráfico `http` sin cifrar y solo debug y profile lo permiten (`android/app/src/debug` y `android/app/src/profile`). El video del detalle es nativo y necesita ese permiso aunque la API (Dart) funcione sin él. Las cuentas de demostración las crea el seed con las contraseñas de tu `.env` (no se versionan).

## Estructura

```
lib/
  core/       api (Dio, interceptor de renovación), realtime (SignalR), db (Drift), connectivity, session, router, theme
  features/   auth · movies · seats · checkout · bookings · profile · shell
```

Cada feature separa `data` (repositorios y modelos), `bloc` (estado) y `ui`. La lógica vive en los BLoC/Cubits y se prueba sin widgets.

## Pruebas

```bash
flutter analyze
flutter test                      # unitarias y de widgets (369; una pendiente del diseño de la sinopsis)
flutter test integration_test/purchase_flow_test.dart -d <emulador> \
  --dart-define=API_BASE_URL=http://10.0.2.2:8090 \
  --dart-define=E2E_PASSWORD=<contraseña de las cuentas demo>
```

El de integración recorre cartelera sin sesión → película → función → login → vuelta al mapa en vivo → bloqueo → confirmación → QR, y comprueba que un bloqueo hecho por **otra persona vía REST** aparece en el mapa sin recargar. La contraseña llega por `--dart-define` y no se escribe en ningún archivo.

> En macOS sin la licencia de Xcode aceptada, `flutter test` puede fallar al compilar dependencias nativas; `sudo xcodebuild -license accept` lo resuelve.

## Decisiones

- Estado con BLoC/Cubit ([ADR-013](../docs/adr/ADR-013-flutter-state-bloc.md)); paquetes y compatibilidades en esa misma ADR.
- Offline parcial: se ve el ticket, no se escribe sin conexión ([ADR-010](../docs/adr/ADR-010-mobile-offline-strategy.md)).
- Tiempo real: SignalR solo avisa; REST es la verdad ([ADR-016](../docs/adr/ADR-016-signalr-realtime.md)).
- Accesibilidad del mapa: cada asiento tiene color **e** ícono, etiqueta para lectores de pantalla, área táctil de al menos 44 px y zoom/desplazamiento.
- La app nunca registra tokens ni la URL del hub.
- Las imágenes (pósters, logos) llegan como URL absolutas desde la API; si una no existe o falla, se usa un póster de respaldo (`assets/mock/`). El detalle de qué dato sale de dónde está en [docs/ui-data-coverage.md](../docs/ui-data-coverage.md).
- Tráiler: el play de la tarjeta lo reproduce en la propia tarjeta (zona de 261 × 200) con el reproductor oficial de YouTube, y el botón de pantalla completa de YouTube lo abre en horizontal (WebView; necesita internet y no se puede probar en un test, por eso hay un `TrailerPlayer` simulado). Los comentarios del tráiler los trae el servidor con su clave de YouTube (`YOUTUBE__APIKEY` en `.env`); sin clave, esa sección no aparece. Se prueba en el emulador o en un dispositivo: el WebView de YouTube pide una imagen de sistema **con Google Play** (la de Pixel 7 Pro lo es).
- Ubicación: al abrir Inicio se pide el permiso del sistema (aproximada) para mostrar la distancia a cada cine. Sin permiso la app funciona igual y muestra la dirección. La posición **nunca se envía al servidor**. En el emulador de Android la ubicación por defecto es California: cámbiala en *Extended controls → Location* (Chimbote: -9.0745, -78.5936).
