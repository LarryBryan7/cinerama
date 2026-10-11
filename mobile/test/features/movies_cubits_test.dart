import 'dart:async';

import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/core/location/device_location.dart';
import 'package:cinerama_mobile/features/movies/bloc/location_cubit.dart';
import 'package:cinerama_mobile/features/movies/bloc/movies_cubit.dart';
import 'package:cinerama_mobile/features/movies/bloc/reviews_cubit.dart';
import 'package:cinerama_mobile/features/movies/bloc/trailer_comments_cubit.dart';
import 'package:cinerama_mobile/features/movies/data/catalog_repository.dart';
import 'package:cinerama_mobile/features/movies/data/movie_models.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../helpers/device_location_fake.dart';
import '../helpers/movie_helpers.dart';

class MockCatalog extends Mock implements CatalogRepository {}

void main() {
  late MockCatalog repo;

  setUpAll(() => registerFallbackValue(MovieStatus.nowShowing));
  setUp(() => repo = MockCatalog());

  void stubMovies(Future<MoviePage> Function(MovieStatus? status, String? city, int page) answer) {
    when(() => repo.movies(search: any(named: 'search'), status: any(named: 'status'), city: any(named: 'city'), page: any(named: 'page'), pageSize: any(named: 'pageSize')))
        .thenAnswer((i) => answer(i.namedArguments[#status] as MovieStatus?, i.namedArguments[#city] as String?, i.namedArguments[#page] as int));
  }

  group('MoviesCubit', () {
    test('al cargar pide la pestaña actual (cartelera) y publica las películas', () async {
      stubMovies((status, city, page) async => moviePage([movieSummary()]));
      final cubit = MoviesCubit(repo);

      await cubit.load();

      verify(() => repo.movies(search: '', status: MovieStatus.nowShowing, city: null, page: 1, pageSize: 20)).called(1);
      expect(cubit.state.status, LoadStatus.ready);
      expect(cubit.state.items, hasLength(1));
    });

    test('cambiar de pestaña recarga con ese estado y NO muestra las películas de la pestaña anterior mientras carga', () async {
      final gate = Completer<MoviePage>();
      stubMovies((status, city, page) => status == MovieStatus.preSale ? gate.future : Future.value(moviePage([movieSummary(title: 'En cartelera')])));
      final cubit = MoviesCubit(repo);
      await cubit.load();
      final states = <MoviesState>[];
      cubit.stream.listen(states.add);

      final change = cubit.setTab(MovieStatus.preSale);
      await pumpEventQueue();

      expect(cubit.state.tab, MovieStatus.preSale);
      expect(cubit.state.status, LoadStatus.loading);
      expect(cubit.state.items, isEmpty, reason: 'las películas de cartelera no deben verse bajo la pestaña Preventa');

      gate.complete(moviePage([movieSummary(title: 'En preventa', status: MovieStatus.preSale)]));
      await change;
      expect(cubit.state.items.single.title, 'En preventa');
      expect(cubit.state.tab, MovieStatus.preSale);
    });

    test('tocar la pestaña que ya está activa no vuelve a pedir nada', () async {
      stubMovies((status, city, page) async => moviePage([movieSummary()]));
      final cubit = MoviesCubit(repo);
      await cubit.load();
      clearInteractions(repo);

      await cubit.setTab(MovieStatus.nowShowing);

      verifyNever(() => repo.movies(search: any(named: 'search'), status: any(named: 'status'), city: any(named: 'city'), page: any(named: 'page'), pageSize: any(named: 'pageSize')));
    });

    test('una respuesta tardía de la pestaña anterior se descarta y no pisa la lista actual', () async {
      final slow = Completer<MoviePage>();
      stubMovies((status, city, page) => status == MovieStatus.nowShowing ? slow.future : Future.value(moviePage([movieSummary(title: 'Próximamente 1', status: MovieStatus.comingSoon)])));
      final cubit = MoviesCubit(repo);

      final first = cubit.load(); // cartelera: tarda
      await pumpEventQueue();
      await cubit.setTab(MovieStatus.comingSoon); // la persona cambia de pestaña y esa responde antes
      slow.complete(moviePage([movieSummary(title: 'Cartelera tardía')]));
      await first;

      expect(cubit.state.tab, MovieStatus.comingSoon);
      expect(cubit.state.items.single.title, 'Próximamente 1', reason: 'la respuesta vieja no puede reemplazarla');
    });

    test('cambiar de ciudad recarga con esa ciudad y conserva la pestaña', () async {
      stubMovies((status, city, page) async => moviePage([movieSummary()]));
      final cubit = MoviesCubit(repo);
      await cubit.setTab(MovieStatus.preSale);

      await cubit.setCity('Chimbote');

      verify(() => repo.movies(search: '', status: MovieStatus.preSale, city: 'Chimbote', page: 1, pageSize: 20)).called(1);
      expect(cubit.state.city, 'Chimbote');
      expect(cubit.state.tab, MovieStatus.preSale);
    });

    test('loadMore sigue usando la pestaña y la ciudad elegidas', () async {
      stubMovies((status, city, page) async => moviePage([movieSummary(id: page)], page: page, pageSize: 1, total: 2));
      final cubit = MoviesCubit(repo);
      await cubit.setCity('Chimbote');
      await cubit.setTab(MovieStatus.preSale);

      await cubit.loadMore();

      verify(() => repo.movies(search: '', status: MovieStatus.preSale, city: 'Chimbote', page: 2, pageSize: 20)).called(1);
      expect(cubit.state.items.map((m) => m.id), [1, 2]);
    });

    test('si falla al cambiar de pestaña, no deja a la vista las películas de la anterior', () async {
      stubMovies((status, city, page) async {
        if (status == MovieStatus.comingSoon) throw const ApiException.network();
        return moviePage([movieSummary()]);
      });
      final cubit = MoviesCubit(repo);
      await cubit.load();

      await cubit.setTab(MovieStatus.comingSoon);

      expect(cubit.state.status, LoadStatus.failure);
      expect(cubit.state.items, isEmpty);
      expect(cubit.state.errorCode, 'network_error');
    });

    test('si falla recargar la misma pestaña, se conserva lo que ya se veía', () async {
      var calls = 0;
      stubMovies((status, city, page) async {
        if (++calls > 1) throw const ApiException.network();
        return moviePage([movieSummary()]);
      });
      final cubit = MoviesCubit(repo);
      await cubit.load();

      await cubit.load();

      expect(cubit.state.status, LoadStatus.failure);
      expect(cubit.state.items, hasLength(1));
    });
  });

  group('LocationCubit', () {
    const chimbote = City(name: 'Chimbote', region: 'Áncash', cinemaCount: 2);
    const trujillo = City(name: 'Trujillo', region: 'La Libertad', cinemaCount: 1);

    test('por defecto elige la primera ciudad', () async {
      when(() => repo.cities()).thenAnswer((_) async => [chimbote, trujillo]);
      final cubit = LocationCubit(repo);

      await cubit.load();

      expect(cubit.state.selected, chimbote);
      expect(cubit.state.cities, hasLength(2));
    });

    test('al recargar conserva la ciudad elegida si sigue existiendo, y si no, la primera', () async {
      when(() => repo.cities()).thenAnswer((_) async => [chimbote, trujillo]);
      final cubit = LocationCubit(repo);
      await cubit.load();
      cubit.select(trujillo);

      await cubit.load();
      expect(cubit.state.selected, trujillo);

      when(() => repo.cities()).thenAnswer((_) async => [chimbote]);
      await cubit.load();
      expect(cubit.state.selected, chimbote);
    });

    test('sin ciudades no hay ciudad elegida', () async {
      when(() => repo.cities()).thenAnswer((_) async => const []);
      final cubit = LocationCubit(repo);

      await cubit.load();

      expect(cubit.state.selected, isNull);
    });

    test('si falla, conserva lo que había', () async {
      when(() => repo.cities()).thenAnswer((_) async => [chimbote]);
      final cubit = LocationCubit(repo);
      await cubit.load();
      when(() => repo.cities()).thenThrow(const ApiException.network());

      await runZonedGuarded(() => cubit.load(), (_, _) {}); // addError reporta el fallo; el estado no se pierde

      expect(cubit.state.status, LoadStatus.failure);
      expect(cubit.state.selected, chimbote);
    });
  });

  group('LocationCubit y la ubicación del dispositivo', () {
    const chimbote = City(name: 'Chimbote', region: 'Áncash', cinemaCount: 2);

    setUp(() => when(() => repo.cities()).thenAnswer((_) async => [chimbote]));

    test('al cargar pide la ubicación (con permiso) y guarda la posición', () async {
      final device = FakeDeviceLocation(const LocationResult(LocationAccess.granted, nearChimbote));
      final cubit = LocationCubit(repo, device);

      await cubit.load();

      expect(device.calls, 1);
      expect(device.asked, [true]);
      expect(cubit.state.access, LocationAccess.granted);
      expect(cubit.state.position, nearChimbote);
      expect(cubit.state.selected, chimbote);
    });

    test('si se niega, no hay posición, se sabe por qué y las ciudades siguen disponibles', () async {
      final cubit = LocationCubit(repo, FakeDeviceLocation(const LocationResult(LocationAccess.denied)));

      await cubit.load();

      expect(cubit.state.access, LocationAccess.denied);
      expect(cubit.state.position, isNull);
      expect(cubit.state.cities, hasLength(1));
    });

    test('recargar la pantalla NO vuelve a molestar con el permiso si ya se respondió', () async {
      final device = FakeDeviceLocation(const LocationResult(LocationAccess.denied));
      final cubit = LocationCubit(repo, device);
      await cubit.load();

      await cubit.load();
      await cubit.load();

      expect(device.calls, 1);
    });

    test('pedirla a propósito (desde el selector) sí vuelve a preguntar, y al concederla guarda la posición', () async {
      final device = FakeDeviceLocation(const LocationResult(LocationAccess.denied));
      final cubit = LocationCubit(repo, device);
      await cubit.load();

      device.result = const LocationResult(LocationAccess.granted, nearChimbote);
      await cubit.locate();

      expect(device.calls, 2);
      expect(cubit.state.position, nearChimbote);
      expect(cubit.state.access, LocationAccess.granted);
    });

    test('perder el permiso después no borra la última posición conocida (la distancia sigue siendo útil)', () async {
      final device = FakeDeviceLocation(const LocationResult(LocationAccess.granted, nearChimbote));
      final cubit = LocationCubit(repo, device);
      await cubit.load();

      device.result = const LocationResult(LocationAccess.unavailable);
      await cubit.locate();

      expect(cubit.state.access, LocationAccess.unavailable);
      expect(cubit.state.position, nearChimbote);
    });

    test('dos solicitudes de ubicación a la vez: la segunda se ignora', () async {
      final gate = Completer<LocationResult>();
      final device = _SlowDevice(gate.future);
      final cubit = LocationCubit(repo, device);

      final first = cubit.locate();
      await cubit.locate();
      gate.complete(const LocationResult(LocationAccess.granted, nearChimbote));
      await first;

      expect(device.calls, 1);
    });

    test('sin ciudades por un error de red, igual se intenta la ubicación', () async {
      when(() => repo.cities()).thenThrow(const ApiException.network());
      final device = FakeDeviceLocation();
      final cubit = LocationCubit(repo, device);

      await runZonedGuarded(() => cubit.load(), (_, _) {});

      expect(device.calls, 1);
    });

    test('sin un servicio de ubicación configurado no hace nada', () async {
      final cubit = LocationCubit(repo);

      await cubit.locate();
      await cubit.load();

      expect(cubit.state.access, LocationAccess.unknown);
    });

    test('abrir los ajustes se delega al dispositivo', () async {
      final device = FakeDeviceLocation();
      await LocationCubit(repo, device).openSettings();

      expect(device.settingsOpened, 1);
    });
  });

  group('TrailerCommentsCubit', () {
    final comment = TrailerComment(authorName: '@ana', text: 'Hola', likeCount: 1, publishedAt: _epoch);

    test('carga hasta 20 comentarios y los publica', () async {
      when(() => repo.trailerComments(7, limit: 20)).thenAnswer((_) async => TrailerComments(status: 'ok', items: [comment]));
      final cubit = TrailerCommentsCubit(repo, 7);
      expect(cubit.state.isLoading, isTrue, reason: 'antes de preguntar todavía no se sabe');

      await cubit.load();

      expect(cubit.state.status, LoadStatus.ready);
      expect(cubit.state.hasComments, isTrue);
      expect(cubit.state.isLoading, isFalse);
    });

    test('con "unavailable" o sin comentarios no hay sección, y deja de estar cargando', () async {
      when(() => repo.trailerComments(7, limit: 20)).thenAnswer((_) async => const TrailerComments(status: 'unavailable', items: []));
      final cubit = TrailerCommentsCubit(repo, 7);

      await cubit.load();

      expect(cubit.state.hasComments, isFalse);
      expect(cubit.state.isLoading, isFalse);
    });

    test('si falla la red, no es un error visible: simplemente no hay sección', () async {
      when(() => repo.trailerComments(7, limit: 20)).thenThrow(const ApiException.network());
      final cubit = TrailerCommentsCubit(repo, 7);

      await cubit.load();

      expect(cubit.state.status, LoadStatus.failure);
      expect(cubit.state.hasComments, isFalse);
      expect(cubit.state.isLoading, isFalse);
    });

    test('recargar conserva los comentarios mientras llega la respuesta nueva', () async {
      final gate = Completer<TrailerComments>();
      var first = true;
      when(() => repo.trailerComments(7, limit: 20)).thenAnswer((_) {
        if (first) {
          first = false;
          return Future.value(TrailerComments(status: 'ok', items: [comment]));
        }
        return gate.future;
      });
      final cubit = TrailerCommentsCubit(repo, 7);
      await cubit.load();

      final reload = cubit.load();
      await pumpEventQueue();
      expect(cubit.state.comments.items, hasLength(1), reason: 'no parpadea a vacío al recargar');

      gate.complete(TrailerComments(status: 'ok', items: [comment, comment]));
      await reload;
      expect(cubit.state.comments.items, hasLength(2));
    });
  });

  group('ReviewsCubit', () {
    test('carga la primera página y detecta la opinión propia', () async {
      when(() => repo.reviews(7, page: any(named: 'page'), pageSize: any(named: 'pageSize')))
          .thenAnswer((_) async => ReviewPage(items: [review(id: 1), review(id: 2, mine: true)], page: 1, pageSize: 20, totalCount: 2));
      final cubit = ReviewsCubit(repo, 7);

      await cubit.load();

      expect(cubit.state.items, hasLength(2));
      expect(cubit.state.mine?.id, 2);
      expect(cubit.state.totalCount, 2);
    });

    test('guardar publica y vuelve a pedir la lista: el servidor es la referencia', () async {
      var saved = false;
      when(() => repo.saveReview(7, rating: 4, comment: 'Muy buena')).thenAnswer((_) async {
        saved = true;
        return review(mine: true, rating: 4);
      });
      when(() => repo.reviews(7, page: any(named: 'page'), pageSize: any(named: 'pageSize'))).thenAnswer(
          (_) async => ReviewPage(items: saved ? [review(id: 9, mine: true, rating: 4)] : const [], page: 1, pageSize: 20, totalCount: saved ? 1 : 0));
      final cubit = ReviewsCubit(repo, 7);
      await cubit.load();

      final ok = await cubit.save(rating: 4, comment: 'Muy buena');

      expect(ok, isTrue);
      expect(cubit.state.mine?.rating, 4);
      expect(cubit.state.submitting, isFalse);
      expect(cubit.state.saveErrorCode, isNull);
    });

    test('si el servidor lo rechaza (película sin estrenar), devuelve false y deja el motivo para mostrarlo', () async {
      when(() => repo.reviews(7, page: any(named: 'page'), pageSize: any(named: 'pageSize')))
          .thenAnswer((_) async => const ReviewPage(items: [], page: 1, pageSize: 20, totalCount: 0));
      when(() => repo.saveReview(7, rating: any(named: 'rating'), comment: any(named: 'comment')))
          .thenThrow(const ApiException(status: 422, code: 'movie_not_released', message: 'x'));
      final cubit = ReviewsCubit(repo, 7);
      await cubit.load();

      final ok = await cubit.save(rating: 5);

      expect(ok, isFalse);
      expect(cubit.state.saveErrorCode, 'movie_not_released');
      expect(cubit.state.submitting, isFalse);
    });

    test('un límite de solicitudes (429) también se informa sin romper nada', () async {
      when(() => repo.reviews(7, page: any(named: 'page'), pageSize: any(named: 'pageSize')))
          .thenAnswer((_) async => const ReviewPage(items: [], page: 1, pageSize: 20, totalCount: 0));
      when(() => repo.saveReview(7, rating: any(named: 'rating'), comment: any(named: 'comment')))
          .thenThrow(const ApiException(status: 429, code: 'rate_limited', message: 'x'));
      final cubit = ReviewsCubit(repo, 7);
      await cubit.load();

      expect(await cubit.save(rating: 5), isFalse);
      expect(cubit.state.saveErrorCode, 'rate_limited');
    });

    test('dos guardados a la vez: el segundo se ignora (un solo envío)', () async {
      when(() => repo.reviews(7, page: any(named: 'page'), pageSize: any(named: 'pageSize')))
          .thenAnswer((_) async => const ReviewPage(items: [], page: 1, pageSize: 20, totalCount: 0));
      final gate = Completer<Review>();
      when(() => repo.saveReview(7, rating: any(named: 'rating'), comment: any(named: 'comment'))).thenAnswer((_) => gate.future);
      final cubit = ReviewsCubit(repo, 7);
      await cubit.load();

      final first = cubit.save(rating: 5);
      final second = await cubit.save(rating: 5);
      gate.complete(review(mine: true));
      await first;

      expect(second, isFalse);
      verify(() => repo.saveReview(7, rating: 5, comment: null)).called(1);
    });

    test('borrar la opinión propia vuelve a pedir la lista', () async {
      var deleted = false;
      when(() => repo.deleteReview(7)).thenAnswer((_) async => deleted = true);
      when(() => repo.reviews(7, page: any(named: 'page'), pageSize: any(named: 'pageSize'))).thenAnswer(
          (_) async => ReviewPage(items: deleted ? const [] : [review(mine: true)], page: 1, pageSize: 20, totalCount: deleted ? 0 : 1));
      final cubit = ReviewsCubit(repo, 7);
      await cubit.load();
      expect(cubit.state.mine, isNotNull);

      expect(await cubit.deleteMine(), isTrue);

      expect(cubit.state.mine, isNull);
      expect(cubit.state.items, isEmpty);
    });

    test('loadMore agrega la página siguiente', () async {
      when(() => repo.reviews(7, page: any(named: 'page'), pageSize: any(named: 'pageSize'))).thenAnswer((i) async {
        final page = i.namedArguments[#page] as int;
        return ReviewPage(items: [review(id: page)], page: page, pageSize: 1, totalCount: 2);
      });
      final cubit = ReviewsCubit(repo, 7);
      await cubit.load();

      await cubit.loadMore();

      expect(cubit.state.items.map((r) => r.id), [1, 2]);
      expect(cubit.state.hasMore, isFalse);
    });
  });
}

/// Un dispositivo cuya respuesta tarda hasta que el test la libera.
class _SlowDevice implements DeviceLocation {
  _SlowDevice(this._answer);

  final Future<LocationResult> _answer;
  int calls = 0;

  @override
  Future<LocationResult> locate({bool askPermission = true}) {
    calls++;
    return _answer;
  }

  @override
  Future<void> openSettings() async {}
}

final _epoch = DateTime.utc(2026, 10, 1);
