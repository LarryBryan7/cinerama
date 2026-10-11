import 'package:dio/dio.dart';

import '../../../core/api/api_client.dart';
import 'movie_models.dart';

/// Cartelera. Es una lectura pública del servidor. (La caché sin conexión de la cartelera llega en la fase 5.)
class CatalogRepository {
  CatalogRepository(this._dio);

  final Dio _dio;

  /// [status] filtra por estado de estreno (cartelera, preventa, próximamente); [city] deja las películas con funciones en esa ciudad.
  Future<MoviePage> movies({String? search, MovieStatus? status, String? city, int page = 1, int pageSize = 20}) async {
    final response = await guardApi(() => _dio.get<Map<String, dynamic>>('movies', queryParameters: {
          'page': page,
          'pageSize': pageSize,
          if (search != null && search.trim().isNotEmpty) 'search': search.trim(),
          if (status != null) 'status': status.query,
          if (city != null && city.trim().isNotEmpty) 'city': city.trim(),
        }));
    return MoviePage.fromJson(response.data!);
  }

  Future<List<City>> cities() async {
    final response = await guardApi(() => _dio.get<List<dynamic>>('cities'));
    return [for (final c in response.data!) City.fromJson(c as Map<String, dynamic>)];
  }

  Future<ReviewPage> reviews(int movieId, {int page = 1, int pageSize = 20}) async {
    final response = await guardApi(() => _dio.get<Map<String, dynamic>>('movies/$movieId/reviews', queryParameters: {'page': page, 'pageSize': pageSize}));
    return ReviewPage.fromJson(response.data!);
  }

  /// Comentarios públicos del tráiler en YouTube (hasta 20; si el video tiene menos, los que haya). Siempre responde 200: el
  /// estado dice si no hay (sin tráiler, comentarios cerrados, función apagada).
  Future<TrailerComments> trailerComments(int movieId, {int limit = 20}) async {
    final response = await guardApi(() => _dio.get<Map<String, dynamic>>('movies/$movieId/trailer-comments', queryParameters: {'limit': limit}));
    return TrailerComments.fromJson(response.data!);
  }

  /// Publica la opinión propia o reemplaza la anterior (requiere sesión).
  Future<Review> saveReview(int movieId, {required int rating, String? comment}) async {
    final response = await guardApi(() => _dio.put<Map<String, dynamic>>('movies/$movieId/reviews/mine', data: {
          'rating': rating,
          if (comment != null && comment.trim().isNotEmpty) 'comment': comment.trim(),
        }));
    return Review.fromJson(response.data!);
  }

  Future<void> deleteReview(int movieId) async {
    await guardApi(() => _dio.delete<void>('movies/$movieId/reviews/mine'));
  }

  Future<MovieDetail> movie(int id) async {
    final response = await guardApi(() => _dio.get<Map<String, dynamic>>('movies/$id'));
    return MovieDetail.fromJson(response.data!);
  }
}
