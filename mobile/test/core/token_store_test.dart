import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/core/api/session_storage.dart';
import 'package:cinerama_mobile/core/api/token_store.dart';
import 'package:cinerama_mobile/features/auth/data/auth_models.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/fakes.dart';

void main() {
  late MemorySessionStorage storage;

  setUp(() {
    storage = MemorySessionStorage()
      ..refreshToken = 'refresh-0'
      ..user = const AppUser(id: 1, fullName: 'Ana Pérez', email: 'ana@example.test', role: 'Customer');
  });

  TokenStore storeWith(ScriptedAdapter adapter, {DateTime Function()? now}) =>
      TokenStore(storage: storage, bareDio: dioWith(adapter), now: now);

  test('tres solicitudes simultáneas que necesitan renovar generan UNA sola llamada de refresh', () async {
    final adapter = ScriptedAdapter((o) async {
      await Future<void>.delayed(const Duration(milliseconds: 30)); // da tiempo a que las otras se amontonen
      return FakeResponse(200, authJson(access: 'access-1', refresh: 'refresh-1'));
    });
    final store = storeWith(adapter);

    final tokens = await Future.wait([store.validAccessToken(), store.validAccessToken(), store.validAccessToken()]);

    expect(tokens, ['access-1', 'access-1', 'access-1']);
    expect(adapter.count('auth/refresh'), 1); // con rotación, dos refresh a la vez se verían como robo del token (ADR-007)
    expect(store.refreshCalls, 1);
  });

  test('guarda el refresh token rotado y el usuario', () async {
    final store = storeWith(ScriptedAdapter((_) => FakeResponse(200, authJson(refresh: 'refresh-nuevo'))));

    await store.refresh();

    expect(storage.refreshToken, 'refresh-nuevo');
    expect(await storage.readUser(), isNotNull);
  });

  test('envía el refresh token guardado en el cuerpo', () async {
    final adapter = ScriptedAdapter((_) => FakeResponse(200, authJson()));

    await storeWith(adapter).refresh();

    expect(adapter.requests.single.data, {'refreshToken': 'refresh-0'});
  });

  test('un access token vigente se reutiliza sin llamar al servidor', () async {
    final adapter = ScriptedAdapter((_) => FakeResponse(200, authJson(access: 'a1')));
    final store = storeWith(adapter);
    await store.refresh();

    expect(await store.validAccessToken(), 'a1');
    expect(adapter.count('auth/refresh'), 1);
  });

  test('un access token a punto de vencer (menos de 30 s) se renueva antes de usarlo', () async {
    var now = DateTime.utc(2026, 10, 2, 12);
    final adapter = ScriptedAdapter((_) => FakeResponse(200, authJson(expires: now.add(const Duration(minutes: 15)))));
    final store = storeWith(adapter, now: () => now);
    await store.refresh();

    now = now.add(const Duration(minutes: 14, seconds: 40));
    await store.validAccessToken();

    expect(adapter.count('auth/refresh'), 2);
  });

  test('un refresh rechazado (401) cierra la sesión: borra lo guardado y lanza SessionExpired', () async {
    final store = storeWith(ScriptedAdapter((_) => FakeResponse.problem(401, 'refresh_token_reused')));

    await expectLater(store.refresh(), throwsA(isA<SessionExpiredException>().having((e) => e.code, 'code', 'refresh_token_reused')));

    expect(storage.refreshToken, isNull);
    expect(storage.user, isNull);
  });

  test('sin red NO se pierde la sesión: se conserva el refresh token', () async {
    final store = storeWith(ScriptedAdapter((_) => noNetwork));

    await expectLater(store.refresh(), throwsA(isA<ApiException>().having((e) => e.isNetwork, 'isNetwork', isTrue)));

    expect(storage.refreshToken, 'refresh-0');
  });

  test('un fallo del servidor (5xx) tampoco pierde la sesión', () async {
    final store = storeWith(ScriptedAdapter((_) => FakeResponse.problem(503, 'internal_error')));

    await expectLater(store.refresh(), throwsA(isA<ApiException>()));

    expect(storage.refreshToken, 'refresh-0');
  });

  test('sin sesión guardada, validAccessToken devuelve null', () async {
    storage.refreshToken = null;

    expect(await storeWith(ScriptedAdapter((_) => FakeResponse(200, authJson()))).validAccessToken(), isNull);
  });

  test('tras un fallo, un nuevo intento vuelve a llamar al servidor', () async {
    var fail = true;
    final adapter = ScriptedAdapter((_) => fail ? noNetwork : FakeResponse(200, authJson(access: 'a-ok')));
    final store = storeWith(adapter);

    await expectLater(store.refresh(), throwsA(isA<ApiException>()));
    fail = false;

    expect(await store.refresh(), 'a-ok');
  });
}
