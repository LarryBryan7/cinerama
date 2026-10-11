import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/api/api_exception.dart';
import '../data/catalog_repository.dart';
import '../data/movie_models.dart';

enum LoadStatus { initial, loading, ready, failure }

class MoviesState extends Equatable {
  const MoviesState({
    this.status = LoadStatus.initial,
    this.items = const [],
    this.page = 0,
    this.hasMore = false,
    this.loadingMore = false,
    this.query = '',
    this.tab = MovieStatus.nowShowing,
    this.city,
    this.errorCode,
  });

  final LoadStatus status;
  final List<MovieSummary> items;
  final int page;
  final bool hasMore;
  final bool loadingMore;
  final String query;

  /// Pestaña elegida: cartelera, preventa o próximamente.
  final MovieStatus tab;

  /// Ciudad elegida; `null` hasta que se conoce el listado de ciudades.
  final String? city;
  final String? errorCode;

  MoviesState copyWith({LoadStatus? status, List<MovieSummary>? items, int? page, bool? hasMore, bool? loadingMore, String? query, String? errorCode, bool clearError = false}) =>
      MoviesState(
        status: status ?? this.status,
        items: items ?? this.items,
        page: page ?? this.page,
        hasMore: hasMore ?? this.hasMore,
        loadingMore: loadingMore ?? this.loadingMore,
        query: query ?? this.query,
        tab: tab,
        city: city,
        errorCode: clearError ? null : (errorCode ?? this.errorCode),
      );

  @override
  List<Object?> get props => [status, items, page, hasMore, loadingMore, query, tab, city, errorCode];
}

class MoviesCubit extends Cubit<MoviesState> {
  MoviesCubit(this._repository) : super(const MoviesState());

  final CatalogRepository _repository;
  static const _pageSize = 20;

  /// Cada carga recibe un número: si el usuario cambia de pestaña o de ciudad antes de que llegue la respuesta anterior,
  /// esa respuesta tardía se descarta (no puede pisar la lista de la pestaña actual).
  var _epoch = 0;

  Future<void> load({String? query}) => _load(query: query ?? state.query, tab: state.tab, city: state.city);

  /// Cambia de pestaña (cartelera / preventa / próximamente) y recarga.
  Future<void> setTab(MovieStatus tab) {
    if (tab == state.tab) return Future.value();
    return _load(query: state.query, tab: tab, city: state.city);
  }

  /// Cambia de ciudad y recarga.
  Future<void> setCity(String? city) {
    if (city == state.city) return Future.value();
    return _load(query: state.query, tab: state.tab, city: city);
  }

  Future<void> _load({required String query, required MovieStatus tab, required String? city}) async {
    final epoch = ++_epoch;
    final sameList = tab == state.tab && city == state.city;
    // Si cambió la pestaña o la ciudad, no se muestran películas de la lista anterior mientras carga la nueva.
    emit(MoviesState(status: LoadStatus.loading, query: query, tab: tab, city: city, items: sameList ? state.items : const []));
    try {
      final page = await _repository.movies(search: query, status: tab, city: city, pageSize: _pageSize);
      if (epoch != _epoch) return;
      emit(MoviesState(status: LoadStatus.ready, query: query, tab: tab, city: city, items: page.items, page: page.page, hasMore: page.hasMore));
    } on ApiException catch (e) {
      if (epoch != _epoch) return;
      emit(MoviesState(status: LoadStatus.failure, query: query, tab: tab, city: city, items: sameList ? state.items : const [], errorCode: e.code));
    }
  }

  Future<void> loadMore() async {
    if (!state.hasMore || state.loadingMore || state.status != LoadStatus.ready) return;
    final epoch = _epoch;
    emit(state.copyWith(loadingMore: true));
    try {
      final next = await _repository.movies(search: state.query, status: state.tab, city: state.city, page: state.page + 1, pageSize: _pageSize);
      if (epoch != _epoch) return;
      emit(state.copyWith(items: [...state.items, ...next.items], page: next.page, hasMore: next.hasMore, loadingMore: false));
    } on ApiException {
      if (epoch != _epoch) return;
      emit(state.copyWith(loadingMore: false)); // sin red: se queda lo ya cargado
    }
  }
}
