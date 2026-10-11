import 'package:cinerama_mobile/core/db/app_database.dart';
import 'package:cinerama_mobile/features/bookings/data/bookings_local_store.dart';
import 'package:drift/drift.dart' show driftRuntimeOptions;
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/booking_helpers.dart';

void main() {
  late AppDatabase db;
  late BookingsLocalStore store;

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    store = BookingsLocalStore(db);
  });

  tearDown(() => db.close());

  test('guarda y devuelve una reserva con su ticket firmado', () async {
    await store.upsertAll([booking(id: 7, ticket: 'K7QH3MXP.firma-del-servidor')]);

    final saved = await store.byId(7);

    expect(saved?.ticketPayload, 'K7QH3MXP.firma-del-servidor');
    expect(saved?.seatLabels, ['F7', 'F8']);
    expect(saved?.totalAmount, '25.00');
    expect(saved?.startsAt, DateTime.utc(2026, 10, 10, 22));
  });

  test('el listado del servidor NO trae el ticket: si ya estaba guardado, se conserva', () async {
    await store.upsertAll([booking(id: 1, ticket: 'K7QH3MXP.firma')]);

    await store.upsertAll([booking(id: 1, ticket: null)]); // viene de GET /bookings (sin ticketPayload)

    expect((await store.byId(1))?.ticketPayload, 'K7QH3MXP.firma');
  });

  test('una reserva nueva sin ticket se guarda sin ticket', () async {
    await store.upsertAll([booking(id: 2, ticket: null)]);

    final saved = await store.byId(2);
    expect(saved?.ticketPayload, isNull);
    expect(saved?.hasTicket, isFalse);
  });

  test('una reserva cancelada se actualiza y deja de tener ticket utilizable', () async {
    await store.upsertAll([booking(id: 1)]);

    await store.upsertAll([booking(id: 1, status: 'Cancelled', cancelledAt: DateTime.utc(2026, 10, 3, 9))]);

    final saved = (await store.byId(1))!;
    expect(saved.isCancelled, isTrue);
    expect(saved.cancelledAt, DateTime.utc(2026, 10, 3, 9));
    expect(saved.hasTicket, isFalse); // aunque el payload siga guardado, una cancelada no muestra QR
  });

  test('lista de la más reciente a la más antigua', () async {
    await store.upsertAll([
      booking(id: 1, createdAt: DateTime.utc(2026, 10, 1)),
      booking(id: 2, createdAt: DateTime.utc(2026, 10, 3)),
      booking(id: 3, createdAt: DateTime.utc(2026, 10, 2)),
    ]);

    expect((await store.all()).map((b) => b.id), [2, 3, 1]);
  });

  test('el servidor manda: lo que ya no devuelve se quita de la caché', () async {
    await store.upsertAll([booking(id: 1), booking(id: 2), booking(id: 3)]);

    await store.removeMissing({1, 3});

    expect((await store.all()).map((b) => b.id), unorderedEquals([1, 3]));
  });

  test('clear() vacía la caché', () async {
    await store.upsertAll([booking(id: 1), booking(id: 2)]);

    await store.clear();

    expect(await store.all(), isEmpty);
  });

  test('cada cuenta tiene su propia base: los datos de una no aparecen en la otra', () async {
    // Dos bases a propósito: es justamente lo que se prueba. Cada una usa su propio ejecutor en memoria.
    driftRuntimeOptions.dontWarnAboutMultipleDatabases = true;
    final otherDb = AppDatabase(NativeDatabase.memory());
    addTearDown(otherDb.close);
    final otherStore = BookingsLocalStore(otherDb);

    await store.upsertAll([booking(id: 1, code: 'DE-ANA')]);
    await otherStore.upsertAll([booking(id: 1, code: 'DE-LUIS')]);

    expect((await store.byId(1))?.code, 'DE-ANA');
    expect((await otherStore.byId(1))?.code, 'DE-LUIS');
  });
}
