import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/features/bookings/data/bookings_repository.dart';
import 'package:cinerama_mobile/features/checkout/bloc/checkout_cubit.dart';
import 'package:cinerama_mobile/features/seats/data/seat_models.dart';
import 'package:cinerama_mobile/features/seats/data/seats_repository.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../helpers/booking_helpers.dart';
import '../helpers/seat_helpers.dart';

class MockSeats extends Mock implements SeatsRepository {}

class MockBookings extends Mock implements BookingsRepository {}

void main() {
  late MockSeats seats;
  late MockBookings bookings;
  final until = DateTime.utc(2026, 10, 3, 20, 5);

  setUp(() {
    seats = MockSeats();
    bookings = MockBookings();
    when(() => seats.map(42)).thenAnswer((_) async => mapOf([
          seat('F', 7, status: SeatStatus.locked, mine: true, until: until),
          seat('F', 8, status: SeatStatus.locked, mine: true, until: until.add(const Duration(minutes: 1))),
          seat('F', 9),
        ], price: 12.5));
  });

  CheckoutCubit cubit({List<Duration> delays = const [Duration.zero, Duration.zero]}) =>
      CheckoutCubit(showtimeId: 42, seats: seats, bookings: bookings, retryDelays: delays, keyGenerator: () => 'clave-fija');

  void stubConfirm(Future<dynamic> Function(Invocation) answer) =>
      when(() => bookings.confirm(showtimeId: any(named: 'showtimeId'), seatIds: any(named: 'seatIds'), idempotencyKey: any(named: 'idempotencyKey')))
          .thenAnswer((i) async => await answer(i) as dynamic);

  group('cargar', () {
    test('lee del SERVIDOR los asientos que tengo bloqueados, su total y el vencimiento más próximo', () async {
      final c = cubit();

      await c.load();

      expect(c.state.status, CheckoutStatus.ready);
      expect(c.state.seats.map((s) => s.label), ['F7', 'F8']); // F9 no es mío
      expect(c.state.total, 25.0);
      expect(c.state.lockedUntil, until);
    });

    test('si ya no tengo ningún asiento bloqueado, vuelve al mapa', () async {
      when(() => seats.map(42)).thenAnswer((_) async => mapOf([seat('F', 7)]));
      final c = cubit();

      await c.load();

      expect(c.state.status, CheckoutStatus.seatsLost);
    });

    test('si no se puede leer el mapa, muestra el error', () async {
      when(() => seats.map(42)).thenThrow(const ApiException.network());
      final c = cubit();

      await c.load();

      expect(c.state.status, CheckoutStatus.failure);
      expect(c.state.errorCode, 'network_error');
    });
  });

  group('confirmar', () {
    test('éxito: envía los asientos y la clave de idempotencia, y queda confirmado', () async {
      stubConfirm((_) async => booking());
      final c = cubit();
      await c.load();

      await c.confirm();

      expect(c.state.status, CheckoutStatus.confirmed);
      expect(c.state.booking?.code, 'K7QH3MXP');
      verify(() => bookings.confirm(showtimeId: 42, seatIds: [idOf('F7'), idOf('F8')], idempotencyKey: 'clave-fija')).called(1);
    });

    test('si la red falla, reintenta con la MISMA clave y termina creando una sola reserva', () async {
      var calls = 0;
      final keys = <String>[];
      when(() => bookings.confirm(showtimeId: any(named: 'showtimeId'), seatIds: any(named: 'seatIds'), idempotencyKey: any(named: 'idempotencyKey')))
          .thenAnswer((i) async {
        keys.add(i.namedArguments[#idempotencyKey] as String);
        if (++calls < 3) throw const ApiException.network();
        return booking();
      });
      final c = cubit();
      await c.load();

      await c.confirm();

      expect(calls, 3);
      expect(keys.toSet(), {'clave-fija'}); // la misma clave en cada intento: si el primero sí llegó, no se duplica la reserva (RN-27)
      expect(c.state.status, CheckoutStatus.confirmed);
    });

    test('un error 5xx también se reintenta', () async {
      var calls = 0;
      stubConfirm((_) async {
        if (++calls == 1) throw const ApiException(status: 503, code: 'internal_error', message: 'x');
        return booking();
      });
      final c = cubit();
      await c.load();

      await c.confirm();

      expect(calls, 2);
      expect(c.state.status, CheckoutStatus.confirmed);
    });

    test('agotados los reintentos, muestra el fallo y conserva la selección para volver a intentar', () async {
      var calls = 0;
      stubConfirm((_) async {
        calls++;
        throw const ApiException.network();
      });
      final c = cubit(delays: const [Duration.zero, Duration.zero]);
      await c.load();

      await c.confirm();

      expect(calls, 3); // el intento original + 2 reintentos
      expect(c.state.status, CheckoutStatus.failure);
      expect(c.state.errorCode, 'network_error');
      expect(c.state.seats, hasLength(2)); // la selección sigue ahí
    });

    test('tras un fallo de red, tocar de nuevo reutiliza la misma clave', () async {
      final keys = <String>[];
      var fail = true;
      when(() => bookings.confirm(showtimeId: any(named: 'showtimeId'), seatIds: any(named: 'seatIds'), idempotencyKey: any(named: 'idempotencyKey')))
          .thenAnswer((i) async {
        keys.add(i.namedArguments[#idempotencyKey] as String);
        if (fail) throw const ApiException.network();
        return booking();
      });
      final c = cubit(delays: const []);
      await c.load();

      await c.confirm();
      fail = false;
      await c.confirm();

      expect(keys, ['clave-fija', 'clave-fija']);
      expect(c.state.status, CheckoutStatus.confirmed);
    });

    test('409 lock_expired: se perdió la selección, NO se reintenta y se vuelve al mapa', () async {
      var calls = 0;
      stubConfirm((_) async {
        calls++;
        throw const ApiException(status: 409, code: 'lock_expired', message: 'x');
      });
      final c = cubit();
      await c.load();

      await c.confirm();

      expect(calls, 1);
      expect(c.state.status, CheckoutStatus.seatsLost);
    });

    test('un error 4xx de negocio no se reintenta nunca', () async {
      var calls = 0;
      stubConfirm((_) async {
        calls++;
        throw const ApiException(status: 422, code: 'sales_closed', message: 'x');
      });
      final c = cubit();
      await c.load();

      await c.confirm();

      expect(calls, 1);
      expect(c.state.status, CheckoutStatus.failure);
      expect(c.state.errorCode, 'sales_closed');
    });

    test('sin conexión real: falla de inmediato, sin reintentos ni cola', () async {
      var calls = 0;
      stubConfirm((_) async {
        calls++;
        throw const OfflineException();
      });
      final c = cubit();
      await c.load();

      await c.confirm();

      expect(calls, 1);
      expect(c.state.errorCode, 'offline');
    });

    test('un segundo toque mientras confirma no lanza otra solicitud', () async {
      stubConfirm((_) async {
        await Future<void>.delayed(const Duration(milliseconds: 30));
        return booking();
      });
      final c = cubit();
      await c.load();

      await Future.wait([c.confirm(), c.confirm(), c.confirm()]);

      verify(() => bookings.confirm(showtimeId: any(named: 'showtimeId'), seatIds: any(named: 'seatIds'), idempotencyKey: any(named: 'idempotencyKey'))).called(1);
    });
  });

  group('clave de idempotencia', () {
    test('tiene formato UUID v4 y no se repite', () {
      final keys = {for (var i = 0; i < 500; i++) generateIdempotencyKey()};

      expect(keys.length, 500);
      expect(keys.every((k) => RegExp(r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$').hasMatch(k)), isTrue);
      expect(keys.first.length, lessThanOrEqualTo(64)); // el servidor admite hasta 64 caracteres
    });
  });
}
