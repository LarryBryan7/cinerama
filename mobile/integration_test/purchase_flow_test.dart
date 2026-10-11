// Flujo completo contra el backend real (docker compose), en un emulador o dispositivo:
// cartelera SIN sesión → película → función → login (solo ahora) → de vuelta al mapa en tiempo real → bloqueo →
// confirmación → ticket con QR.
//
//   flutter test integration_test/purchase_flow_test.dart -d <emulador> \
//     --dart-define=API_BASE_URL=http://10.0.2.2:8090 \
//     --dart-define=E2E_PASSWORD=<contraseña de las cuentas demo> \
//     --dart-define=E2E_EMAIL=cliente1@cinerama.test --dart-define=E2E_OTHER_EMAIL=cliente2@cinerama.test
//
// Al abrir Inicio la app pide el permiso de ubicación. Para que el diálogo del sistema no estorbe, concédelo antes (opcional):
//   adb shell pm grant com.example.cinerama_mobile android.permission.ACCESS_FINE_LOCATION
// Las credenciales llegan por --dart-define y nunca se escriben en el repositorio ni en la salida del test.
import 'dart:async';

import 'package:cinerama_mobile/features/bookings/ui/ticket_page.dart';
import 'package:cinerama_mobile/features/movies/ui/movie_detail_page.dart';
import 'package:cinerama_mobile/main.dart' as app;
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

const _baseUrl = String.fromEnvironment('API_BASE_URL', defaultValue: 'http://10.0.2.2:8090');
const _email = String.fromEnvironment('E2E_EMAIL', defaultValue: 'cliente1@cinerama.test');
const _otherEmail = String.fromEnvironment('E2E_OTHER_EMAIL', defaultValue: 'cliente2@cinerama.test');
const _password = String.fromEnvironment('E2E_PASSWORD');

/// Otra persona usando la API por REST: representa al "segundo cliente" que provoca cambios en el mapa.
class _OtherClient {
  _OtherClient() : _dio = Dio(BaseOptions(baseUrl: '$_baseUrl/api/v1', validateStatus: (_) => true));

  final Dio _dio;

  Future<void> login() async {
    final response = await _dio.post<Map<String, dynamic>>('/auth/login', data: {'email': _otherEmail, 'password': _password});
    expect(response.statusCode, 200, reason: 'el login de la segunda cuenta debe funcionar');
    _dio.options.headers['Authorization'] = 'Bearer ${response.data!['accessToken']}';
  }

  /// La película que la cartelera muestra primero (mismo orden y mismo filtro que la app: en cartelera, ciudad Chimbote).
  Future<Map<String, dynamic>> firstMovieOnTheBillboard() async {
    final response = await _dio.get<Map<String, dynamic>>('/movies', queryParameters: {'status': 'nowShowing', 'city': 'Chimbote', 'pageSize': 1});
    return (response.data!['items'] as List<dynamic>).first as Map<String, dynamic>;
  }

  /// La función más lejana de esa película que tenga asientos de sobra (no choca con la regla de cancelación).
  Future<Map<String, dynamic>> farthestShowtimeOf(int movieId) async {
    final response = await _dio.get<Map<String, dynamic>>('/movies/$movieId');
    final showtimes = (response.data!['upcomingShowtimes'] as List<dynamic>).cast<Map<String, dynamic>>();
    return showtimes.lastWhere((s) => (s['availableSeats'] as int) > 4);
  }

  Future<List<Map<String, dynamic>>> freeSeats(int showtimeId) async {
    final response = await _dio.get<Map<String, dynamic>>('/showtimes/$showtimeId/seats');
    return (response.data!['seats'] as List<dynamic>).cast<Map<String, dynamic>>().where((s) => s['status'] == 'Available').toList();
  }

  Future<int?> lock(int showtimeId, int seatId) async => (await _dio.post<dynamic>('/showtimes/$showtimeId/seats/lock', data: {'seatIds': [seatId]})).statusCode;

  Future<int?> release(int showtimeId, int seatId) async => (await _dio.post<dynamic>('/showtimes/$showtimeId/seats/release', data: {'seatIds': [seatId]})).statusCode;
}

/// Avanza el reloj de la prueba hasta que [finder] aparezca (o falla con [reason]). Evita `pumpAndSettle`, que no termina
/// mientras haya animaciones o temporizadores vivos (cuenta regresiva del bloqueo, indicador de progreso).
Future<void> pumpUntil(WidgetTester tester, Finder finder, {required String reason, Duration timeout = const Duration(seconds: 20)}) async {
  final deadline = DateTime.now().add(timeout);
  while (DateTime.now().isBefore(deadline)) {
    await tester.pump(const Duration(milliseconds: 200));
    if (finder.evaluate().isNotEmpty) return;
  }
  fail('Tiempo agotado esperando: $reason');
}

