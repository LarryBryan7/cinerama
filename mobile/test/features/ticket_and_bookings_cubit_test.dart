import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/features/bookings/bloc/bookings_cubit.dart';
import 'package:cinerama_mobile/features/bookings/bloc/ticket_cubit.dart';
import 'package:cinerama_mobile/features/bookings/data/bookings_repository.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../helpers/booking_helpers.dart';

class MockBookings extends Mock implements BookingsRepository {}

void main() {
  late MockBookings repo;

  setUp(() => repo = MockBookings());

  group('ticket', () {
    test('muestra primero el ticket guardado en el dispositivo y luego lo confirma con el servidor', () async {
      final cached = booking(id: 1, syncedAt: DateTime.utc(2026, 10, 2, 10));
      final fresh = booking(id: 1, syncedAt: DateTime.utc(2026, 10, 2, 15));
      when(() => repo.cachedById(1)).thenAnswer((_) async => cached);
      when(() => repo.fetchDetail(1)).thenAnswer((_) async => fresh);
      final cubit = TicketCubit(repo, 1);
      final states = <TicketState>[];
      cubit.stream.listen(states.add);

      await cubit.load();
      await pumpEventQueue(); // los eventos del stream se entregan de forma asíncrona

      expect(states.first.booking, cached); // instantáneo, antes de hablar con el servidor
      expect(states.first.fromCache, isFalse, reason: 'mientras se confirma NO se afirma que estamos sin conexión (lo leería un lector de pantalla)');
      expect(states.last.fromCache, isFalse);
      expect(states.last.booking, fresh);
    });

    test('SIN CONEXIÓN el ticket guardado se sigue mostrando, con su QR firmado y marcado como guardado', () async {
      final cached = booking(id: 1, ticket: 'K7QH3MXP.firma-del-servidor');
      when(() => repo.cachedById(1)).thenAnswer((_) async => cached);
      when(() => repo.fetchDetail(1)).thenThrow(const ApiException.network());
      final cubit = TicketCubit(repo, 1);

      await cubit.load();

      expect(cubit.state.status, TicketStatus.ready);
      expect(cubit.state.fromCache, isTrue);
      expect(cubit.state.booking?.ticketPayload, 'K7QH3MXP.firma-del-servidor'); // el QR es lo que firmó el servidor
      expect(cubit.state.booking?.hasTicket, isTrue);
    });

    test('sin conexión y sin nada guardado: no hay ticket que mostrar', () async {
      when(() => repo.cachedById(1)).thenAnswer((_) async => null);
      when(() => repo.fetchDetail(1)).thenThrow(const ApiException.network());
      final cubit = TicketCubit(repo, 1);

      await cubit.load();

      expect(cubit.state.status, TicketStatus.notFound);
    });

    test('si el servidor dice que la reserva no existe (404), no se muestra aunque hubiera algo guardado', () async {
      when(() => repo.cachedById(1)).thenAnswer((_) async => booking(id: 1));
      when(() => repo.fetchDetail(1)).thenThrow(const ApiException(status: 404, code: 'booking_not_found', message: 'x'));
      final cubit = TicketCubit(repo, 1);

      await cubit.load();

      expect(cubit.state.status, TicketStatus.notFound);
    });

    test('una reserva cancelada deja de ofrecer QR', () async {
      when(() => repo.cachedById(1)).thenAnswer((_) async => booking(id: 1, status: 'Cancelled'));
      when(() => repo.fetchDetail(1)).thenAnswer((_) async => booking(id: 1, status: 'Cancelled', cancelledAt: DateTime.utc(2026, 10, 3)));
      final cubit = TicketCubit(repo, 1);

      await cubit.load();

      expect(cubit.state.booking?.isCancelled, isTrue);
      expect(cubit.state.booking?.hasTicket, isFalse);
    });

    test('cancelar sin conexión: se rechaza, la reserva sigue como estaba y no se encola nada', () async {
      when(() => repo.cachedById(1)).thenAnswer((_) async => booking(id: 1));
      when(() => repo.fetchDetail(1)).thenAnswer((_) async => booking(id: 1));
      when(() => repo.cancel(1)).thenThrow(const OfflineException());
      final cubit = TicketCubit(repo, 1);
      await cubit.load();

      await cubit.cancel();

      expect(cubit.state.errorCode, 'offline');
      expect(cubit.state.booking?.isCancelled, isFalse);
      verify(() => repo.cancel(1)).called(1);
    });

    test('cancelar con éxito actualiza la reserva', () async {
      when(() => repo.cachedById(1)).thenAnswer((_) async => booking(id: 1));
      when(() => repo.fetchDetail(1)).thenAnswer((_) async => booking(id: 1));
      when(() => repo.cancel(1)).thenAnswer((_) async => booking(id: 1, status: 'Cancelled'));
      final cubit = TicketCubit(repo, 1);
      await cubit.load();

      await cubit.cancel();

      expect(cubit.state.booking?.isCancelled, isTrue);
      expect(cubit.state.cancelling, isFalse);
    });

    test('cancelar fuera del plazo muestra el motivo del servidor', () async {
      when(() => repo.cachedById(1)).thenAnswer((_) async => booking(id: 1));
      when(() => repo.fetchDetail(1)).thenAnswer((_) async => booking(id: 1));
      when(() => repo.cancel(1)).thenThrow(const ApiException(status: 422, code: 'cancellation_window_closed', message: 'x'));
      final cubit = TicketCubit(repo, 1);
      await cubit.load();

      await cubit.cancel();

      expect(cubit.state.errorCode, 'cancellation_window_closed');
      expect(cubit.state.booking?.isCancelled, isFalse);
    });
  });

  group('mis reservas', () {
    test('muestra lo guardado enseguida y luego lo sincronizado', () async {
      final cached = [booking(id: 1)];
      final fresh = [booking(id: 2), booking(id: 1)];
      when(() => repo.cached()).thenAnswer((_) async => cached);
      when(() => repo.refresh()).thenAnswer((_) async => fresh);
      final cubit = BookingsCubit(repo);
      final states = <BookingsState>[];
      cubit.stream.listen(states.add);

      await cubit.load();
      await pumpEventQueue();

      expect(states.first.items, cached);
      expect(states.first.loading, isTrue);
      expect(states.last.items, fresh);
      expect(states.last.loading, isFalse);
      expect(states.last.fromCache, isFalse);
    });

    test('sin conexión se queda con lo guardado, lo indica y no pierde nada', () async {
      final cached = [booking(id: 1), booking(id: 2)];
      when(() => repo.cached()).thenAnswer((_) async => cached);
      when(() => repo.refresh()).thenThrow(const ApiException.network());
      final cubit = BookingsCubit(repo);

      await cubit.load();

      expect(cubit.state.items, cached);
      expect(cubit.state.fromCache, isTrue);
      expect(cubit.state.loading, isFalse);
      expect(cubit.state.errorCode, 'network_error');
      expect(cubit.state.lastSync, isNotNull);
    });
  });
}
