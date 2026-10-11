import 'package:cinerama_mobile/features/seats/data/seat_models.dart';

Seat seat(String row, int number, {SeatStatus status = SeatStatus.available, bool mine = false, DateTime? until}) =>
    Seat(seatId: row.codeUnitAt(0) * 100 + number, row: row, number: number, status: status, isMine: mine, lockedUntil: until);

int idOf(String label) => label.codeUnitAt(0) * 100 + int.parse(label.substring(1));

SeatMap mapOf(List<Seat> seats, {int showtimeId = 42, double price = 12.5, String status = 'Scheduled'}) =>
    SeatMap(showtimeId: showtimeId, showtimeStatus: status, startsAt: DateTime.utc(2026, 10, 3, 22), price: price, seats: seats);

/// Sala de una fila A1..A4 y otra B1..B4, todas disponibles.
List<Seat> smallRoom() => [for (final r in ['A', 'B']) for (var n = 1; n <= 4; n++) seat(r, n)];
