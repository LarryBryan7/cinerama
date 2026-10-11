import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:cinerama_mobile/core/api/api_client.dart';
import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/core/config/app_config.dart';
import 'package:cinerama_mobile/core/connectivity/connectivity_service.dart';
import 'package:cinerama_mobile/core/realtime/seat_realtime.dart';
import 'package:dio/dio.dart';

const testConfig = AppConfig(apiBaseUrl: 'http://api.test');

/// Una respuesta HTTP simulada.
class FakeResponse {
  const FakeResponse(this.status, [this.body, this.headers = const {}]);

  final int status;
  final Object? body;
  final Map<String, String> headers;

  /// Un ProblemDetails como el que devuelve la API.
  factory FakeResponse.problem(int status, String code, {String? detail, List<Map<String, Object>>? conflicts, Map<String, List<String>>? errors}) =>
      FakeResponse(status, {
        'status': status,
        'code': code,
        'title': 'Error',
        'detail': ?detail,
        'conflicts': ?conflicts,
        'errors': ?errors,
      });
}

/// Adaptador de Dio que responde con un guion y registra cada solicitud.
class ScriptedAdapter implements HttpClientAdapter {
  ScriptedAdapter(this.handler);

  final FutureOr<FakeResponse> Function(RequestOptions options) handler;
  final List<RequestOptions> requests = [];

  int count(String pathEnd) => requests.where((r) => r.path.endsWith(pathEnd)).length;

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream, Future<void>? cancelFuture) async {
    requests.add(options);
    final response = await handler(options);
    if (response.status == -1) {
      throw DioException.connectionError(requestOptions: options, reason: 'sin red (simulado)');
    }
    final bytes = utf8.encode(response.body == null ? '' : jsonEncode(response.body));
    return ResponseBody.fromBytes(bytes, response.status, headers: {
      Headers.contentTypeHeader: [Headers.jsonContentType],
      for (final h in response.headers.entries) h.key: [h.value],
    });
  }

  @override
  void close({bool force = false}) {}
}

/// Respuesta que simula la ausencia total de red.
const noNetwork = FakeResponse(-1);

Dio dioWith(ScriptedAdapter adapter) => ApiClient.newBareDio(testConfig, adapter: adapter);

class FakeConnectivity implements ConnectivityService {
  FakeConnectivity({this.online = true});

  bool online;
  final _controller = StreamController<bool>.broadcast();
  int requireCalls = 0;

  @override
  bool get isOnline => online;

  @override
  Stream<bool> get changes => _controller.stream;

  void set(bool value) {
    online = value;
    _controller.add(value);
  }

  @override
  Future<bool> check() async => online;

  @override
  Future<void> requireOnline() async {
    requireCalls++;
    if (!online) throw const OfflineException();
  }

  @override
  void dispose() => unawaited(_controller.close());
}

/// Tiempo real controlable desde el test.
class FakeSeatRealtime implements SeatRealtime {
  final _events = StreamController<SeatStatusChanged>.broadcast();
  final _states = StreamController<RealtimeState>.broadcast();
  RealtimeState _state = RealtimeState.idle;
  int? watching;
  int watchCalls = 0;
  bool stopped = false;

  @override
  Stream<SeatStatusChanged> get events => _events.stream;

  @override
  Stream<RealtimeState> get states => _states.stream;

  @override
  RealtimeState get state => _state;

  void emit(SeatStatusChanged event) => _events.add(event);

  void setState(RealtimeState state) {
    _state = state;
    _states.add(state);
  }

  @override
  Future<void> watch(int showtimeId) async {
    watching = showtimeId;
    watchCalls++;
  }

  @override
  Future<void> stop() async => stopped = true;

  @override
  Future<void> dispose() async {
    await _events.close();
    await _states.close();
  }
}

/// Un ProblemDetails de la API listo para usar en un [DioException].
Map<String, dynamic> authJson({String access = 'access-1', String refresh = 'refresh-1', int userId = 1, DateTime? expires}) => {
      'accessToken': access,
      'accessTokenExpiresAt': (expires ?? DateTime.now().toUtc().add(const Duration(minutes: 15))).toIso8601String(),
      'refreshToken': refresh,
      'refreshTokenExpiresAt': DateTime.now().toUtc().add(const Duration(days: 7)).toIso8601String(),
      'user': {'id': userId, 'fullName': 'Ana Pérez', 'email': 'ana@example.test', 'role': 'Customer'},
    };
