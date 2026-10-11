import 'package:equatable/equatable.dart';

enum RealtimeState {
  /// Todavía no se intentó conectar, o se desconectó a propósito.
  idle,
  connecting,
  connected,

  /// Se perdió la conexión y se está reintentando.
  reconnecting,
}

class SeatChange extends Equatable {
  const SeatChange(this.seatId, this.status);

  final int seatId;

  /// `Available`, `Locked` o `Booked`.
  final String status;

  @override
  List<Object?> get props => [seatId, status];
}

/// Aviso `SeatStatusChanged`. Solo trae función, asiento y estado: nunca quién tiene el asiento (ADR-016).
class SeatStatusChanged extends Equatable {
  const SeatStatusChanged(this.showtimeId, this.changes);

  final int showtimeId;
  final List<SeatChange> changes;

  factory SeatStatusChanged.fromJson(Map<String, dynamic> json) => SeatStatusChanged(
        json['showtimeId'] as int,
        [
          for (final c in json['changes'] as List<dynamic>)
            SeatChange((c as Map<String, dynamic>)['seatId'] as int, c['status'] as String),
        ],
      );

  @override
  List<Object?> get props => [showtimeId, changes];
}

/// Tiempo real de los asientos. Solo AVISA: bloquear, liberar y reservar pasan siempre por REST. Si la conexión se
/// cae, la app sigue funcionando con REST y, al volver, pide el mapa completo (los avisos perdidos no se repiten).
abstract class SeatRealtime {
  Stream<SeatStatusChanged> get events;
  Stream<RealtimeState> get states;
  RealtimeState get state;

  /// Conecta (si hace falta) y se suscribe a la función. Si la conexión se pierde, reintenta y se vuelve a suscribir sola;
  /// cada vez que queda conectada se emite [RealtimeState.connected] para que quien escucha pida el mapa completo.
  Future<void> watch(int showtimeId);

  Future<void> stop();

  Future<void> dispose();
}