Finder _iconOnSeat(String label, IconData icon) => find.descendant(of: find.byKey(Key('seat-$label')), matching: find.byIcon(icon));

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('compra completa con el mapa de asientos en tiempo real', (tester) async {
    expect(_password, isNotEmpty, reason: 'falta --dart-define=E2E_PASSWORD=…');

    // Datos de la prueba: la película que se ve primero, una función lejana suya y dos asientos libres.
    final other = _OtherClient();
    await other.login();
    final movie = await other.firstMovieOnTheBillboard();
    final movieId = movie['id'] as int;
    final showtime = await other.farthestShowtimeOf(movieId);
    final showtimeId = showtime['id'] as int;
    final free = await other.freeSeats(showtimeId);
    expect(free.length, greaterThan(4), reason: 'la función elegida necesita asientos libres');
    final watched = free[0]; // lo bloquea la otra persona mientras miramos el mapa
    final mine = free[free.length - 1]; // lo elegimos nosotros
    final watchedLabel = watched['label'] as String;
    final mineLabel = mine['label'] as String;

    // 1. Arranque SIN sesión: se ven las películas de frente, no el login.
    unawaited(app.main());
    await pumpUntil(tester, find.text(movie['title'] as String), reason: 'la cartelera con la primera película, sin pedir login');
    expect(find.byKey(const Key('login-email')), findsNothing, reason: 'sin sesión se entra directo a la cartelera');
    expect(find.text('CineFlash⚡'), findsOneWidget);
    expect(find.byKey(const Key('tab-nowShowing')), findsOneWidget);

    // 2. Película → función. Hasta aquí no hace falta cuenta.
    await tester.tap(find.byKey(Key('movie-$movieId')));
    await pumpUntil(tester, find.text('Funciones'), reason: 'el detalle de la película');
    // La lista de funciones es perezosa y la función elegida es la más lejana: hay que desplazarse hasta ella.
    await tester.scrollUntilVisible(find.byKey(Key('showtime-$showtimeId')), 300, scrollable: find.descendant(of: find.byType(MovieDetailPage), matching: find.byType(Scrollable)), maxScrolls: 60);
    await tester.pump(const Duration(milliseconds: 300));
    await tester.tap(find.byKey(Key('showtime-$showtimeId')));

    // 3. Elegir asientos sí exige cuenta: aparece el login y, al terminar, se vuelve al mapa de esa función.
    await pumpUntil(tester, find.byKey(const Key('login-email')), reason: 'el login al intentar elegir asientos');
    await tester.enterText(find.byKey(const Key('login-email')), _email);
    await tester.enterText(find.byKey(const Key('login-password')), _password);
    await tester.tap(find.byKey(const Key('login-submit')));

    // 4. Mapa: carga por REST y se conecta el tiempo real.
    await pumpUntil(tester, find.byKey(Key('seat-$mineLabel')), reason: 'el mapa de asientos, de vuelta tras el login');
    await pumpUntil(tester, find.text('En vivo'), reason: 'la conexión de tiempo real (SignalR)');
    expect(_iconOnSeat(watchedLabel, Icons.event_seat_outlined), findsOneWidget, reason: 'al inicio el asiento observado está libre');

    // 5. Otra persona bloquea un asiento por REST → el mapa lo muestra sin recargar; y al liberarlo, vuelve a estar libre.
    expect(await other.lock(showtimeId, watched['seatId'] as int), 200);
    await pumpUntil(tester, _iconOnSeat(watchedLabel, Icons.schedule), reason: 'ver el bloqueo ajeno en vivo');
    expect(await other.release(showtimeId, watched['seatId'] as int), anyOf(200, 204));
    await pumpUntil(tester, _iconOnSeat(watchedLabel, Icons.event_seat_outlined), reason: 'ver la liberación ajena en vivo');

    // 6. Yo elijo un asiento: se bloquea en el servidor y aparece la cuenta regresiva.
    await tester.ensureVisible(find.byKey(Key('seat-$mineLabel')));
    await tester.tap(find.byKey(Key('seat-$mineLabel')));
    await pumpUntil(tester, find.byKey(const Key('lock-countdown')), reason: 'el bloqueo propio y su cuenta regresiva');
    expect(_iconOnSeat(mineLabel, Icons.check), findsOneWidget);

    // 7. Confirmar → ticket con QR.
    await tester.tap(find.byKey(const Key('continue-button')));
    await pumpUntil(tester, find.byKey(const Key('confirm-button')), reason: 'la pantalla de confirmación');
    await tester.tap(find.byKey(const Key('confirm-button')));
    await pumpUntil(tester, find.byKey(const Key('ticket-qr')), reason: 'el ticket con su QR tras confirmar');

    final qr = tester.widget<TicketQr>(find.byKey(const Key('ticket-qr')));
    final code = tester.widget<SelectableText>(find.byKey(const Key('ticket-code'))).data!;
    expect(qr.data, startsWith('$code.'), reason: 'el QR lleva el código de reserva y la firma del servidor');
    expect(find.byKey(const Key('ticket-offline-note')), findsNothing, reason: 'con conexión no se muestra el aviso de "guardado"');
  });
}
