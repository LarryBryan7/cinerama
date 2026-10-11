import 'package:cinerama_mobile/core/realtime/seat_realtime.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('interpreta el aviso del servidor: función y lista de cambios, sin datos de ninguna persona', () {
    final event = SeatStatusChanged.fromJson({
      'showtimeId': 7,
      'changes': [
        {'seatId': 11, 'status': 'Locked'},
        {'seatId': 12, 'status': 'Available'},
      ],
    });

    expect(event, const SeatStatusChanged(7, [SeatChange(11, 'Locked'), SeatChange(12, 'Available')]));
  });

  test('ignora campos que no conoce (el servidor puede agregar datos sin romper versiones viejas de la app)', () {
    final event = SeatStatusChanged.fromJson({
      'showtimeId': 1,
      'changes': [
        {'seatId': 3, 'status': 'Booked', 'campoNuevo': true},
      ],
      'otro': 1,
    });

    expect(event.changes.single, const SeatChange(3, 'Booked'));
  });
}
