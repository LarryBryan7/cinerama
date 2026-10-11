import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:dio/dio.dart';

import '../../features/auth/data/auth_repository.dart';
import '../../features/bookings/data/bookings_repository.dart';
import '../../features/movies/data/catalog_repository.dart';
import '../../features/seats/data/seats_repository.dart';
import '../api/api_client.dart';
import '../api/server_clock.dart';
import '../api/session_storage.dart';
import '../api/token_store.dart';
import '../config/app_config.dart';
import '../connectivity/connectivity_service.dart';
import '../db/user_database.dart';
import '../location/device_location.dart';
import '../realtime/seat_realtime.dart';
import '../realtime/signalr_seat_realtime.dart';
import '../session/session_manager.dart';

/// Raíz de composición: construye y conecta todo. Sin contenedor de inyección: los objetos se pasan por constructor.
class AppDependencies {
  AppDependencies._({
    required this.config,
    required this.clock,
    required this.storage,
    required this.tokens,
    required this.dio,
    required this.connectivity,
    required this.auth,
    required this.catalog,
    required this.seats,
    required this.sessions,
    required this.realtimeFactory,
    required this.location,
    required StreamController<String> sessionLost,
  }) : sessionLostController = sessionLost;

  final AppConfig config;
  final ServerClock clock;
  final SessionStorage storage;
  final TokenStore tokens;
  final Dio dio;
  final ConnectivityService connectivity;
  final AuthRepository auth;
  final CatalogRepository catalog;
  final SeatsRepository seats;
  final SessionManager sessions;

  /// Una conexión de tiempo real por pantalla de asientos.
  final SeatRealtime Function() realtimeFactory;
  /// Ubicación aproximada del dispositivo (solo para calcular distancias en el propio dispositivo).
  final DeviceLocation location;
  final StreamController<String> sessionLostController;

  /// El servidor dejó de aceptar la sesión (código del motivo).
  Stream<String> get sessionLost => sessionLostController.stream;

  factory AppDependencies.create({
    AppConfig? config,
    SessionStorage? storage,
    HttpClientAdapter? adapter,
    ConnectivityService? connectivity,
    Stream<List<ConnectivityResult>>? interfaceChanges,
    DeviceLocation? location,
  }) {
    final cfg = config ?? AppConfig.fromEnvironment();
    final store = storage ?? SecureSessionStorage();
    final clock = ServerClock();
    final lost = StreamController<String>.broadcast();

    final bare = ApiClient.newBareDio(cfg, adapter: adapter);
    final tokens = TokenStore(storage: store, bareDio: bare);
    final dio = ApiClient.newAuthedDio(config: cfg, tokens: tokens, clock: clock, onSessionLost: lost.add, adapter: adapter);

    final conn = connectivity ?? RealConnectivityService(config: cfg, bareDio: bare, interfaceChanges: interfaceChanges);
    final sessions = SessionManager(
      openDatabase: UserDatabase.open,
      destroyDatabase: UserDatabase.destroy,
      bookingsFactory: (local) => BookingsRepository(dio: dio, local: local, connectivity: conn),
    );

    return AppDependencies._(
      config: cfg,
      clock: clock,
      storage: store,
      tokens: tokens,
      dio: dio,
      connectivity: conn,
      auth: AuthRepository(dio: dio, tokens: tokens, storage: store),
      catalog: CatalogRepository(dio),
      seats: SeatsRepository(dio: dio, connectivity: conn),
      sessions: sessions,
      realtimeFactory: () => SignalRSeatRealtime(config: cfg, accessToken: tokens.validAccessToken),
      location: location ?? const GeolocatorDeviceLocation(),
      sessionLost: lost,
    );
  }

  Future<void> dispose() async {
    connectivity.dispose();
    await sessionLostController.close();
  }
}
