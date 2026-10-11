import 'package:cinerama_mobile/features/bookings/data/booking_models.dart';

Booking booking({
  int id = 1,
  String code = 'K7QH3MXP',
  String status = 'Confirmed',
  String? ticket = 'K7QH3MXP.firma',
  List<String> seats = const ['F7', 'F8'],
  DateTime? startsAt,
  DateTime? syncedAt,
  DateTime? createdAt,
  DateTime? cancelledAt,
  String movie = 'Noche en el Puerto',
}) =>
    Booking(
      id: id,
      code: code,
      status: status,
      showtimeId: 42,
      movieTitle: movie,
      roomName: 'Sala 2',
      startsAt: startsAt ?? DateTime.utc(2026, 10, 10, 22),
      seatLabels: seats,
      totalAmount: '25.00',
      createdAt: createdAt ?? DateTime.utc(2026, 10, 2, 12, id),
      ticketPayload: ticket,
      cancelledAt: cancelledAt,
      syncedAt: syncedAt ?? DateTime.utc(2026, 10, 2, 12),
    );

Map<String, dynamic> bookingDetailJson({int id = 1, String code = 'K7QH3MXP', String status = 'Confirmed', bool ticket = true}) => {
      'id': id,
      'code': code,
      'status': status,
      'showtime': {'id': 42, 'movieId': 3, 'movieTitle': 'Noche en el Puerto', 'roomName': 'Sala 2', 'startsAt': '2026-10-10T22:00:00Z'},
      'seats': [
        {'seatId': 7, 'label': 'F7', 'price': 12.5},
        {'seatId': 8, 'label': 'F8', 'price': 12.5},
      ],
      'totalAmount': 25.0,
      'ticketPayload': ticket ? '$code.firma' : null,
      'createdAt': '2026-10-02T12:00:00Z',
      'cancelledAt': status == 'Cancelled' ? '2026-10-03T09:00:00Z' : null,
    };

Map<String, dynamic> bookingListItemJson({int id = 1, String code = 'K7QH3MXP', String status = 'Confirmed'}) => {
      'id': id,
      'code': code,
      'status': status,
      'movieTitle': 'Noche en el Puerto',
      'roomName': 'Sala 2',
      'startsAt': '2026-10-10T22:00:00Z',
      'seatLabels': ['F7', 'F8'],
      'totalAmount': 25.0,
      'createdAt': '2026-10-02T12:0$id:00Z',
    };
