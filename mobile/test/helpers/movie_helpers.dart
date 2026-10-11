import 'package:cinerama_mobile/features/movies/data/movie_models.dart';

Review review({int id = 1, String author = 'Lucía V.', int rating = 5, String? comment = 'Excelente película', DateTime? at, bool mine = false, String? avatar}) =>
    Review(id: id, authorName: author, rating: rating, comment: comment, createdAt: at ?? DateTime.utc(2026, 10, 7, 15), isMine: mine, authorAvatarUrl: avatar);

CinemaRef cinema({int id = 1, String name = 'Cinerama - Chimbote', String address = 'Av. José Pardo 123', String? logo}) =>
    CinemaRef(id: id, name: name, address: address, city: 'Chimbote', latitude: -9.0745, longitude: -78.5936, logoUrl: logo);

MovieSummary movieSummary({
  int id = 1,
  String title = 'Marea Alta',
  MovieStatus status = MovieStatus.nowShowing,
  String? synopsis = 'Dos hermanos heredan un faro.',
  double? rating = 4.5,
  int reviewCount = 20,
  Review? featured,
  List<CinemaRef>? cinemas,
  DateTime? releaseDate,
  String? posterUrl,
  String? videoUrl,
  String? trailerUrl,
}) =>
    MovieSummary(
      id: id,
      title: title,
      durationMinutes: 121,
      classification: '+14',
      genres: const ['Aventura', 'Drama'],
      synopsis: synopsis,
      status: status,
      averageRating: rating,
      reviewCount: reviewCount,
      featuredReview: featured,
      releaseDate: releaseDate ?? DateTime(2026, 10, 11),
      posterUrl: posterUrl,
      previewVideoUrl: videoUrl,
      trailerUrl: trailerUrl,
      cinemas: cinemas ?? [cinema()],
    );

MoviePage moviePage(List<MovieSummary> items, {int page = 1, int pageSize = 20, int? total}) =>
    MoviePage(items: items, page: page, pageSize: pageSize, totalCount: total ?? items.length);

/// JSON de una película tal como lo devuelve GET /api/v1/movies.
Map<String, dynamic> movieJson({
  int id = 10,
  String status = 'NowShowing',
  Object? featured,
  double? rating = 4.5,
  int reviewCount = 20,
  List<Map<String, dynamic>>? cinemas,
}) =>
    {
      'id': id,
      'title': 'Ciudad de Cristal',
      'synopsis': 'En una ciudad donde nadie miente.',
      'durationMinutes': 134,
      'classification': '+14',
      'posterUrl': 'http://localhost/media/posters/ciudad-de-cristal.jpg',
      'trailerUrl': null,
      'genres': ['Ciencia ficción', 'Drama'],
      'releaseDate': '2026-10-11',
      'preSaleStartsOn': '2026-09-27',
      'status': status,
      'averageRating': rating,
      'reviewCount': reviewCount,
      'featuredReview': featured,
      'cinemas': cinemas ?? [cinemaJson()],
    };

Map<String, dynamic> cinemaJson({int id = 1, String name = 'Cinerama - Chimbote', String? logoUrl}) => {
      'id': id,
      'name': name,
      'address': 'Av. José Pardo 123',
      'city': 'Chimbote',
      'region': 'Áncash',
      'latitude': -9.0745,
      'longitude': -78.5936,
      'logoUrl': logoUrl,
    };

Map<String, dynamic> reviewJson({int id = 1, String author = 'Lucía V.', int rating = 5, String? comment = 'Excelente película', bool mine = false}) => {
      'id': id,
      'authorName': author,
      'authorAvatarUrl': null,
      'rating': rating,
      'comment': comment,
      'createdAt': '2026-10-07T15:00:00Z',
      'isMine': mine,
    };

Map<String, dynamic> pageJson(List<Map<String, dynamic>> items, {int page = 1, int pageSize = 20, int? total}) =>
    {'items': items, 'page': page, 'pageSize': pageSize, 'totalCount': total ?? items.length};
