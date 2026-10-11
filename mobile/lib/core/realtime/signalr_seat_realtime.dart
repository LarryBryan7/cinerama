import 'dart:async';

import 'package:signalr_netcore/signalr_client.dart';

import '../config/app_config.dart';
import 'seat_realtime.dart';

/// Cliente SignalR de los asientos (paquete `signalr_netcore`, no oficial de Microsoft).
///
/// - El token se pide en cada conexión (`accessTokenFactory`), así una reconexión usa un token vigente: el servidor
///   cierra la conexión cuando el token expira (ADR-016) y la app la reabre con uno renovado.
/// - Con WebSockets el cliente manda el token en la URL (`?access_token=`). Por eso **nunca se registra la URL** del hub.
/// - Al reconectar se pierden los grupos del servidor: se vuelve a llamar a `JoinShowtime`.
class SignalRSeatRealtime implements SeatRealtime {
  SignalRSeatRealtime({required this.config, required this.accessToken});

  final AppConfig config;
  final Future<String?> Function() accessToken;

  static const _retryDelaysMs = [0, 2000, 5000, 10000, 20000, 30000];

  final _events = StreamController<SeatStatusChanged>.broadcast();
  final _states = StreamController<RealtimeState>.broadcast();

  HubConnection? _connection;
  int? _showtimeId;
  RealtimeState _state = RealtimeState.idle;
  Timer? _retryTimer;
  int _attempt = 0;
  bool _stopped = false;

  @override
  Stream<SeatStatusChanged> get events => _events.stream;

  @override
  Stream<RealtimeState> get states => _states.stream;

  @override
  RealtimeState get state => _state;

  @override
  Future<void> watch(int showtimeId) async {
    _stopped = false;
    _showtimeId = showtimeId;
    await _connect();
  }

  Future<void> _connect() async {
    if (_stopped || _showtimeId == null) return;
    _setState(RealtimeState.connecting);

    final connection = _connection ??= _build();
    try {
      if (connection.state != HubConnectionState.Connected) {
        await connection.start();
      }
      await connection.invoke('JoinShowtime', args: [_showtimeId!]);
      _attempt = 0;
      _setState(RealtimeState.connected);
    } on Object {
      // Sin servidor, sin sesión o token rechazado: se reintenta con espera creciente. REST sigue funcionando.
      _scheduleRetry();
    }
  }

  HubConnection _build() {
    final connection = HubConnectionBuilder()
        .withUrl(
          config.hubUrl,
          options: HttpConnectionOptions(
            accessTokenFactory: () async => (await accessToken()) ?? '',
            transport: HttpTransportType.WebSockets,
            logMessageContent: false,
            // Sin logger a propósito: los mensajes incluyen la URL y la URL lleva el token.
          ),
        )
        .withAutomaticReconnect(retryDelays: _retryDelaysMs)
        .build();

    connection.on('SeatStatusChanged', (arguments) {
      final payload = arguments?.firstOrNull;
      if (payload is Map) {
        _events.add(SeatStatusChanged.fromJson(Map<String, dynamic>.from(payload)));
      }
    });

    connection.onreconnecting(({error}) => _setState(RealtimeState.reconnecting));

    connection.onreconnected(({connectionId}) async {
      // Reconexión automática: es una conexión nueva, sin grupos. Hay que suscribirse otra vez.
      try {
        await connection.invoke('JoinShowtime', args: [_showtimeId!]);
        _setState(RealtimeState.connected);
      } on Object {
        _scheduleRetry();
      }
    });

    // El servidor cerró la conexión (por ejemplo, el token venció) o se agotaron los reintentos automáticos.
    connection.onclose(({error}) => _scheduleRetry());

    return connection;
  }

  void _scheduleRetry() {
    if (_stopped) return;
    _setState(RealtimeState.reconnecting);
    _retryTimer?.cancel();
    final delay = Duration(milliseconds: _retryDelaysMs[_attempt.clamp(0, _retryDelaysMs.length - 1)]);
    _attempt++;
    _retryTimer = Timer(delay, () => unawaited(_connect()));
  }

  void _setState(RealtimeState state) {
    if (_state == state) return;
    _state = state;
    if (!_states.isClosed) _states.add(state);
  }

  @override
  Future<void> stop() async {
    _stopped = true;
    _retryTimer?.cancel();
    final connection = _connection;
    _connection = null;
    _showtimeId = null;
    _attempt = 0;
    try {
      await connection?.stop();
    } on Object {
      // Ya estaba cerrada.
    }
    _setState(RealtimeState.idle);
  }

  @override
  Future<void> dispose() async {
    await stop();
    await _events.close();
    await _states.close();
  }
}
