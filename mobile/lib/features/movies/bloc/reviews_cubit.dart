import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/api/api_exception.dart';
import '../data/catalog_repository.dart';
import '../data/movie_models.dart';
import 'movies_cubit.dart';

class ReviewsState extends Equatable {
  const ReviewsState({
    this.status = LoadStatus.initial,
    this.items = const [],
    this.page = 0,
    this.hasMore = false,
    this.totalCount = 0,
    this.loadingMore = false,
    this.submitting = false,
    this.errorCode,
    this.saveErrorCode,
  });

  final LoadStatus status;
  final List<Review> items;
  final int page;
  final bool hasMore;
  final int totalCount;
  final bool loadingMore;

  /// Se está guardando o borrando la opinión propia.
  final bool submitting;

  /// Error al cargar la lista.
  final String? errorCode;

  /// Error al guardar o borrar (se muestra en el formulario, sin perder lo escrito).
  final String? saveErrorCode;

  /// La opinión propia, si ya la publicó (el servidor marca `isMine`).
  Review? get mine => items.where((r) => r.isMine).firstOrNull;

  ReviewsState copyWith({
    LoadStatus? status,
    List<Review>? items,
    int? page,
    bool? hasMore,
    int? totalCount,
    bool? loadingMore,
    bool? submitting,
    String? errorCode,
    String? saveErrorCode,
    bool clearSaveError = false,
  }) =>
      ReviewsState(
        status: status ?? this.status,
        items: items ?? this.items,
        page: page ?? this.page,
        hasMore: hasMore ?? this.hasMore,
        totalCount: totalCount ?? this.totalCount,
        loadingMore: loadingMore ?? this.loadingMore,
        submitting: submitting ?? this.submitting,
        errorCode: errorCode ?? this.errorCode,
        saveErrorCode: clearSaveError ? null : (saveErrorCode ?? this.saveErrorCode),
      );

  @override
  List<Object?> get props => [status, items, page, hasMore, totalCount, loadingMore, submitting, errorCode, saveErrorCode];
}

/// Opiniones de una película. Leerlas es público; publicar, cambiar o borrar la propia exige sesión (RN-43).
class ReviewsCubit extends Cubit<ReviewsState> {
  ReviewsCubit(this._repository, this.movieId) : super(const ReviewsState());

  final CatalogRepository _repository;
  final int movieId;
  static const _pageSize = 20;

  Future<void> load() async {
    emit(ReviewsState(status: LoadStatus.loading, items: state.items, totalCount: state.totalCount));
    try {
      final page = await _repository.reviews(movieId, pageSize: _pageSize);
      emit(ReviewsState(status: LoadStatus.ready, items: page.items, page: page.page, hasMore: page.hasMore, totalCount: page.totalCount));
    } on ApiException catch (e) {
      emit(ReviewsState(status: LoadStatus.failure, items: state.items, totalCount: state.totalCount, errorCode: e.code));
    } on SessionExpiredException {
      emit(ReviewsState(status: LoadStatus.failure, items: state.items, totalCount: state.totalCount, errorCode: 'session_expired'));
    }
  }

  Future<void> loadMore() async {
    if (!state.hasMore || state.loadingMore || state.status != LoadStatus.ready) return;
    emit(state.copyWith(loadingMore: true));
    try {
      final next = await _repository.reviews(movieId, page: state.page + 1, pageSize: _pageSize);
      emit(state.copyWith(items: [...state.items, ...next.items], page: next.page, hasMore: next.hasMore, totalCount: next.totalCount, loadingMore: false));
    } on ApiException {
      emit(state.copyWith(loadingMore: false));
    }
  }

  /// Publica la opinión propia o reemplaza la anterior. Devuelve `true` si se guardó; si no, el motivo queda en [ReviewsState.saveErrorCode].
  Future<bool> save({required int rating, String? comment}) => _submit(() => _repository.saveReview(movieId, rating: rating, comment: comment));

  Future<bool> deleteMine() => _submit(() => _repository.deleteReview(movieId));

  Future<bool> _submit(Future<void> Function() action) async {
    if (state.submitting) return false;
    emit(state.copyWith(submitting: true, clearSaveError: true));
    try {
      await action();
    } on ApiException catch (e) {
      emit(state.copyWith(submitting: false, saveErrorCode: e.code));
      return false;
    } on SessionExpiredException {
      emit(state.copyWith(submitting: false)); // el AuthBloc lleva al login; lo escrito se pierde, pero no hay nada que reintentar sin sesión
      return false;
    }
    await load(); // el servidor es la referencia: se vuelve a pedir la lista (con la opinión propia marcada)
    emit(state.copyWith(submitting: false));
    return true;
  }
}
