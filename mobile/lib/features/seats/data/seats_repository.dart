import 'package:dio/dio.dart';

import '../../../core/api/api_client.dart';
import '../../../core/connectivity/connectivity_service.dart';
import 'seat_models.dart';

/// Mapa, bloqueo y liberación de asientos. TODAS las escrituras exigen conexión real y se resuelven en el servidor
/// (ADR-010): la app nunca bloquea "por su cuenta" ni encola un bloqueo para más tarde.
class SeatsRepository {
  SeatsRepository({required this.dio, required this.connectivity});

  final Dio dio;
  final ConnectivityService connectivity;

  /// El mapa completo: la fuente de verdad que se pide al abrir, al reconectar y como respaldo si el tiempo real se cae.
  Future<SeatMap> map(int showtimeId) async {
    final response = await guardApi(() => dio.get<Map<String, dynamic>>('showtimes/$showtimeId/seats'));
    return SeatMap.fromJson(response.data!);
  }

  /// Bloquea todo o nada. Devuelve los asientos tal como quedaron (con `isMine` y su vencimiento).
  Future<List<Seat>> lock(int showtimeId, List<int> seatIds) async {
    await connectivity.requireOnline();
    final response = await guardApi(() => dio.post<Map<String, dynamic>>('showtimes/$showtimeId/seats/lock', data: {'seatIds': seatIds}));
    return [for (final s in response.data!['seats'] as List<dynamic>) Seat.fromJson(s as Map<String, dynamic>)];
  }

  Future<void> release(int showtimeId, List<int> seatIds) async {
    await connectivity.requireOnline();
    await guardApi(() => dio.post<void>('showtimes/$showtimeId/seats/release', data: {'seatIds': seatIds}));
  }

  Future<List<SeatSuggestion>> suggestions(int showtimeId, int count) async {
    final response = await guardApi(() => dio.get<List<dynamic>>('showtimes/$showtimeId/seats/recommendations', queryParameters: {'count': count}));
    return [for (final s in response.data!) SeatSuggestion.fromJson(s as Map<String, dynamic>)];
  }
}
