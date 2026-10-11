import 'package:cinerama_mobile/core/api/api_client.dart';
import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/core/api/server_clock.dart';
import 'package:cinerama_mobile/core/api/session_storage.dart';
import 'package:cinerama_mobile/core/api/token_store.dart';
import 'package:cinerama_mobile/features/auth/data/auth_models.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/fakes.dart';

void main() {
  late MemorySessionStorage storage;
  late List<String> sessionLost;

  setUp(() {
    storage = MemorySessionStorage()
      ..refreshToken = 'refresh-0'
      ..user = const AppUser(id: 1, fullName: 'Ana', email: 'a@x.test', role: 'Customer');
    sessionLost = [];
  });

  /// Cliente con sesión: `handler` responde a la API y `refresh` a /auth/refresh.
  ({Dio dio, ScriptedAdapter adapter, TokenStore tokens}) client({
    required FakeResponse Function(RequestOptions) api,
    FakeResponse Function(RequestOptions)? refresh,
    ServerClock? clock,
  }) {
    final adapter = ScriptedAdapter((o) async {
      if (o.path.endsWith('auth/refresh')) {
        await Future<void>.delayed(const Duration(milliseconds: 20));
        return (refresh ?? (_) => FakeResponse(200, authJson(access: 'access-nuevo', refresh: 'refresh-1')))(o);
      }
      return api(o);
    });
    final tokens = TokenStore(storage: storage, bareDio: ApiClient.newBareDio(testConfig, adapter: adapter));
    final dio = ApiClient.newAuthedDio(
      config: testConfig,
      tokens: tokens,
      clock: clock ?? ServerClock(),
      onSessionLost: sessionLost.add,
      adapter: adapter,
    );
    return (dio: dio, adapter: adapter, tokens: tokens);
  }

  test('agrega el access token como Bearer', () async {
    final c = client(api: (_) => const FakeResponse(200, {'ok': true}));
    c.tokens.setSession(AuthSession.fromJson(authJson(access: 'mi-token')));

    await c.dio.get<dynamic>('movies');

    expect(c.adapter.requests.single.headers['Authorization'], 'Bearer mi-token');
  });

  test('401 token_expired: renueva la sesión y repite la solicitud sin que se note', () async {
    var calls = 0;
    final c = client(api: (o) {
      calls++;
      return o.headers['Authorization'] == 'Bearer access-nuevo' ? const FakeResponse(200, {'ok': true}) : FakeResponse.problem(401, 'token_expired');
    });
    c.tokens.setSession(AuthSession.fromJson(authJson(access: 'viejo')));

    final response = await c.dio.get<Map<String, dynamic>>('bookings');

    expect(response.data, {'ok': true});
    expect(calls, 2); // la original y el reintento
    expect(c.adapter.count('auth/refresh'), 1);
    expect(sessionLost, isEmpty);
  });

  test('tres solicitudes con el token vencido a la vez: UN solo refresh y las tres se repiten', () async {
    final c = client(api: (o) => o.headers['Authorization'] == 'Bearer access-nuevo' ? const FakeResponse(200, {'ok': true}) : FakeResponse.problem(401, 'token_expired'));
    c.tokens.setSession(AuthSession.fromJson(authJson(access: 'viejo')));

    final results = await Future.wait([c.dio.get<dynamic>('a'), c.dio.get<dynamic>('b'), c.dio.get<dynamic>('c')]);

    expect(results.map((r) => r.statusCode), [200, 200, 200]);
    expect(c.adapter.count('auth/refresh'), 1);
  });

  test('si el refresh falla, avisa que la sesión se perdió y la solicitud falla con su error original', () async {
    final c = client(
      api: (_) => FakeResponse.problem(401, 'token_expired'),
      refresh: (_) => FakeResponse.problem(401, 'invalid_refresh_token'),
    );
    c.tokens.setSession(AuthSession.fromJson(authJson(access: 'viejo')));

    await expectLater(c.dio.get<dynamic>('bookings'), throwsA(isA<DioException>()));

    expect(sessionLost, ['invalid_refresh_token']);
    expect(storage.refreshToken, isNull);
  });

  test('un 401 que NO es token_expired (token inválido) no intenta renovar: la sesión se perdió', () async {
    final c = client(api: (_) => FakeResponse.problem(401, 'invalid_token'));
    c.tokens.setSession(AuthSession.fromJson(authJson(access: 'raro')));

    await expectLater(c.dio.get<dynamic>('bookings'), throwsA(isA<DioException>()));

    expect(c.adapter.count('auth/refresh'), 0);
    expect(sessionLost, ['invalid_token']);
  });

  test('solo se reintenta una vez: si el servidor vuelve a responder token_expired, la sesión se pierde', () async {
    final c = client(api: (_) => FakeResponse.problem(401, 'token_expired'));
    c.tokens.setSession(AuthSession.fromJson(authJson(access: 'viejo')));

    await expectLater(c.dio.get<dynamic>('bookings'), throwsA(isA<DioException>()));

    expect(c.adapter.count('auth/refresh'), 1); // no entra en un bucle de refrescos
    expect(sessionLost, isNotEmpty);
  });

  test('sin red durante el refresh, la sesión se conserva', () async {
    final c = client(api: (_) => FakeResponse.problem(401, 'token_expired'), refresh: (_) => noNetwork);
    c.tokens.setSession(AuthSession.fromJson(authJson(access: 'viejo')));

    await expectLater(c.dio.get<dynamic>('bookings'), throwsA(isA<DioException>()));

    expect(sessionLost, isEmpty);
    expect(storage.refreshToken, 'refresh-0');
  });

  test('SIN sesión la cartelera se pide igual: sin Authorization, sin renovar y sin avisar que se perdió la sesión', () async {
    storage
      ..refreshToken = null
      ..user = null;
    final c = client(api: (_) => const FakeResponse(200, {'items': []}));

    final response = await c.dio.get<dynamic>('movies');

    expect(response.statusCode, 200);
    expect(c.adapter.requests.single.headers.containsKey('Authorization'), isFalse);
    expect(c.adapter.count('auth/refresh'), 0);
    expect(sessionLost, isEmpty); // ver la cartelera sin cuenta no es "sesión perdida"
  });

  test('las rutas de autenticación no llevan token ni intentan renovar', () async {
    final c = client(api: (_) => FakeResponse.problem(401, 'invalid_credentials'));

    await expectLater(c.dio.post<dynamic>('auth/login', data: {}), throwsA(isA<DioException>()));

    expect(c.adapter.requests.single.headers.containsKey('Authorization'), isFalse);
    expect(c.adapter.count('auth/refresh'), 0);
    expect(sessionLost, isEmpty); // un login fallido no es "sesión perdida"
  });

  test('sincroniza el reloj con la cabecera Date del servidor', () async {
    final clock = ServerClock(deviceNow: () => DateTime.utc(2026, 10, 2, 12, 0, 0));
    final c = client(
      api: (_) => const FakeResponse(200, {'ok': true}, {'date': 'Fri, 02 Oct 2026 12:05:00 GMT'}),
      clock: clock,
    );
    c.tokens.setSession(AuthSession.fromJson(authJson()));

    await c.dio.get<dynamic>('movies');

    expect(clock.offset, const Duration(minutes: 5));
    expect(clock.now(), DateTime.utc(2026, 10, 2, 12, 5, 0));
  });

  test('guardApi traduce un error de Dio a ApiException con el code del servidor', () async {
    final c = client(api: (_) => FakeResponse.problem(409, 'lock_expired'));
    c.tokens.setSession(AuthSession.fromJson(authJson()));

    await expectLater(guardApi(() => c.dio.get<dynamic>('bookings')), throwsA(isA<ApiException>().having((e) => e.code, 'code', 'lock_expired')));
  });
}
