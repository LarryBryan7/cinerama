import 'package:dio/dio.dart';

import '../../../core/api/api_client.dart';
import '../../../core/api/api_exception.dart';
import '../../../core/connectivity/connectivity_service.dart';
import 'booking_models.dart';
import 'bookings_local_store.dart';

/// Reservas. Leer funciona con y sin conexión (caché local); confirmar y cancelar EXIGEN conexión real y nunca se encolan.
class BookingsRepository {
  BookingsRepository({
    required this.dio,
    required this.local,
    required this.connectivity,
    DateTime Function()? now,
  }) : _now = now ?? _utcNow;

  static DateTime _utcNow() => DateTime.now().toUtc();

  final Dio dio;
  final BookingsLocalStore local;
  final ConnectivityService connectivity;
  final DateTime Function() _now;

  /// Confirma los asientos bloqueados. [idempotencyKey] es la misma en todos los reintentos de un mismo intento de
  /// compra: si la respuesta se pierde, repetir devuelve la reserva original y no crea otra (RN-27).
  Future<Booking> confirm({required int showtimeId, required List<int> seatIds, required String idempotencyKey}) async {
    await connectivity.requireOnline();
    final response = await guardApi(() => dio.post<Map<String, dynamic>>(
          'bookings',
          data: {'showtimeId': showtimeId, 'seatIds': seatIds},
          options: Options(headers: {'Idempotency-Key': idempotencyKey}),
        ));
    final booking = Booking.fromDetailJson(response.data!, syncedAt: _now());
    await local.upsertAll([booking]); // el ticket queda en el dispositivo desde el primer momento
    return booking;
  }

  Future<Booking> cancel(int bookingId) async {
    await connectivity.requireOnline();
    final response = await guardApi(() => dio.post<Map<String, dynamic>>('bookings/$bookingId/cancel'));
    final booking = Booking.fromDetailJson(response.data!, syncedAt: _now());
    await local.upsertAll([booking]);
    return booking;
  }

  /// Lo guardado en el dispositivo (sirve sin conexión).
  Future<List<Booking>> cached() => local.all();

  Future<Booking?> cachedById(int id) => local.byId(id);

  /// Sincroniza con el servidor: reemplaza el listado local por el del servidor y descarga el ticket de las reservas
  /// confirmadas que todavía no lo tienen en el dispositivo. Lanza [ApiException] de red si no hay conexión; la caché queda intacta.
  Future<List<Booking>> refresh() async {
    final now = _now();
    final response = await guardApi(() => dio.get<Map<String, dynamic>>('bookings', queryParameters: {'pageSize': 100}));
    final items = [
      for (final item in response.data!['items'] as List<dynamic>) Booking.fromListJson(item as Map<String, dynamic>, syncedAt: now),
    ];

    await local.upsertAll(items);
    await local.removeMissing({for (final b in items) b.id});

    for (final booking in items.where((b) => !b.isCancelled)) {
      final stored = await local.byId(booking.id);
      if (stored?.ticketPayload == null) await _downloadDetail(booking.id);
    }
    return local.all();
  }

  /// Descarga el detalle (con el ticket firmado) de una reserva y lo guarda.
  Future<Booking> fetchDetail(int bookingId) => _downloadDetail(bookingId);

  Future<Booking> _downloadDetail(int bookingId) async {
    final response = await guardApi(() => dio.get<Map<String, dynamic>>('bookings/$bookingId'));
    final booking = Booking.fromDetailJson(response.data!, syncedAt: _now());
    await local.upsertAll([booking]);
    return booking;
  }
}
