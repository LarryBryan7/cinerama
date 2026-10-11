import 'dart:convert';

import 'package:equatable/equatable.dart';

import '../../../core/db/app_database.dart';

String _money(num value) => value.toStringAsFixed(2);

/// Una reserva, ya sea recién confirmada, descargada del servidor o leída de la caché local.
class Booking extends Equatable {
  const Booking({
    required this.id,
    required this.code,
    required this.status,
    required this.showtimeId,
    required this.movieTitle,
    required this.roomName,
    required this.startsAt,
    required this.seatLabels,
    required this.totalAmount,
    required this.createdAt,
    this.ticketPayload,
    this.cancelledAt,
    this.syncedAt,
  });

  final int id;
  final String code;
  final String status; // Confirmed | Cancelled
  final int? showtimeId;
  final String movieTitle;
  final String roomName;
  final DateTime startsAt;
  final List<String> seatLabels;
  final String totalAmount;
  final DateTime createdAt;
  final String? ticketPayload;
  final DateTime? cancelledAt;
  final DateTime? syncedAt;

  bool get isCancelled => status == 'Cancelled';
  bool get hasTicket => ticketPayload != null && !isCancelled;

  /// `GET /bookings/{id}` y `POST /bookings`: trae el ticket firmado.
  factory Booking.fromDetailJson(Map<String, dynamic> json, {required DateTime syncedAt}) {
    final showtime = json['showtime'] as Map<String, dynamic>;
    final seats = [for (final s in json['seats'] as List<dynamic>) (s as Map<String, dynamic>)['label'] as String];
    return Booking(
      id: json['id'] as int,
      code: json['code'] as String,
      status: json['status'] as String,
      showtimeId: showtime['id'] as int,
      movieTitle: showtime['movieTitle'] as String,
      roomName: showtime['roomName'] as String,
      startsAt: DateTime.parse(showtime['startsAt'] as String).toUtc(),
      seatLabels: seats,
      totalAmount: _money(json['totalAmount'] as num),
      ticketPayload: json['ticketPayload'] as String?,
      createdAt: DateTime.parse(json['createdAt'] as String).toUtc(),
      cancelledAt: json['cancelledAt'] == null ? null : DateTime.parse(json['cancelledAt'] as String).toUtc(),
      syncedAt: syncedAt,
    );
  }

  /// `GET /bookings`: el listado NO trae el ticket (se descarga el detalle aparte).
  factory Booking.fromListJson(Map<String, dynamic> json, {required DateTime syncedAt}) => Booking(
        id: json['id'] as int,
        code: json['code'] as String,
        status: json['status'] as String,
        showtimeId: null,
        movieTitle: json['movieTitle'] as String,
        roomName: json['roomName'] as String,
        startsAt: DateTime.parse(json['startsAt'] as String).toUtc(),
        seatLabels: [for (final l in json['seatLabels'] as List<dynamic>) l as String],
        totalAmount: _money(json['totalAmount'] as num),
        createdAt: DateTime.parse(json['createdAt'] as String).toUtc(),
        syncedAt: syncedAt,
      );

  factory Booking.fromRow(CachedBooking row) => Booking(
        id: row.id,
        code: row.code,
        status: row.status,
        showtimeId: row.showtimeId,
        movieTitle: row.movieTitle,
        roomName: row.roomName,
        startsAt: row.startsAt.toUtc(),
        seatLabels: [for (final l in jsonDecode(row.seatLabels) as List<dynamic>) l as String],
        totalAmount: row.totalAmount,
        ticketPayload: row.ticketPayload,
        createdAt: row.createdAt.toUtc(),
        cancelledAt: row.cancelledAt?.toUtc(),
        syncedAt: row.syncedAt.toUtc(),
      );

  Booking copyWith({String? ticketPayload}) => Booking(
        id: id,
        code: code,
        status: status,
        showtimeId: showtimeId,
        movieTitle: movieTitle,
        roomName: roomName,
        startsAt: startsAt,
        seatLabels: seatLabels,
        totalAmount: totalAmount,
        createdAt: createdAt,
        ticketPayload: ticketPayload ?? this.ticketPayload,
        cancelledAt: cancelledAt,
        syncedAt: syncedAt,
      );

  @override
  List<Object?> get props =>
      [id, code, status, showtimeId, movieTitle, roomName, startsAt, seatLabels, totalAmount, createdAt, ticketPayload, cancelledAt, syncedAt];
}
