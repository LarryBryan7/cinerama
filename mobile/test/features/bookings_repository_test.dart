import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/core/db/app_database.dart';
import 'package:cinerama_mobile/features/bookings/data/bookings_local_store.dart';
import 'package:cinerama_mobile/features/bookings/data/bookings_repository.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/booking_helpers.dart';
import '../helpers/fakes.dart';

void main() {
  late AppDatabase db;
  late BookingsLocalStore local;
  late FakeConnectivity connectivity;

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    local = BookingsLocalStore(db);
    connectivity = FakeConnectivity();
  });

  tearDown(() => db.close());

  BookingsRepository repo(ScriptedAdapter adapter) =>
      BookingsRepository(dio: dioWith(adapter), local: local, connectivity: connectivity, now: () => DateTime.utc(2026, 10, 2, 15));

  group('confirmar', () {
    test('envía los asientos y la Idempotency-Key, y guarda el ticket en el dispositivo desde el primer momento', () async {
      final adapter = ScriptedAdapter((_) => FakeResponse(201, bookingDetailJson(id: 9, code: 'ABCD2345')));

      final booking = await repo(adapter).confirm(showtimeId: 42, seatIds: [7, 8], idempotencyKey: 'clave-1');

      final request = adapter.requests.single;
      expect(request.path, 'bookings');
      expect(request.data, {'showtimeId': 42, 'seatIds': [7, 8]});
      expect(request.headers['Idempotency-Key'], 'clave-1');
      expect(booking.ticketPayload, 'ABCD2345.firma');
      expect((await local.byId(9))?.ticketPayload, 'ABCD2345.firma');
    });

    test('exige conexión real: sin ella NO se hace ninguna solicitud ni se encola nada', () async {
      connectivity.online = false;
      final adapter = ScriptedAdapter((_) => FakeResponse(201, bookingDetailJson()));

      await expectLater(repo(adapter).confirm(showtimeId: 42, seatIds: [7], idempotencyKey: 'k'), throwsA(isA<OfflineException>()));

      expect(adapter.requests, isEmpty);
      expect(await local.all(), isEmpty);
    });

    test('un conflicto del servidor se informa con su code y no guarda nada', () async {
      final adapter = ScriptedAdapter((_) => FakeResponse.problem(409, 'lock_expired'));

      await expectLater(
        repo(adapter).confirm(showtimeId: 42, seatIds: [7], idempotencyKey: 'k'),
        throwsA(isA<ApiException>().having((e) => e.code, 'code', 'lock_expired')),
      );

      expect(await local.all(), isEmpty);
    });
  });

  group('sincronizar (refresh)', () {
    test('guarda el listado y descarga el ticket de las reservas confirmadas que aún no lo tienen', () async {
      final adapter = ScriptedAdapter((o) {
        if (o.path == 'bookings') {
          return FakeResponse(200, {
            'items': [bookingListItemJson(id: 1, code: 'AAAAAAAA'), bookingListItemJson(id: 2, code: 'BBBBBBBB')],
            'page': 1, 'pageSize': 100, 'totalCount': 2,
          });
        }
        final id = int.parse(o.path.split('/').last);
        return FakeResponse(200, bookingDetailJson(id: id, code: id == 1 ? 'AAAAAAAA' : 'BBBBBBBB'));
      });

      final result = await repo(adapter).refresh();

      expect(result.map((b) => b.id), containsAll([1, 2]));
      expect(result.every((b) => b.hasTicket), isTrue);
      expect(adapter.count('bookings/1'), 1);
      expect(adapter.count('bookings/2'), 1);
    });

    test('no vuelve a descargar un ticket que ya está guardado', () async {
      await local.upsertAll([booking(id: 1, code: 'AAAAAAAA', ticket: 'AAAAAAAA.firma')]);
      final adapter = ScriptedAdapter((o) => o.path == 'bookings'
          ? FakeResponse(200, {'items': [bookingListItemJson(id: 1, code: 'AAAAAAAA')], 'page': 1, 'pageSize': 100, 'totalCount': 1})
          : FakeResponse(500, null));

      await repo(adapter).refresh();

      expect(adapter.count('bookings/1'), 0);
      expect((await local.byId(1))?.ticketPayload, 'AAAAAAAA.firma');
    });

    test('las reservas canceladas no descargan ticket', () async {
      final adapter = ScriptedAdapter((o) => o.path == 'bookings'
          ? FakeResponse(200, {'items': [bookingListItemJson(id: 3, status: 'Cancelled')], 'page': 1, 'pageSize': 100, 'totalCount': 1})
          : FakeResponse(500, null));

      await repo(adapter).refresh();

      expect(adapter.requests.length, 1);
    });

    test('el servidor manda: lo que ya no devuelve desaparece de la caché', () async {
      await local.upsertAll([booking(id: 1), booking(id: 99)]);
      final adapter = ScriptedAdapter((o) => o.path == 'bookings'
          ? FakeResponse(200, {'items': [bookingListItemJson(id: 1)], 'page': 1, 'pageSize': 100, 'totalCount': 1})
          : FakeResponse(500, null));

      final result = await repo(adapter).refresh();

      expect(result.map((b) => b.id), [1]);
    });

    test('sin conexión: falla con error de red y la caché queda INTACTA (no se pierde ningún ticket)', () async {
      await local.upsertAll([booking(id: 1), booking(id: 2)]);
      final adapter = ScriptedAdapter((_) => noNetwork);

      await expectLater(repo(adapter).refresh(), throwsA(isA<ApiException>().having((e) => e.isNetwork, 'isNetwork', isTrue)));

      expect((await local.all()).map((b) => b.id), unorderedEquals([1, 2]));
      expect((await local.byId(1))?.hasTicket, isTrue);
    });
  });

  group('cancelar', () {
    test('actualiza la caché con la reserva cancelada', () async {
      await local.upsertAll([booking(id: 1)]);
      final adapter = ScriptedAdapter((_) => FakeResponse(200, bookingDetailJson(id: 1, status: 'Cancelled')));

      final cancelled = await repo(adapter).cancel(1);

      expect(cancelled.isCancelled, isTrue);
      expect((await local.byId(1))?.isCancelled, isTrue);
    });

    test('exige conexión real', () async {
      connectivity.online = false;
      final adapter = ScriptedAdapter((_) => FakeResponse(200, bookingDetailJson()));

      await expectLater(repo(adapter).cancel(1), throwsA(isA<OfflineException>()));

      expect(adapter.requests, isEmpty);
    });
  });
}
