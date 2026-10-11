import 'package:equatable/equatable.dart';

import '../../../core/youtube/youtube_id.dart';

/// Estado de una película frente al público (RN-41). Lo calcula el servidor a partir de las fechas.
enum MovieStatus {
  nowShowing('NowShowing', 'nowShowing'),
  preSale('PreSale', 'presale'),
  comingSoon('ComingSoon', 'comingSoon');

  const MovieStatus(this.apiValue, this.query);

  /// Valor que devuelve el servidor en `status`.
  final String apiValue;

  /// Valor del parámetro `status` al pedir la lista.
  final String query;

  static MovieStatus fromApi(String value) =>
      MovieStatus.values.firstWhere((s) => s.apiValue == value, orElse: () => MovieStatus.nowShowing);
}

/// Un cine donde la película tiene funciones próximas. La distancia no viene del servidor: se calcula en el dispositivo.
class CinemaRef extends Equatable {
  const CinemaRef({required this.id, required this.name, required this.address, required this.city, required this.latitude, required this.longitude, this.logoUrl});

  final int id;
  final String name;
  final String address;
  final String city;
  final double latitude;
  final double longitude;
  final String? logoUrl;

  /// Primera letra del nombre, para el recuadro cuando el cine no tiene logo.
  String get initial => name.trim().isEmpty ? '?' : name.trim()[0].toUpperCase();

  factory CinemaRef.fromJson(Map<String, dynamic> json) => CinemaRef(
        id: json['id'] as int,
        name: json['name'] as String,
        address: json['address'] as String,
        city: json['city'] as String,
        latitude: (json['latitude'] as num).toDouble(),
        longitude: (json['longitude'] as num).toDouble(),
        logoUrl: json['logoUrl'] as String?,
      );

  @override
  List<Object?> get props => [id, name, address, city, latitude, longitude, logoUrl];
}

/// Opinión de una persona. El nombre llega abreviado ("David F."): la app nunca conoce el nombre completo de otra persona.
class Review extends Equatable {
  const Review({required this.id, required this.authorName, required this.rating, required this.createdAt, required this.isMine, this.comment, this.authorAvatarUrl});

  final int id;
  final String authorName;
  final String? authorAvatarUrl;
  final int rating;
  final String? comment;
  final DateTime createdAt; // UTC
  final bool isMine;

  factory Review.fromJson(Map<String, dynamic> json) => Review(
        id: json['id'] as int,
        authorName: json['authorName'] as String,
        authorAvatarUrl: json['authorAvatarUrl'] as String?,
        rating: json['rating'] as int,
        comment: json['comment'] as String?,
        createdAt: DateTime.parse(json['createdAt'] as String).toUtc(),
        isMine: json['isMine'] as bool? ?? false,
      );

  @override
  List<Object?> get props => [id, authorName, authorAvatarUrl, rating, comment, createdAt, isMine];
}

class ReviewPage extends Equatable {
  const ReviewPage({required this.items, required this.page, required this.pageSize, required this.totalCount});

  final List<Review> items;
  final int page;
  final int pageSize;
  final int totalCount;

  bool get hasMore => page * pageSize < totalCount;

  factory ReviewPage.fromJson(Map<String, dynamic> json) => ReviewPage(
        items: [for (final r in json['items'] as List<dynamic>) Review.fromJson(r as Map<String, dynamic>)],
        page: json['page'] as int,
        pageSize: json['pageSize'] as int,
        totalCount: json['totalCount'] as int,
      );

  @override
  List<Object?> get props => [items, page, pageSize, totalCount];
}

class City extends Equatable {
  const City({required this.name, required this.region, required this.cinemaCount});

  final String name;
  final String region;
  final int cinemaCount;

  /// "Chimbote, Áncash": lo que muestra la cabecera.
  String get label => '$name, $region';

  factory City.fromJson(Map<String, dynamic> json) =>
      City(name: json['city'] as String, region: json['region'] as String, cinemaCount: json['cinemaCount'] as int);

  @override
  List<Object?> get props => [name, region, cinemaCount];
}

List<CinemaRef> _cinemas(Object? json) =>
    [for (final c in (json as List<dynamic>? ?? const [])) CinemaRef.fromJson(c as Map<String, dynamic>)];

DateTime _date(String value) => DateTime.parse(value); // "2026-10-11": una fecha del calendario, sin hora ni zona

