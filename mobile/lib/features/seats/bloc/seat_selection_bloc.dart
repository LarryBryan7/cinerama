import 'dart:async';

import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/api/api_exception.dart';
import '../../../core/realtime/seat_realtime.dart';
import '../data/seat_models.dart';
import '../data/seats_repository.dart';

enum SeatMapStatus { loading, ready, failure }

enum NoticeKind { info, error }

/// Mensaje pasajero para la persona (se muestra como aviso y se anuncia a los lectores de pantalla). El [id] hace que
/// el mismo texto dos veces seguidas siga disparando el aviso.
class SeatNotice extends Equatable {
  const SeatNotice(this.id, this.kind, this.message);

  final int id;
  final NoticeKind kind;
  final String message;

  @override
  List<Object?> get props => [id, kind, message];
}

class SeatSelectionState extends Equatable {
  const SeatSelectionState({
    this.status = SeatMapStatus.loading,
    this.showtimeId = 0,
    this.showtimeStatus = '',
    this.price = 0,
    this.startsAt,
    this.seats = const [],
    this.busy = const {},
    this.realtime = RealtimeState.idle,
    this.lockedUntil,
    this.remaining,
    this.notice,
    this.errorCode,
  });

  final SeatMapStatus status;
  final int showtimeId;
  final String showtimeStatus;
  final double price;
  final DateTime? startsAt;
  final List<Seat> seats;

  /// Asientos con una operación en curso (se ignoran más toques sobre ellos).
  final Set<int> busy;
  final RealtimeState realtime;

  /// El vencimiento más próximo de mis bloqueos, en hora del SERVIDOR.
  final DateTime? lockedUntil;
  final Duration? remaining;
  final SeatNotice? notice;
  final String? errorCode;

  List<Seat> get mine => [for (final s in seats) if (s.isMine) s];
  double get total => price * mine.length;
  bool get canContinue => mine.isNotEmpty && (remaining == null || remaining! > Duration.zero);

  SeatSelectionState copyWith({
    SeatMapStatus? status,
    int? showtimeId,
    String? showtimeStatus,
    double? price,
    DateTime? startsAt,
    List<Seat>? seats,
    Set<int>? busy,
    RealtimeState? realtime,
    DateTime? lockedUntil,
    Duration? remaining,
    bool clearLock = false,
    SeatNotice? notice,
    String? errorCode,
  }) =>
      SeatSelectionState(
        status: status ?? this.status,
        showtimeId: showtimeId ?? this.showtimeId,
        showtimeStatus: showtimeStatus ?? this.showtimeStatus,
        price: price ?? this.price,
        startsAt: startsAt ?? this.startsAt,
        seats: seats ?? this.seats,
        busy: busy ?? this.busy,
        realtime: realtime ?? this.realtime,
        lockedUntil: clearLock ? null : (lockedUntil ?? this.lockedUntil),
        remaining: clearLock ? null : (remaining ?? this.remaining),
        notice: notice ?? this.notice,
        errorCode: errorCode ?? this.errorCode,
      );

  @override
  List<Object?> get props => [status, showtimeId, showtimeStatus, price, startsAt, seats, busy, realtime, lockedUntil, remaining, notice, errorCode];
}

sealed class SeatSelectionEvent {
  const SeatSelectionEvent();
}

class SeatMapStarted extends SeatSelectionEvent {
  const SeatMapStarted(this.showtimeId);
  final int showtimeId;
}

/// Pide el mapa completo. `silent` = sin pantalla de carga (resincronización en segundo plano).
class SeatMapRefreshRequested extends SeatSelectionEvent {
  const SeatMapRefreshRequested({this.silent = true});
  final bool silent;
}

class SeatTapped extends SeatSelectionEvent {
  const SeatTapped(this.seatId);
  final int seatId;
}

/// La persona eligió una de las sugerencias: se bloquean todos sus asientos de una vez.
class SeatsChosen extends SeatSelectionEvent {
  const SeatsChosen(this.seatIds);
  final List<int> seatIds;
}

class RemoteChangesReceived extends SeatSelectionEvent {
  const RemoteChangesReceived(this.change);
  final SeatStatusChanged change;
}

