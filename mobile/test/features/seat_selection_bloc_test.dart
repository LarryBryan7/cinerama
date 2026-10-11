import 'dart:async';

import 'package:bloc_test/bloc_test.dart';
import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/core/realtime/seat_realtime.dart';
import 'package:cinerama_mobile/features/seats/bloc/seat_selection_bloc.dart';
import 'package:cinerama_mobile/features/seats/data/seat_models.dart';
import 'package:cinerama_mobile/features/seats/data/seats_repository.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../helpers/fakes.dart';
import '../helpers/seat_helpers.dart';

class MockSeatsRepository extends Mock implements SeatsRepository {}

void main() {
  late MockSeatsRepository repo;
  late FakeSeatRealtime realtime;
  late StreamController<void> ticker;
  late DateTime clock;

  setUp(() {
    repo = MockSeatsRepository();
    realtime = FakeSeatRealtime();
    ticker = StreamController<void>.broadcast();
    clock = DateTime.utc(2026, 10, 3, 20);
    when(() => repo.map(42)).thenAnswer((_) async => mapOf(smallRoom()));
  });

  tearDown(() async {
    await ticker.close();
    await realtime.dispose();
  });

  SeatSelectionBloc build({int pollSeconds = 3}) => SeatSelectionBloc(
        repository: repo,
        realtime: realtime,
        now: () => clock,
        tickerFactory: () => ticker.stream,
        pollSeconds: pollSeconds,
      );

  /// Abre el mapa y espera a que quede listo.
  Future<SeatSelectionBloc> started({int pollSeconds = 3}) async {
    final bloc = build(pollSeconds: pollSeconds)..add(const SeatMapStarted(42));
    await bloc.stream.firstWhere((s) => s.status == SeatMapStatus.ready);
    return bloc;
  }

  Seat find(SeatSelectionBloc bloc, String label) => bloc.state.seats.firstWhere((s) => s.label == label);

  // ── Arranque ────────────────────────────────────────────────────────────

  group('arranque', () {
    test('carga el mapa por REST, lo ordena y empieza a escuchar la función en el tiempo real', () async {
      when(() => repo.map(42)).thenAnswer((_) async => mapOf([seat('B', 2), seat('A', 3), seat('B', 1), seat('A', 1)]));

      final bloc = await started();

      expect(bloc.state.seats.map((s) => s.label), ['A1', 'A3', 'B1', 'B2']);
      expect(bloc.state.price, 12.5);
      expect(realtime.watching, 42);
      await bloc.close();
    });

    blocTest<SeatSelectionBloc, SeatSelectionState>(
      'si REST falla al abrir, muestra el error (el tiempo real no sustituye a REST)',
      setUp: () => when(() => repo.map(42)).thenThrow(const ApiException.network()),
      build: build,
      act: (bloc) => bloc.add(const SeatMapStarted(42)),
      expect: () => [
        isA<SeatSelectionState>().having((s) => s.status, 'status', SeatMapStatus.loading),
        isA<SeatSelectionState>().having((s) => s.status, 'status', SeatMapStatus.failure).having((s) => s.errorCode, 'code', 'network_error'),
      ],
    );

    test('sigue funcionando por REST aunque el tiempo real nunca conecte', () async {
      final bloc = await started();

      expect(bloc.state.realtime, RealtimeState.idle);
      expect(bloc.state.status, SeatMapStatus.ready);
      await bloc.close();
    });
  });

  // ── Toques ──────────────────────────────────────────────────────────────

  group('elegir y liberar asientos (siempre por REST)', () {
    test('tocar un asiento libre lo bloquea: queda como mío solo cuando el servidor responde 200', () async {
      final until = clock.add(const Duration(minutes: 5));
      final completer = Completer<List<Seat>>();
      when(() => repo.lock(42, [idOf('A2')])).thenAnswer((_) => completer.future);
      final bloc = await started();

      bloc.add(SeatTapped(idOf('A2')));
      await pumpEventQueue();
      expect(find(bloc, 'A2').isMine, isFalse, reason: 'sin respuesta del servidor aún no es mío');
      expect(bloc.state.busy, {idOf('A2')});

      completer.complete([seat('A', 2, status: SeatStatus.locked, mine: true, until: until)]);
      await pumpEventQueue();

      expect(find(bloc, 'A2').isMine, isTrue);
      expect(bloc.state.busy, isEmpty);
      expect(bloc.state.lockedUntil, until);
      expect(bloc.state.remaining, const Duration(minutes: 5));
      expect(bloc.state.total, 12.5);
      expect(bloc.state.canContinue, isTrue);
      await bloc.close();
    });

    test('dos toques seguidos sobre el mismo asiento solo hacen UNA solicitud', () async {
      final completer = Completer<List<Seat>>();
      when(() => repo.lock(42, any())).thenAnswer((_) => completer.future);
      final bloc = await started();

      bloc
        ..add(SeatTapped(idOf('A1')))
        ..add(SeatTapped(idOf('A1')))
        ..add(SeatTapped(idOf('A1')));
      await pumpEventQueue();
      completer.complete([seat('A', 1, status: SeatStatus.locked, mine: true, until: clock.add(const Duration(minutes: 5)))]);
      await pumpEventQueue();

      verify(() => repo.lock(42, any())).called(1);
      await bloc.close();
    });

    test('409 seat_unavailable: alguien más lo tomó; se marca como no disponible y se avisa cuál', () async {
      when(() => repo.lock(42, any())).thenThrow(ApiException(
        status: 409,
        code: 'seat_unavailable',
        message: 'x',
        conflicts: [SeatConflict(idOf('A1'), 'A1')],
      ));
      final bloc = await started();

      bloc.add(SeatTapped(idOf('A1')));
      await pumpEventQueue();

      expect(find(bloc, 'A1').status, SeatStatus.locked);
      expect(find(bloc, 'A1').isMine, isFalse);
      expect(bloc.state.busy, isEmpty);
      expect(bloc.state.notice?.message, contains('A1'));
      expect(bloc.state.notice?.kind, NoticeKind.error);
      await bloc.close();
    });

    test('422 lock_limit_exceeded: explica el límite de 10 asientos y no cambia el mapa', () async {
      when(() => repo.lock(42, any())).thenThrow(const ApiException(status: 422, code: 'lock_limit_exceeded', message: 'x'));
      final bloc = await started();

      bloc.add(SeatTapped(idOf('A1')));
      await pumpEventQueue();

      expect(bloc.state.notice?.message, contains('10 asientos'));
      expect(find(bloc, 'A1').isAvailable, isTrue);
      await bloc.close();
    });

    test('sin conexión real: se avisa que hace falta y NO se encola nada', () async {
      when(() => repo.lock(42, any())).thenThrow(const OfflineException());
      final bloc = await started();

      bloc.add(SeatTapped(idOf('A1')));
      await pumpEventQueue();

      expect(bloc.state.notice?.message, contains('conexión'));
      expect(find(bloc, 'A1').isAvailable, isTrue);
      expect(bloc.state.busy, isEmpty);
      verify(() => repo.lock(42, any())).called(1); // un único intento: no hay cola de reintentos
      await bloc.close();
    });

    test('un fallo de red al bloquear no deja el asiento "ocupado" para siempre', () async {
      when(() => repo.lock(42, any())).thenThrow(const ApiException.network());
      final bloc = await started();

      bloc.add(SeatTapped(idOf('A1')));
      await pumpEventQueue();

      expect(bloc.state.busy, isEmpty);
      expect(bloc.state.notice?.message, contains('Sin conexión'));
      await bloc.close();
    });

    test('tocar un asiento propio lo libera por REST', () async {
      final until = clock.add(const Duration(minutes: 5));
      when(() => repo.map(42)).thenAnswer((_) async => mapOf([seat('A', 1, status: SeatStatus.locked, mine: true, until: until), seat('A', 2)]));
      when(() => repo.release(42, [idOf('A1')])).thenAnswer((_) async {});
      final bloc = await started();

      bloc.add(SeatTapped(idOf('A1')));
      await pumpEventQueue();

      verify(() => repo.release(42, [idOf('A1')])).called(1);
      expect(find(bloc, 'A1').isAvailable, isTrue);
      expect(bloc.state.mine, isEmpty);
      expect(bloc.state.lockedUntil, isNull);
      await bloc.close();
    });

    test('un asiento vendido o bloqueado por otra persona no se puede elegir: solo se avisa', () async {
      when(() => repo.map(42)).thenAnswer((_) async => mapOf([seat('A', 1, status: SeatStatus.booked), seat('A', 2, status: SeatStatus.locked)]));
      final bloc = await started();

      bloc..add(SeatTapped(idOf('A1')))..add(SeatTapped(idOf('A2')));
      await pumpEventQueue();

      verifyNever(() => repo.lock(any(), any()));
      expect(bloc.state.notice?.message, contains('no está disponible'));
      await bloc.close();
    });

    test('elegir una sugerencia bloquea todos sus asientos de una vez', () async {
      final until = clock.add(const Duration(minutes: 5));
      when(() => repo.lock(42, [idOf('B2'), idOf('B3')])).thenAnswer(
        (_) async => [seat('B', 2, status: SeatStatus.locked, mine: true, until: until), seat('B', 3, status: SeatStatus.locked, mine: true, until: until)],
      );
      final bloc = await started();

      bloc.add(SeatsChosen([idOf('B2'), idOf('B3')]));
      await pumpEventQueue();

      expect(bloc.state.mine.map((s) => s.label), ['B2', 'B3']);
      expect(bloc.state.total, 25.0);
      await bloc.close();
    });
  });

  // ── Avisos en vivo ──────────────────────────────────────────────────────

  group('avisos en vivo (SeatStatusChanged)', () {
    SeatStatusChanged change(String label, String status, {int showtime = 42}) =>
        SeatStatusChanged(showtime, [SeatChange(idOf(label), status)]);

    test('otra persona bloquea un asiento: se ve como no disponible SIN recargar el mapa', () async {
      final bloc = await started();
      clearInteractions(repo);

      realtime.emit(change('A3', 'Locked'));
      await pumpEventQueue();

      expect(find(bloc, 'A3').status, SeatStatus.locked);
      expect(find(bloc, 'A3').isMine, isFalse);
      verifyNever(() => repo.map(any()));
      await bloc.close();
    });

    test('un asiento se vende: pasa a Vendido', () async {
      final bloc = await started();

      realtime.emit(change('A3', 'Booked'));
      await pumpEventQueue();

      expect(find(bloc, 'A3').status, SeatStatus.booked);
      await bloc.close();
    });

    test('se libera un asiento ajeno: vuelve a estar disponible', () async {
      when(() => repo.map(42)).thenAnswer((_) async => mapOf([seat('A', 1, status: SeatStatus.locked), seat('A', 2)]));
      final bloc = await started();

      realtime.emit(change('A1', 'Available'));
      await pumpEventQueue();

      expect(find(bloc, 'A1').isAvailable, isTrue);
      await bloc.close();
    });

    test('el eco de MI propio bloqueo no me lo quita ni lo cambia', () async {
      final until = clock.add(const Duration(minutes: 5));
      when(() => repo.map(42)).thenAnswer((_) async => mapOf([seat('A', 1, status: SeatStatus.locked, mine: true, until: until)]));
      final bloc = await started();

      realtime.emit(change('A1', 'Locked'));
      await pumpEventQueue();

      expect(find(bloc, 'A1').isMine, isTrue);
      expect(bloc.state.lockedUntil, until);
      await bloc.close();
    });

    test('me avisan que mi asiento volvió a estar disponible (venció): lo pierdo y se me avisa', () async {
      final until = clock.add(const Duration(minutes: 5));
      when(() => repo.map(42)).thenAnswer((_) async => mapOf([seat('A', 1, status: SeatStatus.locked, mine: true, until: until)]));
      final bloc = await started();

      realtime.emit(change('A1', 'Available'));
      await pumpEventQueue();

      expect(find(bloc, 'A1').isMine, isFalse);
      expect(find(bloc, 'A1').isAvailable, isTrue);
      expect(bloc.state.notice?.message, contains('dejó de estar reservado'));
      expect(bloc.state.canContinue, isFalse);
      await bloc.close();
    });

    test('los avisos de OTRA función se ignoran', () async {
      final bloc = await started();
      final before = bloc.state.seats;

      realtime.emit(change('A1', 'Booked', showtime: 99));
      await pumpEventQueue();

      expect(bloc.state.seats, before);
      await bloc.close();
    });

    test('un asiento desconocido en un aviso no rompe nada', () async {
      final bloc = await started();

      realtime.emit(const SeatStatusChanged(42, [SeatChange(999999, 'Locked')]));
      await pumpEventQueue();

      expect(bloc.state.status, SeatMapStatus.ready);
      await bloc.close();
    });

    test('varios cambios en un mismo aviso se aplican juntos', () async {
      final bloc = await started();

      realtime.emit(SeatStatusChanged(42, [SeatChange(idOf('A1'), 'Booked'), SeatChange(idOf('A2'), 'Locked'), SeatChange(idOf('B1'), 'Booked')]));
      await pumpEventQueue();

      expect([find(bloc, 'A1').status, find(bloc, 'A2').status, find(bloc, 'B1').status], [SeatStatus.booked, SeatStatus.locked, SeatStatus.booked]);
      await bloc.close();
    });
  });

  // ── Reconexión y resincronización ───────────────────────────────────────

  group('reconexión', () {
    test('cada vez que el tiempo real queda conectado se pide el mapa COMPLETO por REST', () async {
      final bloc = await started();
      clearInteractions(repo);

      realtime.setState(RealtimeState.connected);
      await pumpEventQueue();

      verify(() => repo.map(42)).called(1);
      expect(bloc.state.realtime, RealtimeState.connected);
      await bloc.close();
    });

    test('perder la conexión NO pide el mapa; reconectar sí, y trae lo que cambió mientras estuvo desconectado', () async {
      final bloc = await started();
      realtime.setState(RealtimeState.connected);
      await pumpEventQueue();
      clearInteractions(repo);

      realtime.setState(RealtimeState.reconnecting);
      await pumpEventQueue();
      verifyNever(() => repo.map(any()));
      expect(bloc.state.realtime, RealtimeState.reconnecting);

      // Mientras estuvo desconectado cambiaron asientos y SignalR no repite los avisos perdidos.
      when(() => repo.map(42)).thenAnswer((_) async => mapOf([
            for (final s in smallRoom())
              if (s.label == 'A1') seat('A', 1, status: SeatStatus.booked) else if (s.label == 'B4') seat('B', 4, status: SeatStatus.locked) else s,
          ]));
      realtime.setState(RealtimeState.connected);
      await pumpEventQueue();

      expect(find(bloc, 'A1').status, SeatStatus.booked);
      expect(find(bloc, 'B4').status, SeatStatus.locked);
      expect(find(bloc, 'A2').isAvailable, isTrue);
      await bloc.close();
    });

    test('al resincronizar se detecta que mi bloqueo venció mientras estuve desconectado', () async {
      final until = clock.add(const Duration(minutes: 5));
      when(() => repo.map(42)).thenAnswer((_) async => mapOf([seat('A', 1, status: SeatStatus.locked, mine: true, until: until), seat('A', 2)]));
      final bloc = await started();
      expect(bloc.state.mine, hasLength(1));

      when(() => repo.map(42)).thenAnswer((_) async => mapOf([seat('A', 1), seat('A', 2)])); // ya no es mío
      realtime.setState(RealtimeState.connected);
      await pumpEventQueue();

      expect(bloc.state.mine, isEmpty);
      expect(bloc.state.notice?.message, contains('venció'));
      await bloc.close();
    });

    test('si la resincronización falla por red, se conserva lo que había (no se vacía el mapa)', () async {
      final bloc = await started();
      final before = bloc.state.seats;

      when(() => repo.map(42)).thenThrow(const ApiException.network());
      realtime.setState(RealtimeState.connected);
      await pumpEventQueue();

      expect(bloc.state.status, SeatMapStatus.ready);
      expect(bloc.state.seats, before);
      await bloc.close();
    });
  });

  // ── Respaldo por REST ───────────────────────────────────────────────────

  group('con el tiempo real caído, REST sigue siendo la verdad', () {
    test('sin tiempo real conectado, el mapa se refresca por REST cada N segundos', () async {
      final bloc = await started(pollSeconds: 3);
      clearInteractions(repo);

      for (var i = 0; i < 6; i++) {
        ticker.add(null);
        await pumpEventQueue();
      }

      verify(() => repo.map(42)).called(2); // en el tick 3 y en el 6
      await bloc.close();
    });

    test('con el tiempo real conectado NO se sondea (los avisos bastan)', () async {
      final bloc = await started(pollSeconds: 3);
      realtime.setState(RealtimeState.connected);
      await pumpEventQueue();
      clearInteractions(repo);

      for (var i = 0; i < 9; i++) {
        ticker.add(null);
        await pumpEventQueue();
      }

      verifyNever(() => repo.map(any()));
      await bloc.close();
    });

    test('el sondeo recoge cambios hechos por otras personas sin necesidad de avisos', () async {
      final bloc = await started(pollSeconds: 2);
      when(() => repo.map(42)).thenAnswer((_) async => mapOf([for (final s in smallRoom()) s.label == 'B2' ? seat('B', 2, status: SeatStatus.booked) : s]));

      ticker..add(null)..add(null);
      await pumpEventQueue();

      expect(find(bloc, 'B2').status, SeatStatus.booked);
      await bloc.close();
    });
  });

  // ── Cuenta regresiva ────────────────────────────────────────────────────

  group('cuenta regresiva del bloqueo (hora del servidor)', () {
    test('actualiza el tiempo restante cada segundo', () async {
      final until = clock.add(const Duration(minutes: 5));
      when(() => repo.map(42)).thenAnswer((_) async => mapOf([seat('A', 1, status: SeatStatus.locked, mine: true, until: until)]));
      final bloc = await started();

      clock = clock.add(const Duration(seconds: 30));
      ticker.add(null);
      await pumpEventQueue();

      expect(bloc.state.remaining, const Duration(minutes: 4, seconds: 30));
      await bloc.close();
    });

    test('al vencer, la pantalla suelta la selección y avisa (el servidor ya la trata como libre)', () async {
      final until = clock.add(const Duration(seconds: 2));
      when(() => repo.map(42)).thenAnswer((_) async => mapOf([seat('A', 1, status: SeatStatus.locked, mine: true, until: until), seat('A', 2)]));
      final bloc = await started();

      clock = clock.add(const Duration(seconds: 3));
      ticker.add(null);
      await pumpEventQueue();

      expect(bloc.state.mine, isEmpty);
      expect(find(bloc, 'A1').isAvailable, isTrue);
      expect(bloc.state.lockedUntil, isNull);
      expect(bloc.state.canContinue, isFalse);
      expect(bloc.state.notice?.message, contains('venció'));
      await bloc.close();
    });

    test('la cuenta usa el MÍNIMO de mis bloqueos (el primero en vencer)', () async {
      final early = clock.add(const Duration(minutes: 1));
      final late = clock.add(const Duration(minutes: 4));
      when(() => repo.map(42)).thenAnswer((_) async => mapOf([
            seat('A', 1, status: SeatStatus.locked, mine: true, until: late),
            seat('A', 2, status: SeatStatus.locked, mine: true, until: early),
          ]));
      final bloc = await started();

      expect(bloc.state.lockedUntil, early);
      await bloc.close();
    });
  });

  test('al cerrar, deja de escuchar el tiempo real', () async {
    final bloc = await started();

    await bloc.close();

    expect(realtime.stopped, isTrue);
  });
}