class MovieSummary extends Equatable {
  const MovieSummary({
    required this.id,
    required this.title,
    required this.durationMinutes,
    required this.classification,
    required this.genres,
    this.posterUrl,
    this.trailerUrl,
    this.previewVideoUrl,
    this.synopsis,
    this.releaseDate,
    this.preSaleStartsOn,
    this.status = MovieStatus.nowShowing,
    this.averageRating,
    this.reviewCount = 0,
    this.featuredReview,
    this.cinemas = const [],
  });

  final int id;
  final String title;
  final int durationMinutes;
  final String classification;
  final List<String> genres;
  final String? posterUrl;
  final String? trailerUrl;

  /// Video corto que se reproduce en la tarjeta cuando es la activa del carrusel; `null` si no tiene.
  final String? previewVideoUrl;
  final String? synopsis;

  /// Fecha de estreno (calendario del cine) y, si hay preventa, desde cuándo se puede reservar.
  final DateTime? releaseDate;
  final DateTime? preSaleStartsOn;
  final MovieStatus status;

  /// Promedio redondeado a un decimal; `null` mientras nadie ha opinado.
  final double? averageRating;
  final int reviewCount;

  /// La opinión más reciente que tiene comentario.
  final Review? featuredReview;

  /// Cines con funciones próximas (vacío en "próximamente").
  final List<CinemaRef> cinemas;

  /// Código del tráiler en YouTube, o `null` si la película no tiene uno válido.
  String? get trailerVideoId => YouTubeId.parse(trailerUrl);

  /// Se pueden reservar asientos (cartelera o preventa).
  bool get canBook => status != MovieStatus.comingSoon;

  factory MovieSummary.fromJson(Map<String, dynamic> json) => MovieSummary(
        id: json['id'] as int,
        title: json['title'] as String,
        durationMinutes: json['durationMinutes'] as int,
        classification: json['classification'] as String,
        posterUrl: json['posterUrl'] as String?,
        trailerUrl: json['trailerUrl'] as String?,
        previewVideoUrl: json['previewVideoUrl'] as String?,
        synopsis: json['synopsis'] as String?,
        genres: [for (final g in json['genres'] as List<dynamic>) g as String],
        releaseDate: json['releaseDate'] == null ? null : _date(json['releaseDate'] as String),
        preSaleStartsOn: json['preSaleStartsOn'] == null ? null : _date(json['preSaleStartsOn'] as String),
        status: MovieStatus.fromApi(json['status'] as String? ?? 'NowShowing'),
        averageRating: (json['averageRating'] as num?)?.toDouble(),
        reviewCount: json['reviewCount'] as int? ?? 0,
        featuredReview: json['featuredReview'] == null ? null : Review.fromJson(json['featuredReview'] as Map<String, dynamic>),
        cinemas: _cinemas(json['cinemas']),
      );

  @override
  List<Object?> get props =>
      [id, title, durationMinutes, classification, genres, posterUrl, trailerUrl, previewVideoUrl, synopsis, releaseDate, preSaleStartsOn, status, averageRating, reviewCount, featuredReview, cinemas];
}

class MoviePage extends Equatable {
  const MoviePage({required this.items, required this.page, required this.pageSize, required this.totalCount});

  final List<MovieSummary> items;
  final int page;
  final int pageSize;
  final int totalCount;

  bool get hasMore => page * pageSize < totalCount;

  factory MoviePage.fromJson(Map<String, dynamic> json) => MoviePage(
        items: [for (final m in json['items'] as List<dynamic>) MovieSummary.fromJson(m as Map<String, dynamic>)],
        page: json['page'] as int,
        pageSize: json['pageSize'] as int,
        totalCount: json['totalCount'] as int,
      );

  @override
  List<Object?> get props => [items, page, pageSize, totalCount];
}

class ShowtimeSummary extends Equatable {
  const ShowtimeSummary({
    required this.id,
    required this.startsAt,
    required this.roomName,
    required this.price,
    required this.availableSeats,
    this.cinemaId = 0,
    this.cinemaName = '',
  });

  final int id;
  final DateTime startsAt; // UTC
  final String roomName;
  final double price;
  final int availableSeats;
  final int cinemaId;
  final String cinemaName;

  factory ShowtimeSummary.fromJson(Map<String, dynamic> json) => ShowtimeSummary(
        id: json['id'] as int,
        startsAt: DateTime.parse(json['startsAt'] as String).toUtc(),
        roomName: json['roomName'] as String,
        price: (json['price'] as num).toDouble(),
        availableSeats: json['availableSeats'] as int,
        cinemaId: json['cinemaId'] as int? ?? 0,
        cinemaName: json['cinemaName'] as String? ?? '',
      );

  @override
  List<Object?> get props => [id, startsAt, roomName, price, availableSeats, cinemaId, cinemaName];
}