class RealtimeStateChanged extends SeatSelectionEvent {
  const RealtimeStateChanged(this.state);
  final RealtimeState state;
}

class Ticked extends SeatSelectionEvent {
  const Ticked();
}

/// Mapa de asientos de una función.
///
/// REST es la fuente de verdad; el tiempo real solo acelera la pantalla:
/// - al abrir y cada vez que el tiempo real (re)conecta, se pide el mapa COMPLETO (los avisos perdidos no se repiten);
/// - si el tiempo real no está conectado, el mapa se refresca por REST cada [pollSeconds] segundos;
/// - bloquear y liberar son llamadas REST: la app nunca marca un asiento como suyo hasta que el servidor responde 200.
class SeatSelectionBloc extends Bloc<SeatSelectionEvent, SeatSelectionState> {
  SeatSelectionBloc({
    required this.repository,
    required this.realtime,
    required this.now,
    Stream<void> Function()? tickerFactory,
    this.pollSeconds = 10,
  })  : _tickerFactory = tickerFactory ?? (() => Stream<void>.periodic(const Duration(seconds: 1))),
        super(const SeatSelectionState()) {
    on<SeatMapStarted>(_onStarted);
    on<SeatMapRefreshRequested>(_onRefresh);
    on<SeatTapped>(_onTapped);
    on<SeatsChosen>((event, emit) => _lock(event.seatIds, emit));
    on<RemoteChangesReceived>(_onRemote);
    on<RealtimeStateChanged>(_onRealtimeState);
    on<Ticked>(_onTicked);
  }

  final SeatsRepository repository;
  final SeatRealtime realtime;

  /// Hora del servidor (corrige el reloj del teléfono).
  final DateTime Function() now;
  final Stream<void> Function() _tickerFactory;
  final int pollSeconds;

  StreamSubscription<SeatStatusChanged>? _events;
  StreamSubscription<RealtimeState>? _states;
  StreamSubscription<void>? _ticker;
  int _noticeSeq = 0;
  int _tickCount = 0;

  Future<List<SeatSuggestion>> suggest(int count) => repository.suggestions(state.showtimeId, count);

  // ── Arranque y resincronización ─────────────────────────────────────────

  Future<void> _onStarted(SeatMapStarted event, Emitter<SeatSelectionState> emit) async {
    emit(SeatSelectionState(showtimeId: event.showtimeId));

    _events ??= realtime.events.listen((e) => add(RemoteChangesReceived(e)));
    _states ??= realtime.states.listen((s) => add(RealtimeStateChanged(s)));
    _ticker ??= _tickerFactory().listen((_) => add(const Ticked()));

    // Se conecta en paralelo: si falla, REST sigue funcionando y la conexión se reintenta sola.
    unawaited(realtime.watch(event.showtimeId));

    await _load(emit, silent: false);
  }

  Future<void> _onRefresh(SeatMapRefreshRequested event, Emitter<SeatSelectionState> emit) => _load(emit, silent: event.silent);

  Future<void> _load(Emitter<SeatSelectionState> emit, {required bool silent}) async {
    if (state.showtimeId == 0) return;
    try {
      final map = await repository.map(state.showtimeId);
      _apply(map, emit);
    } on ApiException catch (e) {
      if (!silent) emit(state.copyWith(status: SeatMapStatus.failure, errorCode: e.code));
    }
  }

  void _apply(SeatMap map, Emitter<SeatSelectionState> emit) {
    final previousMine = {for (final s in state.mine) s.seatId};
    final seats = [...map.seats]..sort((a, b) => a.row == b.row ? a.number.compareTo(b.number) : a.row.compareTo(b.row));
    final mineNow = {for (final s in seats) if (s.isMine) s.seatId};

    final lost = state.status == SeatMapStatus.ready ? previousMine.difference(mineNow).difference(state.busy) : <int>{};
    final lockedUntil = _earliestLock(seats);

    emit(state.copyWith(
      status: SeatMapStatus.ready,
      showtimeStatus: map.showtimeStatus,
      price: map.price,
      startsAt: map.startsAt,
      seats: seats,
      lockedUntil: lockedUntil,
      clearLock: lockedUntil == null,
      remaining: lockedUntil?.difference(now()),
      notice: lost.isEmpty ? null : _notice(NoticeKind.error, 'Tu selección venció o cambió. Revisa tus asientos.'),
    ));
  }

