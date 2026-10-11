import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/api/api_exception.dart';
import '../data/catalog_repository.dart';
import '../data/movie_models.dart';
import 'movies_cubit.dart';

class TrailerCommentsState extends Equatable {
  const TrailerCommentsState({this.status = LoadStatus.initial, this.comments = const TrailerComments(status: 'unavailable', items: [])});

  final LoadStatus status;
  final TrailerComments comments;

  /// Hay comentarios de YouTube que mostrar.
  bool get hasComments => status == LoadStatus.ready && comments.hasComments;

  /// Todavía se está preguntando: la pantalla no debe decir "no hay comentarios" antes de saberlo.
  bool get isLoading => status == LoadStatus.initial || status == LoadStatus.loading;

  @override
  List<Object?> get props => [status, comments];
}

/// Comentarios del tráiler en YouTube de una película (hasta 20; si el video tiene menos, los que haya). Es un extra de la
/// pantalla de comentarios: si falla, no hay tráiler, los comentarios están cerrados o el servidor no tiene clave de YouTube, la
/// sección simplemente no aparece (nunca bloquea ni muestra un error).
class TrailerCommentsCubit extends Cubit<TrailerCommentsState> {
  TrailerCommentsCubit(this._repository, this.movieId) : super(const TrailerCommentsState());

  final CatalogRepository _repository;
  final int movieId;
  static const maxComments = 20;

  Future<void> load() async {
    emit(TrailerCommentsState(status: LoadStatus.loading, comments: state.comments));
    try {
      emit(TrailerCommentsState(status: LoadStatus.ready, comments: await _repository.trailerComments(movieId, limit: maxComments)));
    } on ApiException {
      emit(const TrailerCommentsState(status: LoadStatus.failure));
    } on SessionExpiredException {
      emit(const TrailerCommentsState(status: LoadStatus.failure));
    }
  }
}
