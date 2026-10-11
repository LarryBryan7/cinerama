import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/api/api_exception.dart';
import '../data/booking_models.dart';
import '../data/bookings_repository.dart';

class BookingsState extends Equatable {
  const BookingsState({this.items = const [], this.loading = true, this.fromCache = false, this.lastSync, this.errorCode});

  final List<Booking> items;
  final bool loading;

  /// Se muestra lo guardado en el dispositivo porque no se pudo sincronizar con el servidor.
  final bool fromCache;
  final DateTime? lastSync;
  final String? errorCode;

  @override
  List<Object?> get props => [items, loading, fromCache, lastSync, errorCode];
}

/// "Mis reservas": primero lo guardado en el dispositivo (instantáneo, sirve sin conexión) y luego, si hay conexión,
/// se sincroniza con el servidor, que manda siempre.
class BookingsCubit extends Cubit<BookingsState> {
  BookingsCubit(this._repository) : super(const BookingsState());

  final BookingsRepository _repository;

  DateTime? _lastSync(List<Booking> items) =>
      items.map((b) => b.syncedAt).whereType<DateTime>().fold<DateTime?>(null, (a, b) => a == null || b.isAfter(a) ? b : a);

  Future<void> load() async {
    final cached = await _repository.cached();
    emit(BookingsState(items: cached, loading: true, fromCache: true, lastSync: _lastSync(cached)));

    try {
      final fresh = await _repository.refresh();
      emit(BookingsState(items: fresh, loading: false, lastSync: _lastSync(fresh)));
    } on ApiException catch (e) {
      // Sin conexión: se queda con lo guardado y se avisa. Nada se pierde.
      emit(BookingsState(items: cached, loading: false, fromCache: true, lastSync: _lastSync(cached), errorCode: e.code));
    } on SessionExpiredException {
      emit(BookingsState(items: cached, loading: false, fromCache: true, lastSync: _lastSync(cached)));
    }
  }
}