  // ── Toques ──────────────────────────────────────────────────────────────

  Future<void> _onTapped(SeatTapped event, Emitter<SeatSelectionState> emit) async {
    final seat = state.seats.where((s) => s.seatId == event.seatId).firstOrNull;
    if (seat == null || state.busy.contains(seat.seatId)) return;

    if (seat.isMine) return _release(seat, emit);
    if (seat.isAvailable) return _lock([seat.seatId], emit);

    emit(state.copyWith(notice: _notice(NoticeKind.info, 'El asiento ${seat.label} no está disponible.')));
  }

  Future<void> _lock(List<int> seatIds, Emitter<SeatSelectionState> emit) async {
    emit(state.copyWith(busy: {...state.busy, ...seatIds}));
    try {
      final locked = await repository.lock(state.showtimeId, seatIds);
      final byId = {for (final s in locked) s.seatId: s};
      final seats = [for (final s in state.seats) byId[s.seatId] ?? s];
      _emitSeats(emit, seats, busyDone: seatIds);
    } on OfflineException {
      emit(_idle(seatIds).copyWith(notice: _notice(NoticeKind.error, 'Necesitas conexión para elegir asientos.')));
    } on ApiException catch (e) {
      await _onLockFailed(e, seatIds, emit);
    }
  }

  Future<void> _onLockFailed(ApiException e, List<int> seatIds, Emitter<SeatSelectionState> emit) async {
    switch (e.code) {
      case 'seat_unavailable':
        // Otra persona los tomó: se marcan como no disponibles y se informa cuáles.
        final taken = {for (final c in e.conflicts) c.seatId};
        final seats = [
          for (final s in state.seats)
            taken.contains(s.seatId) && !s.isMine ? s.copyWith(status: SeatStatus.locked, isMine: false, clearLock: true) : s,
        ];
        final labels = e.conflicts.map((c) => c.label).join(', ');
        emit(_idle(seatIds).copyWith(
          seats: seats,
          notice: _notice(NoticeKind.error, labels.isEmpty ? 'Alguien tomó esos asientos. Elige otros.' : 'Alguien tomó $labels. Elige otros asientos.'),
        ));
      case 'lock_limit_exceeded':
        emit(_idle(seatIds).copyWith(notice: _notice(NoticeKind.error, 'Puedes seleccionar hasta 10 asientos por función.')));
      case 'sales_closed' || 'showtime_cancelled':
        emit(_idle(seatIds).copyWith(notice: _notice(NoticeKind.error, 'La venta de esta función ya cerró.')));
      case 'rate_limited':
        emit(_idle(seatIds).copyWith(notice: _notice(NoticeKind.error, 'Demasiadas acciones seguidas. Espera un momento.')));
      case 'network_error':
        emit(_idle(seatIds).copyWith(notice: _notice(NoticeKind.error, 'Sin conexión con el servidor. No se pudo elegir el asiento.')));
      default:
        emit(_idle(seatIds).copyWith(notice: _notice(NoticeKind.error, 'No se pudo elegir el asiento. Inténtalo de nuevo.')));
        add(const SeatMapRefreshRequested());
    }
  }

  Future<void> _release(Seat seat, Emitter<SeatSelectionState> emit) async {
    emit(state.copyWith(busy: {...state.busy, seat.seatId}));
    try {
      await repository.release(state.showtimeId, [seat.seatId]);
      final seats = [for (final s in state.seats) s.seatId == seat.seatId ? s.copyWith(status: SeatStatus.available, isMine: false, clearLock: true) : s];
      _emitSeats(emit, seats, busyDone: [seat.seatId]);
    } on OfflineException {
      emit(_idle([seat.seatId]).copyWith(notice: _notice(NoticeKind.error, 'Necesitas conexión para liberar el asiento.')));
    } on ApiException catch (e) {
      emit(_idle([seat.seatId]).copyWith(
        notice: _notice(NoticeKind.error, e.isNetwork ? 'Sin conexión con el servidor. No se pudo liberar el asiento.' : 'No se pudo liberar el asiento.'),
      ));
      if (!e.isNetwork) add(const SeatMapRefreshRequested());
    }
  }

