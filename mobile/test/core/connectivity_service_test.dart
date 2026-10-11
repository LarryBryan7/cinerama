import 'dart:async';

import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/core/connectivity/connectivity_service.dart';
import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/fakes.dart';

void main() {
  late StreamController<List<ConnectivityResult>> interfaces;
  late bool apiUp;
  late ScriptedAdapter adapter;

  setUp(() {
    interfaces = StreamController<List<ConnectivityResult>>.broadcast();
    apiUp = true;
    adapter = ScriptedAdapter((o) => apiUp ? const FakeResponse(200, 'Healthy') : noNetwork);
  });

  RealConnectivityService service({Duration online = const Duration(hours: 1), Duration offline = const Duration(hours: 1)}) =>
      RealConnectivityService(
        config: testConfig,
        bareDio: dioWith(adapter),
        interfaceChanges: interfaces.stream,
        onlineRecheck: online,
        offlineRecheck: offline,
      );

  test('con red y API respondiendo, está en línea y consulta /health', () async {
    final s = service();

    expect(await s.check(), isTrue);

    expect(s.isOnline, isTrue);
    expect(adapter.requests.single.uri.toString(), 'http://api.test/health');
    s.dispose();
  });

  test('hay una interfaz de red PERO la API no responde: se considera sin conexión (no basta connectivity_plus)', () async {
    apiUp = false;
    final s = service();

    interfaces.add([ConnectivityResult.wifi]); // el sistema dice "hay Wi-Fi"
    await pumpEventQueue();

    expect(s.isOnline, isFalse);
    s.dispose();
  });

  test('sin ninguna interfaz de red, queda sin conexión al instante, sin consultar /health', () async {
    final s = service();
    final changes = <bool>[];
    s.changes.listen(changes.add);

    interfaces.add([ConnectivityResult.none]);
    await pumpEventQueue();

    expect(s.isOnline, isFalse);
    expect(changes, [false]);
    expect(adapter.requests, isEmpty);
    s.dispose();
  });

  test('al volver la red y la API, se recupera y avisa', () async {
    apiUp = false;
    final s = service();
    await s.check();
    final changes = <bool>[];
    s.changes.listen(changes.add);

    apiUp = true;
    interfaces.add([ConnectivityResult.mobile]);
    await pumpEventQueue();

    expect(s.isOnline, isTrue);
    expect(changes, [true]);
    s.dispose();
  });

  test('requireOnline lanza OfflineException cuando /health no responde', () async {
    apiUp = false;
    final s = service();

    await expectLater(s.requireOnline(), throwsA(isA<OfflineException>()));
    s.dispose();
  });

  test('requireOnline reutiliza una comprobación reciente (no consulta /health en cada toque)', () async {
    final s = service();
    await s.check();

    await s.requireOnline();
    await s.requireOnline();
    await s.requireOnline();

    expect(adapter.requests.length, 1);
    s.dispose();
  });

  test('requireOnline no se fía de un "en línea" viejo: vuelve a comprobar', () async {
    var now = DateTime.utc(2026, 10, 2, 12);
    final s = RealConnectivityService(
      config: testConfig,
      bareDio: dioWith(adapter),
      interfaceChanges: interfaces.stream,
      onlineRecheck: const Duration(hours: 1),
      offlineRecheck: const Duration(hours: 1),
      now: () => now,
    );
    await s.check();
    now = now.add(const Duration(seconds: 11)); // pasó el margen de 10 s

    await s.requireOnline();

    expect(adapter.requests.length, 2);
    s.dispose();
  });

  test('comprobaciones simultáneas comparten una sola consulta a /health', () async {
    final s = service();

    await Future.wait([s.check(), s.check(), s.check()]);

    expect(adapter.requests.length, 1);
    s.dispose();
  });

  test('reintenta periódicamente mientras está sin conexión y se recupera solo', () async {
    apiUp = false;
    final s = service(offline: const Duration(milliseconds: 40));
    await s.check();
    expect(s.isOnline, isFalse);

    apiUp = true;
    await Future<void>.delayed(const Duration(milliseconds: 200));

    expect(s.isOnline, isTrue);
    s.dispose();
  });
}
