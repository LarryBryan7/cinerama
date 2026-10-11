import 'package:cinerama_mobile/features/movies/data/movie_models.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/movie_helpers.dart';

void main() {
  group('MovieSummary.fromJson', () {
    test('lee todo lo que muestra la tarjeta: sinopsis, calificación, opinión destacada, cines y estado', () {
      final movie = MovieSummary.fromJson(movieJson(status: 'PreSale', featured: reviewJson(author: 'David F.', comment: 'Me encantó'), cinemas: [
        cinemaJson(id: 1, name: 'Cinerama - Chimbote'),
        cinemaJson(id: 2, name: 'Cine Star - Chimbote', logoUrl: 'http://localhost/media/cinemas/cine-star.png'),
      ]));

      expect(movie.synopsis, 'En una ciudad donde nadie miente.');
      expect(movie.posterUrl, 'http://localhost/media/posters/ciudad-de-cristal.jpg');
      expect(movie.status, MovieStatus.preSale);
      expect(movie.canBook, isTrue);
      expect(movie.releaseDate, DateTime(2026, 10, 11));
      expect(movie.preSaleStartsOn, DateTime(2026, 9, 27));
      expect(movie.averageRating, 4.5);
      expect(movie.reviewCount, 20);
      expect(movie.featuredReview?.authorName, 'David F.');
      expect(movie.cinemas.map((c) => c.name), ['Cinerama - Chimbote', 'Cine Star - Chimbote']);
      expect(movie.cinemas.last.logoUrl, isNotNull);
      expect(movie.cinemas.first.logoUrl, isNull);
    });

    test('una película sin opiniones ni cines no inventa nada: calificación nula, cero opiniones, sin destacada', () {
      final movie = MovieSummary.fromJson(movieJson(status: 'ComingSoon', rating: null, reviewCount: 0, cinemas: []));

      expect(movie.averageRating, isNull);
      expect(movie.reviewCount, 0);
      expect(movie.featuredReview, isNull);
      expect(movie.cinemas, isEmpty);
      expect(movie.status, MovieStatus.comingSoon);
      expect(movie.canBook, isFalse); // "próximamente" no se puede reservar
    });

    test('un servidor más antiguo (sin los campos nuevos) sigue funcionando', () {
      final movie = MovieSummary.fromJson({
        'id': 1, 'title': 'Vieja', 'durationMinutes': 90, 'classification': '+7', 'posterUrl': null, 'genres': <String>[],
      });

      expect(movie.status, MovieStatus.nowShowing);
      expect(movie.reviewCount, 0);
      expect(movie.cinemas, isEmpty);
      expect(movie.releaseDate, isNull);
    });

    test('un estado desconocido se trata como cartelera, no rompe la pantalla', () {
      expect(MovieStatus.fromApi('Algo nuevo'), MovieStatus.nowShowing);
    });
  });

  test('la lista y el detalle leen el video corto, y sin él queda nulo', () {
    expect(MovieSummary.fromJson({...movieJson(), 'previewVideoUrl': 'http://localhost/media/posters/v.mp4'}).previewVideoUrl, 'http://localhost/media/posters/v.mp4');
    expect(MovieSummary.fromJson(movieJson()).previewVideoUrl, isNull);
    expect(MovieDetail.fromJson({...movieJson(), 'previewVideoUrl': 'http://localhost/media/posters/v.mp4', 'upcomingShowtimes': <dynamic>[]}).previewVideoUrl, isNotNull);
  });

  test('los tres estados se piden con el valor que entiende el servidor', () {
    expect(MovieStatus.values.map((s) => s.query), ['nowShowing', 'presale', 'comingSoon']);
    expect(MovieStatus.values.map((s) => MovieStatus.fromApi(s.apiValue)), MovieStatus.values);
  });

  test('MovieDetail lee estado, calificación, cines y las funciones con su cine', () {
    final detail = MovieDetail.fromJson({
      ...movieJson(status: 'PreSale', featured: reviewJson()),
      'upcomingShowtimes': [
        {'id': 20, 'startsAt': '2026-10-13T22:00:00Z', 'roomName': 'Sala 1', 'price': 12.0, 'availableSeats': 60, 'cinemaId': 1, 'cinemaName': 'Cinerama - Chimbote'},
      ],
    });

    expect(detail.status, MovieStatus.preSale);
    expect(detail.averageRating, 4.5);
    expect(detail.showtimes.single.cinemaName, 'Cinerama - Chimbote');
    expect(detail.showtimes.single.cinemaId, 1);
    expect(detail.cinemas, hasLength(1));
  });

  test('una función sin datos de cine (servidor antiguo) queda con cine vacío', () {
    final showtime = ShowtimeSummary.fromJson({'id': 1, 'startsAt': '2026-10-13T22:00:00Z', 'roomName': 'Sala 1', 'price': 12, 'availableSeats': 5});

    expect(showtime.cinemaName, '');
  });

  group('Review y ReviewPage', () {
    test('lee la opinión, su fecha en UTC y si es propia', () {
      final r = Review.fromJson(reviewJson(mine: true, comment: null));

      expect(r.isMine, isTrue);
      expect(r.comment, isNull);
      expect(r.createdAt.isUtc, isTrue);
      expect(r.createdAt, DateTime.utc(2026, 10, 7, 15));
    });

    test('hasMore indica si faltan páginas', () {
      expect(ReviewPage.fromJson(pageJson([reviewJson()], pageSize: 1, total: 2)).hasMore, isTrue);
      expect(ReviewPage.fromJson(pageJson([reviewJson()], pageSize: 20, total: 1)).hasMore, isFalse);
    });
  });

  test('City arma la etiqueta de la cabecera y CinemaRef su inicial', () {
    expect(City.fromJson({'city': 'Chimbote', 'region': 'Áncash', 'cinemaCount': 2}).label, 'Chimbote, Áncash');
    expect(CinemaRef.fromJson(cinemaJson(name: 'cine star')).initial, 'C');
    expect(CinemaRef.fromJson(cinemaJson(name: '  ')).initial, '?');
  });

  group('tráiler y comentarios de YouTube', () {
    test('trailerVideoId sale de la URL del tráiler, y es nulo si no hay una válida', () {
      expect(movieSummary(trailerUrl: 'https://www.youtube.com/watch?v=1McLG8FgNaI').trailerVideoId, '1McLG8FgNaI');
      expect(movieSummary().trailerVideoId, isNull);
      expect(movieSummary(trailerUrl: 'https://www.youtube.com/watch?v=e').trailerVideoId, isNull);
    });

    test('TrailerComments lee los comentarios, sus "me gusta" y la fecha en UTC', () {
      final c = TrailerComments.fromJson({
        'status': 'ok',
        'videoId': '1McLG8FgNaI',
        'items': [
          {'authorName': '@ana', 'authorAvatarUrl': 'https://yt3.ggpht.com/a', 'text': 'Qué buen tráiler', 'likeCount': 12, 'publishedAt': '2026-10-01T12:00:00Z'},
          {'authorName': '@beto', 'authorAvatarUrl': null, 'text': 'Sí', 'likeCount': 0, 'publishedAt': '2026-10-02T12:00:00Z'},
        ],
      });

      expect(c.hasComments, isTrue);
      expect(c.items.first.likeCount, 12);
      expect(c.items.first.publishedAt, DateTime.utc(2026, 10, 1, 12));
      expect(c.items.first.publishedAt.isUtc, isTrue);
      expect(c.items.last.authorAvatarUrl, isNull);
    });

    test('un estado sin comentarios no cuenta como "hay comentarios", ni siquiera con items', () {
      for (final status in ['empty', 'disabled', 'noTrailer', 'unavailable']) {
        expect(TrailerComments.fromJson({'status': status, 'items': <dynamic>[]}).hasComments, isFalse, reason: status);
      }
      expect(const TrailerComments(status: 'ok', items: []).hasComments, isFalse);
    });

    test('una respuesta incompleta (servidor antiguo) no rompe: queda "unavailable"', () {
      final c = TrailerComments.fromJson(<String, dynamic>{});

      expect(c.status, 'unavailable');
      expect(c.items, isEmpty);
    });
  });
}