  // ── Avisos en vivo ──────────────────────────────────────────────────────

  void _onRemote(RemoteChangesReceived event, Emitter<SeatSelectionState> emit) {
    if (event.change.showtimeId != state.showtimeId || state.status != SeatMapStatus.ready) return;

    final changes = {for (final c in event.change.changes) c.seatId: c.status};
    var lostMine = false;

    final seats = [
      for (final s in state.seats)
        if (!changes.containsKey(s.seatId))
          s
        else
          switch (changes[s.seatId]) {
            'Available' => () {
                lostMine = lostMine || s.isMine; // si era mío, mi bloqueo venció o se liberó
                return s.copyWith(status: SeatStatus.available, isMine: false, clearLock: true);
              }(),
            // Mi propio bloqueo vuelve como eco del servidor: no cambia nada. Si lo bloqueó otra persona, ya no está libre.
            'Locked' => s.isMine ? s : s.copyWith(status: SeatStatus.locked, isMine: false, clearLock: true),
            'Booked' => s.copyWith(status: SeatStatus.booked, isMine: false, clearLock: true),
            _ => s,
          },
    ];

    _emitSeats(emit, seats, notice: lostMine ? _notice(NoticeKind.error, 'Uno de tus asientos dejó de estar reservado para ti.') : null);
  }

  void _onRealtimeState(RealtimeStateChanged event, Emitter<SeatSelectionState> emit) {
    emit(state.copyWith(realtime: event.state));

    // Cada vez que el tiempo real queda conectado (la primera vez o al reconectar) se pide el mapa COMPLETO: los avisos
    // emitidos mientras no estaba conectado no se repiten.
    if (event.state == RealtimeState.connected) add(const SeatMapRefreshRequested());
  }

  // ── Reloj ───────────────────────────────────────────────────────────────

  void _onTicked(Ticked event, Emitter<SeatSelectionState> emit) {
    _tickCount++;

    final until = state.lockedUntil;
    if (until != null) {
      final remaining = until.difference(now());
      if (remaining <= Duration.zero) {
        // El servidor ya trata esos bloqueos como vencidos (RN-23): la pantalla debe reflejarlo.
        final seats = [for (final s in state.seats) s.isMine ? s.copyWith(status: SeatStatus.available, isMine: false, clearLock: true) : s];
        emit(state.copyWith(seats: seats, clearLock: true, notice: _notice(NoticeKind.error, 'Tu selección venció. Elige tus asientos de nuevo.')));
      } else if (state.remaining == null || state.remaining!.inSeconds != remaining.inSeconds) {
        emit(state.copyWith(remaining: remaining));
      }
    }

    // Respaldo: sin tiempo real, REST sigue siendo la verdad y se consulta cada tanto.
    if (state.status == SeatMapStatus.ready && state.realtime != RealtimeState.connected && _tickCount % pollSeconds == 0) {
      add(const SeatMapRefreshRequested());
    }
  }

  // ── Utilidades ──────────────────────────────────────────────────────────

  void _emitSeats(Emitter<SeatSelectionState> emit, List<Seat> seats, {Iterable<int> busyDone = const [], SeatNotice? notice}) {
    final lockedUntil = _earliestLock(seats);
    emit(state.copyWith(
      seats: seats,
      busy: {...state.busy}..removeAll(busyDone),
      lockedUntil: lockedUntil,
      clearLock: lockedUntil == null,
      remaining: lockedUntil?.difference(now()),
      notice: notice,
    ));
  }

  SeatSelectionState _idle(Iterable<int> seatIds) => state.copyWith(busy: {...state.busy}..removeAll(seatIds));

  static DateTime? _earliestLock(List<Seat> seats) {
    final locks = [for (final s in seats) if (s.isMine && s.lockedUntil != null) s.lockedUntil!];
    return locks.isEmpty ? null : locks.reduce((a, b) => a.isBefore(b) ? a : b);
  }

  SeatNotice _notice(NoticeKind kind, String message) => SeatNotice(++_noticeSeq, kind, message);

  @override
  Future<void> close() async {
    await _events?.cancel();
    await _states?.cancel();
    await _ticker?.cancel();
    unawaited(realtime.stop());
    return super.close();
  }
}
