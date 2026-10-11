import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/features/movies/data/catalog_repository.dart';
import 'package:cinerama_mobile/features/movies/data/movie_models.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/fakes.dart';
import '../helpers/movie_helpers.dart';

void main() {
  CatalogRepository repoWith(ScriptedAdapter adapter) => CatalogRepository(dioWith(adapter));

  group('movies', () {
    test('pide el estado y la ciudad con los valores que entiende el servidor', () async {
      final adapter = ScriptedAdapter((_) => FakeResponse(200, pageJson([movieJson()])));

      await repoWith(adapter).movies(status: MovieStatus.preSale, city: 'Chimbote', search: ' faro ');

      final query = adapter.requests.single.queryParameters;
      expect(query['status'], 'presale');
      expect(query['city'], 'Chimbote');
      expect(query['search'], 'faro');
      expect(query['page'], 1);
    });

    test('sin filtros no envía parámetros vacíos', () async {
      final adapter = ScriptedAdapter((_) => FakeResponse(200, pageJson([movieJson()])));

      await repoWith(adapter).movies(search: '  ', city: ' ');

      expect(adapter.requests.single.queryParameters.keys, unorderedEquals(['page', 'pageSize']));
    });

    test('un estado inválido que rechace el servidor llega como ApiException con su código', () async {
      final adapter = ScriptedAdapter((_) => FakeResponse.problem(400, 'validation_failed'));

      await expectLater(repoWith(adapter).movies(), throwsA(isA<ApiException>().having((e) => e.code, 'code', 'validation_failed')));
    });
  });

  test('cities lee la lista de ciudades', () async {
    final adapter = ScriptedAdapter((_) => const FakeResponse(200, [
          {'city': 'Chimbote', 'region': 'Áncash', 'cinemaCount': 2},
        ]));

    final cities = await repoWith(adapter).cities();

    expect(adapter.requests.single.path, endsWith('cities'));
    expect(cities.single.label, 'Chimbote, Áncash');
  });

  group('reviews', () {
    test('lee una página de opiniones', () async {
      final adapter = ScriptedAdapter((_) => FakeResponse(200, pageJson([reviewJson(id: 1), reviewJson(id: 2, mine: true)], total: 2)));

      final page = await repoWith(adapter).reviews(7, page: 2, pageSize: 5);

      expect(adapter.requests.single.path, endsWith('movies/7/reviews'));
      expect(adapter.requests.single.queryParameters, {'page': 2, 'pageSize': 5});
      expect(page.items.map((r) => r.isMine), [false, true]);
    });

    test('saveReview envía puntuación y comentario recortado, por PUT a /mine, y NUNCA un id de usuario', () async {
      final adapter = ScriptedAdapter((_) => FakeResponse(201, reviewJson(mine: true)));

      final saved = await repoWith(adapter).saveReview(7, rating: 4, comment: '  Muy buena  ');

      final request = adapter.requests.single;
      expect(request.method, 'PUT');
      expect(request.path, endsWith('movies/7/reviews/mine'));
      expect(request.data, {'rating': 4, 'comment': 'Muy buena'});
      expect(saved.isMine, isTrue);
    });

    test('un comentario vacío no se envía', () async {
      final adapter = ScriptedAdapter((_) => FakeResponse(200, reviewJson(mine: true, comment: null)));

      await repoWith(adapter).saveReview(7, rating: 5, comment: '   ');

      expect(adapter.requests.single.data, {'rating': 5});
    });

    test('deleteReview borra con DELETE a /mine', () async {
      final adapter = ScriptedAdapter((_) => const FakeResponse(204));

      await repoWith(adapter).deleteReview(7);

      expect(adapter.requests.single.method, 'DELETE');
      expect(adapter.requests.single.path, endsWith('movies/7/reviews/mine'));
    });

    test('una película sin estrenar responde 422 movie_not_released y llega con su código', () async {
      final adapter = ScriptedAdapter((_) => FakeResponse.problem(422, 'movie_not_released'));

      await expectLater(repoWith(adapter).saveReview(7, rating: 5), throwsA(isA<ApiException>().having((e) => e.code, 'code', 'movie_not_released')));
    });
  });

  group('trailerComments', () {
    test('pide hasta 20 comentarios de la película y lee la respuesta', () async {
      final adapter = ScriptedAdapter((_) => const FakeResponse(200, {
            'status': 'ok',
            'videoId': '1McLG8FgNaI',
            'items': [
              {'authorName': '@ana', 'authorAvatarUrl': null, 'text': 'Hola', 'likeCount': 3, 'publishedAt': '2026-10-01T12:00:00Z'},
            ],
          }));

      final result = await repoWith(adapter).trailerComments(7);

      expect(adapter.requests.single.path, endsWith('movies/7/trailer-comments'));
      expect(adapter.requests.single.queryParameters, {'limit': 20});
      expect(result.items.single.text, 'Hola');
    });

    test('un límite inválido que rechace el servidor llega con su código', () async {
      final adapter = ScriptedAdapter((_) => FakeResponse.problem(400, 'validation_failed'));

      await expectLater(repoWith(adapter).trailerComments(7, limit: 99), throwsA(isA<ApiException>().having((e) => e.code, 'code', 'validation_failed')));
    });
  });
}
