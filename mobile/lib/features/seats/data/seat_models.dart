import 'package:equatable/equatable.dart';

enum SeatStatus {
  available,
  locked,
  booked;

  static SeatStatus parse(String value) => switch (value) {
        'Available' => SeatStatus.available,
        'Locked' => SeatStatus.locked,
        'Booked' => SeatStatus.booked,
        _ => throw FormatException('Estado de asiento desconocido: $value'),
      };
}

/// Un asiento tal como lo ve ESTA persona. [isMine] significa "bloqueado, y vigente, a mi nombre" (la app lo muestra
/// como "Seleccionado"). Nunca se sabe quién tiene un asiento ajeno: el servidor no lo informa.
class Seat extends Equatable {
  const Seat({
    required this.seatId,
    required this.row,
    required this.number,
    required this.status,
    this.isMine = false,
    this.lockedUntil,
  });

  final int seatId;
  final String row;
  final int number;
  final SeatStatus status;
  final bool isMine;
  final DateTime? lockedUntil; // solo para los propios

  String get label => '$row$number';

  bool get isAvailable => status == SeatStatus.available;
  bool get isTakenByOthers => (status == SeatStatus.locked && !isMine) || status == SeatStatus.booked;

  factory Seat.fromJson(Map<String, dynamic> json) => Seat(
        seatId: json['seatId'] as int,
        row: json['row'] as String,
        number: json['number'] as int,
        status: SeatStatus.parse(json['status'] as String),
        isMine: json['isMine'] as bool,
        lockedUntil: json['lockedUntil'] == null ? null : DateTime.parse(json['lockedUntil'] as String).toUtc(),
      );

  Seat copyWith({SeatStatus? status, bool? isMine, DateTime? lockedUntil, bool clearLock = false}) => Seat(
        seatId: seatId,
        row: row,
        number: number,
        status: status ?? this.status,
        isMine: isMine ?? this.isMine,
        lockedUntil: clearLock ? null : (lockedUntil ?? this.lockedUntil),
      );

  @override
  List<Object?> get props => [seatId, row, number, status, isMine, lockedUntil];
}

class SeatMap extends Equatable {
  const SeatMap({required this.showtimeId, required this.showtimeStatus, required this.startsAt, required this.price, required this.seats});

  final int showtimeId;
  final String showtimeStatus;
  final DateTime startsAt;
  final double price;
  final List<Seat> seats;

  factory SeatMap.fromJson(Map<String, dynamic> json) => SeatMap(
        showtimeId: json['showtimeId'] as int,
        showtimeStatus: json['showtimeStatus'] as String,
        startsAt: DateTime.parse(json['startsAt'] as String).toUtc(),
        price: (json['price'] as num).toDouble(),
        seats: [for (final s in json['seats'] as List<dynamic>) Seat.fromJson(s as Map<String, dynamic>)],
      );

  @override
  List<Object?> get props => [showtimeId, showtimeStatus, startsAt, price, seats];
}

class SeatSuggestion extends Equatable {
  const SeatSuggestion({required this.score, required this.seatIds, required this.labels});

  final double score;
  final List<int> seatIds;
  final List<String> labels;

  factory SeatSuggestion.fromJson(Map<String, dynamic> json) {
    final seats = (json['seats'] as List<dynamic>).cast<Map<String, dynamic>>();
    return SeatSuggestion(
      score: (json['score'] as num).toDouble(),
      seatIds: [for (final s in seats) s['seatId'] as int],
      labels: [for (final s in seats) s['label'] as String],
    );
  }

  @override
  List<Object?> get props => [score, seatIds, labels];
}
