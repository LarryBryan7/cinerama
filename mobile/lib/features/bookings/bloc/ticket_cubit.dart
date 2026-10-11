import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/api/api_exception.dart';
import '../data/booking_models.dart';
import '../data/bookings_repository.dart';

enum TicketStatus { loading, ready, notFound }

class TicketState extends Equatable {
  const TicketState({this.status = TicketStatus.loading, this.booking, this.fromCache = false, this.cancelling = false, this.errorCode});

  final TicketStatus status;
  final Booking? booking;

  /// El ticket que se muestra es el guardado en el dispositivo (no se pudo confirmar con el servidor).
  final bool fromCache;
  final bool cancelling;
  final String? errorCode;

  @override
  List<Object?> get props => [status, booking, fromCache, cancelling, errorCode];
}

/// Ticket con QR. Se muestra de inmediato desde el dispositivo —también sin conexión— y, si hay conexión, se confirma
/// con el servidor. El QR es el contenido FIRMADO por el servidor (`{código}.{firma}`): la app lo guarda y lo dibuja,
/// pero no puede fabricar uno válido, y la validez final la decide el cine al escanearlo.
class TicketCubit extends Cubit<TicketState> {
  TicketCubit(this._repository, this.bookingId) : super(const TicketState());

  final BookingsRepository _repository;
  final int bookingId;

  Future<void> load() async {
    final cached = await _repository.cachedById(bookingId);
    if (cached != null) emit(TicketState(status: TicketStatus.ready, booking: cached)); // sin aviso: aún no sabemos si hay conexión

    try {
      final fresh = await _repository.fetchDetail(bookingId);
      emit(TicketState(status: TicketStatus.ready, booking: fresh));
    } on ApiException catch (e) {
      if (e.status == 404) {
        emit(const TicketState(status: TicketStatus.notFound));
      } else if (cached != null) {
        emit(TicketState(status: TicketStatus.ready, booking: cached, fromCache: true, errorCode: e.code));
      } else {
        emit(TicketState(status: TicketStatus.notFound, errorCode: e.code));
      }
    } on SessionExpiredException {
      // El AuthBloc se encarga de llevar al login.
    }
  }

  /// Cancela la reserva (exige conexión).
  Future<void> cancel() async {
    final current = state.booking;
    if (current == null || state.cancelling) return;
    emit(TicketState(status: TicketStatus.ready, booking: current, fromCache: state.fromCache, cancelling: true));
    try {
      emit(TicketState(status: TicketStatus.ready, booking: await _repository.cancel(bookingId)));
    } on OfflineException {
      emit(TicketState(status: TicketStatus.ready, booking: current, fromCache: true, errorCode: 'offline'));
    } on ApiException catch (e) {
      emit(TicketState(status: TicketStatus.ready, booking: current, fromCache: state.fromCache, errorCode: e.code));
    }
  }
}
