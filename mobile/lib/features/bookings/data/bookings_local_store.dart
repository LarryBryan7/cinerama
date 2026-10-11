import 'dart:convert';

import 'package:drift/drift.dart';

import '../../../core/db/app_database.dart';
import 'booking_models.dart';

/// Las reservas guardadas en el dispositivo. Es una caché de LECTURA: nada se escribe aquí que no haya venido del
/// servidor, y el servidor manda siempre que hay conexión.
class BookingsLocalStore {
  BookingsLocalStore(this._db);

  final AppDatabase _db;

  Future<List<Booking>> all() async {
    final rows = await (_db.select(_db.cachedBookings)
          ..orderBy([(t) => OrderingTerm.desc(t.createdAt), (t) => OrderingTerm.desc(t.id)]))
        .get();
    return rows.map(Booking.fromRow).toList();
  }

  Future<Booking?> byId(int id) async {
    final row = await (_db.select(_db.cachedBookings)..where((t) => t.id.equals(id))).getSingleOrNull();
    return row == null ? null : Booking.fromRow(row);
  }

  /// Guarda las reservas. El listado del servidor no trae el ticket: si ya se tenía, se conserva en lugar de borrarlo.
  Future<void> upsertAll(List<Booking> bookings) async {
    await _db.transaction(() async {
      for (final booking in bookings) {
        final existing = await byId(booking.id);
        final payload = booking.ticketPayload ?? existing?.ticketPayload;
        await _db.into(_db.cachedBookings).insertOnConflictUpdate(CachedBookingsCompanion.insert(
              id: Value(booking.id),
              code: booking.code,
              status: booking.status,
              showtimeId: Value(booking.showtimeId ?? existing?.showtimeId),
              movieTitle: booking.movieTitle,
              roomName: booking.roomName,
              startsAt: booking.startsAt,
              seatLabels: jsonEncode(booking.seatLabels),
              totalAmount: booking.totalAmount,
              ticketPayload: Value(payload),
              createdAt: booking.createdAt,
              cancelledAt: Value(booking.cancelledAt ?? (booking.isCancelled ? existing?.cancelledAt : null)),
              syncedAt: booking.syncedAt ?? DateTime.now().toUtc(),
            ));
      }
    });
  }

  /// El servidor es la fuente de verdad: lo que ya no devuelve (por ejemplo, otro dispositivo lo canceló y purgó) se quita.
  Future<void> removeMissing(Set<int> serverIds) async {
    await (_db.delete(_db.cachedBookings)..where((t) => t.id.isNotIn(serverIds))).go();
  }

  Future<void> clear() => _db.delete(_db.cachedBookings).go();
}
