import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/api/api_exception.dart';
import '../data/catalog_repository.dart';
import '../data/movie_models.dart';
import 'movies_cubit.dart';

class MovieDetailState extends Equatable {
  const MovieDetailState({this.status = LoadStatus.initial, this.movie, this.errorCode});

  final LoadStatus status;
  final MovieDetail? movie;
  final String? errorCode;

  @override
  List<Object?> get props => [status, movie, errorCode];
}

class MovieDetailCubit extends Cubit<MovieDetailState> {
  MovieDetailCubit(this._repository, this.movieId) : super(const MovieDetailState());

  final CatalogRepository _repository;
  final int movieId;

  Future<void> load() async {
    emit(const MovieDetailState(status: LoadStatus.loading));
    try {
      emit(MovieDetailState(status: LoadStatus.ready, movie: await _repository.movie(movieId)));
    } on ApiException catch (e) {
      emit(MovieDetailState(status: LoadStatus.failure, errorCode: e.code));
    }
  }
}
