import 'dart:async';
import 'dart:math';

import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/api/api_exception.dart';
import '../../bookings/data/booking_models.dart';
import '../../bookings/data/bookings_repository.dart';
import '../../seats/data/seat_models.dart';
import '../../seats/data/seats_repository.dart';

enum CheckoutStatus { loading, ready, confirming, confirmed, seatsLost, failure }

class CheckoutState extends Equatable {
  const CheckoutState({
    this.status = CheckoutStatus.loading,
    this.seats = const [],
    this.price = 0,
    this.lockedUntil,
    this.booking,
    this.errorCode,
    this.message,
  });

  final CheckoutStatus status;
  final List<Seat> seats;
  final double price;
  final DateTime? lockedUntil;
  final Booking? booking;
  final String? errorCode;
  final String? message;

  double get total => price * seats.length;

  @override
  List<Object?> get props => [status, seats, price, lockedUntil, booking, errorCode, message];
}

/// Confirma la reserva (pago simulado). Exige conexión real; nada se encola.
///
/// Todos los reintentos de UN intento de compra usan la MISMA clave de idempotencia: si la respuesta se pierde (la
/// red cae justo después de que el servidor creó la reserva), repetir devuelve la reserva original y no crea otra (RN-27).
class CheckoutCubit extends Cubit<CheckoutState> {
  CheckoutCubit({
    required this.showtimeId,
    required this.seats,
    required this.bookings,
    this.retryDelays = const [Duration(seconds: 1), Duration(seconds: 2)],
    String Function()? keyGenerator,
  })  : _key = (keyGenerator ?? generateIdempotencyKey)(),
        super(const CheckoutState());

  final int showtimeId;
  final SeatsRepository seats;
  final BookingsRepository bookings;
  final List<Duration> retryDelays;
  final String _key;

  String get idempotencyKey => _key;

  /// Lee del servidor qué asientos tengo bloqueados (la fuente de verdad), no de lo que la pantalla anterior recordaba.
  Future<void> load() async {
    emit(const CheckoutState());
    try {
      final map = await seats.map(showtimeId);
      final mine = map.seats.where((s) => s.isMine).toList();
      if (mine.isEmpty) {
        emit(const CheckoutState(status: CheckoutStatus.seatsLost, errorCode: 'lock_expired'));
        return;
      }
      final until = mine.map((s) => s.lockedUntil).whereType<DateTime>().fold<DateTime?>(null, (a, b) => a == null || b.isBefore(a) ? b : a);
      emit(CheckoutState(status: CheckoutStatus.ready, seats: mine, price: map.price, lockedUntil: until));
    } on ApiException catch (e) {
      emit(CheckoutState(status: CheckoutStatus.failure, errorCode: e.code));
    }
  }

  Future<void> confirm() async {
    if (state.status != CheckoutStatus.ready && state.status != CheckoutStatus.failure) return;
    final current = state;
    emit(CheckoutState(status: CheckoutStatus.confirming, seats: current.seats, price: current.price, lockedUntil: current.lockedUntil));

    for (var attempt = 0;; attempt++) {
      try {
        final booking = await bookings.confirm(
          showtimeId: showtimeId,
          seatIds: [for (final s in current.seats) s.seatId],
          idempotencyKey: _key,
        );
        emit(CheckoutState(status: CheckoutStatus.confirmed, seats: current.seats, price: current.price, booking: booking));
        return;
      } on OfflineException {
        emit(_failed(current, 'offline'));
        return;
      } on ApiException catch (e) {
        // Solo se reintenta si la solicitud nunca llegó o el servidor falló, y con la misma clave. Jamás ante un 4xx.
        if (e.isRetryable && attempt < retryDelays.length) {
          await Future<void>.delayed(retryDelays[attempt]);
          continue;
        }
        if (e.code == 'lock_expired' || e.code == 'seat_unavailable') {
          emit(CheckoutState(status: CheckoutStatus.seatsLost, errorCode: e.code));
        } else {
          emit(_failed(current, e.code));
        }
        return;
      }
    }
  }

  CheckoutState _failed(CheckoutState from, String code) =>
      CheckoutState(status: CheckoutStatus.failure, seats: from.seats, price: from.price, lockedUntil: from.lockedUntil, errorCode: code);
}

/// Clave de idempotencia: 128 bits aleatorios con formato UUID v4.
String generateIdempotencyKey([Random? random]) {
  final r = random ?? Random.secure();
  final bytes = List<int>.generate(16, (_) => r.nextInt(256));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  final hex = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
  return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20)}';
}