class MovieDetail extends Equatable {
  const MovieDetail({
    required this.id,
    required this.title,
    required this.durationMinutes,
    required this.classification,
    required this.genres,
    required this.showtimes,
    this.synopsis,
    this.posterUrl,
    this.trailerUrl,
    this.previewVideoUrl,
    this.releaseDate,
    this.preSaleStartsOn,
    this.status = MovieStatus.nowShowing,
    this.averageRating,
    this.reviewCount = 0,
    this.featuredReview,
    this.cinemas = const [],
  });

  final int id;
  final String title;
  final String? synopsis;
  final int durationMinutes;
  final String classification;
  final List<String> genres;
  final String? posterUrl;
  final String? trailerUrl;

  /// Video corto (sin sonido, en bucle) que se muestra arriba del detalle; `null` si la película no tiene.
  final String? previewVideoUrl;
  final DateTime? releaseDate;
  final DateTime? preSaleStartsOn;
  final MovieStatus status;
  final double? averageRating;
  final int reviewCount;
  final Review? featuredReview;
  final List<CinemaRef> cinemas;
  final List<ShowtimeSummary> showtimes;

  bool get canBook => status != MovieStatus.comingSoon;

  factory MovieDetail.fromJson(Map<String, dynamic> json) => MovieDetail(
        id: json['id'] as int,
        title: json['title'] as String,
        synopsis: json['synopsis'] as String?,
        durationMinutes: json['durationMinutes'] as int,
        classification: json['classification'] as String,
        posterUrl: json['posterUrl'] as String?,
        trailerUrl: json['trailerUrl'] as String?,
        previewVideoUrl: json['previewVideoUrl'] as String?,
        genres: [for (final g in json['genres'] as List<dynamic>) g as String],
        releaseDate: json['releaseDate'] == null ? null : _date(json['releaseDate'] as String),
        preSaleStartsOn: json['preSaleStartsOn'] == null ? null : _date(json['preSaleStartsOn'] as String),
        status: MovieStatus.fromApi(json['status'] as String? ?? 'NowShowing'),
        averageRating: (json['averageRating'] as num?)?.toDouble(),
        reviewCount: json['reviewCount'] as int? ?? 0,
        featuredReview: json['featuredReview'] == null ? null : Review.fromJson(json['featuredReview'] as Map<String, dynamic>),
        cinemas: _cinemas(json['cinemas']),
        showtimes: [for (final s in json['upcomingShowtimes'] as List<dynamic>) ShowtimeSummary.fromJson(s as Map<String, dynamic>)],
      );

  @override
  List<Object?> get props =>
      [id, title, synopsis, durationMinutes, classification, genres, posterUrl, trailerUrl, previewVideoUrl, releaseDate, preSaleStartsOn, status, averageRating, reviewCount, featuredReview, cinemas, showtimes];
}

/// Comentario público del tráiler en YouTube. Se muestra con su nombre público tal como lo publica YouTube.
class TrailerComment extends Equatable {
  const TrailerComment({required this.authorName, required this.text, required this.likeCount, required this.publishedAt, this.authorAvatarUrl});

  final String authorName;
  final String? authorAvatarUrl;
  final String text;
  final int likeCount;
  final DateTime publishedAt; // UTC

  factory TrailerComment.fromJson(Map<String, dynamic> json) => TrailerComment(
        authorName: json['authorName'] as String,
        authorAvatarUrl: json['authorAvatarUrl'] as String?,
        text: json['text'] as String,
        likeCount: json['likeCount'] as int? ?? 0,
        publishedAt: DateTime.parse(json['publishedAt'] as String).toUtc(),
      );

  @override
  List<Object?> get props => [authorName, authorAvatarUrl, text, likeCount, publishedAt];
}

/// `status`: `ok` (hay comentarios), `empty`, `disabled` (comentarios cerrados), `noTrailer` o `unavailable` (el servidor no tiene
/// clave de YouTube o YouTube no respondió). Solo `ok` trae comentarios.
class TrailerComments extends Equatable {
  const TrailerComments({required this.status, required this.items, this.videoId});

  final String status;
  final String? videoId;
  final List<TrailerComment> items;

  bool get hasComments => status == 'ok' && items.isNotEmpty;

  factory TrailerComments.fromJson(Map<String, dynamic> json) => TrailerComments(
        status: json['status'] as String? ?? 'unavailable',
        videoId: json['videoId'] as String?,
        items: [for (final c in json['items'] as List<dynamic>? ?? const []) TrailerComment.fromJson(c as Map<String, dynamic>)],
      );

  @override
  List<Object?> get props => [status, videoId, items];
}
