import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:dio/dio.dart';

import '../api/api_exception.dart';
import '../config/app_config.dart';

/// Conectividad REAL con la API. `connectivity_plus` solo dice si hay una interfaz de red activa; un Wi-Fi con portal
/// cautivo o sin salida a internet figura como "conectado". Por eso el estado se confirma consultando `/health` (ADR-010).
abstract class ConnectivityService {
  /// Último estado conocido.
  bool get isOnline;

  Stream<bool> get changes;

  /// Consulta `/health` ahora mismo.
  Future<bool> check();

  /// Para las operaciones que exigen conexión (bloquear, confirmar, cancelar): no se encolan, se rechazan sin red.
  Future<void> requireOnline();

  void dispose();
}

class RealConnectivityService implements ConnectivityService {
  RealConnectivityService({
    required this.config,
    required Dio bareDio,
    Stream<List<ConnectivityResult>>? interfaceChanges,
    this.onlineRecheck = const Duration(seconds: 30),
    this.offlineRecheck = const Duration(seconds: 8),
    this.freshFor = const Duration(seconds: 10),
    DateTime Function()? now,
  })  : _dio = bareDio,
        _now = now ?? DateTime.now {
    _subscription = (interfaceChanges ?? Connectivity().onConnectivityChanged).listen(_onInterfaceChanged);
    _schedule();
  }

  final AppConfig config;
  final Dio _dio;
  final Duration onlineRecheck;
  final Duration offlineRecheck;
  final Duration freshFor;
  final DateTime Function() _now;

  final _controller = StreamController<bool>.broadcast();
  StreamSubscription<List<ConnectivityResult>>? _subscription;
  Timer? _timer;
  bool _online = true; // optimista hasta la primera comprobación
  DateTime? _lastCheck;
  Future<bool>? _inFlight;

  @override
  bool get isOnline => _online;

  @override
  Stream<bool> get changes => _controller.stream;

  @override
  Future<bool> check() => _inFlight ??= _ping().whenComplete(() => _inFlight = null);

  @override
  Future<void> requireOnline() async {
    // Si se comprobó hace poco y había conexión, se confía; si no, se confirma con /health.
    final recent = _lastCheck != null && _now().difference(_lastCheck!) < freshFor;
    if (_online && recent) return;
    if (!await check()) throw const OfflineException();
  }

  Future<bool> _ping() async {
    bool ok;
    try {
      final response = await _dio.get<dynamic>(
        config.healthUrl,
        options: Options(
          receiveTimeout: const Duration(seconds: 4),
          sendTimeout: const Duration(seconds: 4),
          validateStatus: (status) => status == 200,
        ),
      );
      ok = response.statusCode == 200;
    } on DioException {
      ok = false;
    }
    _lastCheck = _now();
    _set(ok);
    return ok;
  }

  void _onInterfaceChanged(List<ConnectivityResult> results) {
    if (results.every((r) => r == ConnectivityResult.none)) {
      _set(false); // sin ninguna interfaz no hace falta preguntar
    } else {
      unawaited(check()); // hay red, pero ¿llega a la API?
    }
  }

  void _set(bool online) {
    final changed = online != _online;
    _online = online;
    if (changed) _controller.add(online);
    _schedule();
  }

  void _schedule() {
    _timer?.cancel();
    _timer = Timer(_online ? onlineRecheck : offlineRecheck, () => unawaited(check()));
  }

  @override
  void dispose() {
    _timer?.cancel();
    unawaited(_subscription?.cancel());
    unawaited(_controller.close());
  }
}
