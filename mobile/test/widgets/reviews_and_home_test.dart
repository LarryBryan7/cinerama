import 'dart:async';

import 'package:bloc_test/bloc_test.dart';
import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/core/location/device_location.dart';
import 'package:cinerama_mobile/features/auth/bloc/auth_bloc.dart';
import 'package:cinerama_mobile/features/movies/bloc/location_cubit.dart';
import 'package:cinerama_mobile/features/movies/bloc/movies_cubit.dart';
import 'package:cinerama_mobile/features/movies/bloc/reviews_cubit.dart';
import 'package:cinerama_mobile/features/movies/bloc/trailer_comments_cubit.dart';
import 'package:cinerama_mobile/features/movies/data/catalog_repository.dart';
import 'package:cinerama_mobile/features/movies/data/movie_models.dart';
import 'package:cinerama_mobile/features/movies/ui/movies_page.dart';
import 'package:cinerama_mobile/features/movies/ui/reviews_page.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:mocktail/mocktail.dart';

import '../helpers/device_location_fake.dart';
import '../helpers/movie_helpers.dart';

class MockCatalog extends Mock implements CatalogRepository {}

class MockAuthBloc extends MockBloc<AuthEvent, AuthState> implements AuthBloc {}

void main() {
  late MockCatalog repo;
  late MockAuthBloc auth;

  setUpAll(() {
    registerFallbackValue(MovieStatus.nowShowing);
    return initializeDateFormatting('es');
  });

  setUp(() {
    repo = MockCatalog();
    auth = MockAuthBloc();
    // Por defecto el servidor no tiene comentarios del tráiler (sin clave de YouTube): la sección no aparece.
    when(() => repo.trailerComments(any(), limit: any(named: 'limit'))).thenAnswer((_) async => const TrailerComments(status: 'unavailable', items: []));
  });

  void stubTrailerComments(TrailerComments result) =>
      when(() => repo.trailerComments(7, limit: any(named: 'limit'))).thenAnswer((_) async => result);

  void stubReviews(List<Review> items, {int? total}) {
    when(() => repo.reviews(7, page: any(named: 'page'), pageSize: any(named: 'pageSize')))
        .thenAnswer((_) async => ReviewPage(items: items, page: 1, pageSize: 20, totalCount: total ?? items.length));
  }

  void setAuth(AuthStatus status) => when(() => auth.state).thenReturn(AuthState(status: status));

  /// La hoja de comentarios dentro de un router real (para comprobar a dónde navega).
  Future<GoRouter> pumpReviews(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final router = GoRouter(initialLocation: '/movies/7/reviews', routes: [
      GoRoute(
        path: '/movies/:id/reviews',
        builder: (_, _) => MultiBlocProvider(
          providers: [BlocProvider(create: (_) => ReviewsCubit(repo, 7)), BlocProvider(create: (_) => TrailerCommentsCubit(repo, 7))],
          child: ReviewsPage(now: DateTime(2026, 10, 7, 12)),
        ),
      ),
      GoRoute(path: '/login', builder: (_, state) => Scaffold(body: Text('LOGIN desde ${state.uri.queryParameters['from']}'))),
    ]);
    await tester.pumpWidget(BlocProvider<AuthBloc>.value(value: auth, child: MaterialApp.router(routerConfig: router)));
    await tester.pumpAndSettle();
    return router;
  }

  group('comentarios (hoja)', () {
    TrailerComment yt(int i, {int likes = 0, String? avatar}) =>
        TrailerComment(authorName: '@usuario$i', text: 'Comentario de YouTube $i', likeCount: likes, publishedAt: DateTime.utc(2026, 10, 5), authorAvatarUrl: avatar);

    testWidgets('la hoja muestra SOLO los comentarios de YouTube, con su rótulo y sus "me gusta" reales; los de la comunidad (generados) no se ven', (tester) async {
      setAuth(AuthStatus.authenticated);
      stubReviews([
        review(id: 1, author: 'Lucía V.', comment: 'Una joya', at: DateTime(2026, 10, 7, 8).toUtc()),
        review(id: 3, author: 'Kim D.', rating: 4, comment: 'Muy bonita', at: DateTime(2026, 9, 30, 12).toUtc()),
      ]);
      stubTrailerComments(TrailerComments(status: 'ok', videoId: '1McLG8FgNaI', items: [yt(1, likes: 1234), yt(2, likes: 7)]));

      await pumpReviews(tester);

      expect(find.text('Comentarios'), findsOneWidget);
      expect(find.byKey(const Key('trailer-comments-header')), findsOneWidget);
      expect(find.text('Comentarios del tráiler en YouTube'), findsOneWidget);
      expect(find.byKey(const Key('youtube-attribution')), findsOneWidget);
      expect(find.text('Fuente: YouTube. Se muestran tal como los publicaron.'), findsOneWidget, reason: 'YouTube pide dejar claro que es la fuente');
      expect(find.text('@usuario1'), findsOneWidget);
      expect(find.text('Comentario de YouTube 2'), findsOneWidget);
      expect(find.text('4h'), findsNothing, reason: 'ningún comentario de la comunidad');
      expect(find.text('1,2 mil'), findsOneWidget, reason: '1234 me gusta, en formato corto');
      expect(find.text('7'), findsOneWidget);
      expect(find.text('Lucía V.'), findsNothing);
      expect(find.text('Una joya'), findsNothing);
      expect(find.text('Kim D.'), findsNothing);
      expect(find.byKey(const Key('review-1')), findsNothing);
      expect(find.byKey(const Key('reviews-more')), findsNothing, reason: 'ya no hay paginación de la comunidad');
    });

    testWidgets('si el video tiene solo 2 comentarios, se muestran 2', (tester) async {
      setAuth(AuthStatus.authenticated);
      stubReviews(const []);
      stubTrailerComments(TrailerComments(status: 'ok', items: [yt(1), yt(2)]));

      await pumpReviews(tester);

      expect(find.byKey(const Key('trailer-comment-0')), findsOneWidget);
      expect(find.byKey(const Key('trailer-comment-1')), findsOneWidget);
      expect(find.byKey(const Key('trailer-comment-2')), findsNothing);
    });

    testWidgets('se piden hasta 20 comentarios del tráiler', (tester) async {
      setAuth(AuthStatus.authenticated);
      stubReviews(const []);

      await pumpReviews(tester);

      verify(() => repo.trailerComments(7, limit: 20)).called(1);
    });

    for (final status in ['unavailable', 'disabled', 'noTrailer', 'empty']) {
      testWidgets('con el estado "$status" dice que aún no hay comentarios (aunque haya de la comunidad) y no hay ningún error visible', (tester) async {
        setAuth(AuthStatus.authenticated);
        stubReviews([review(id: 1, author: 'Lucía V.', comment: 'Una joya')]);
        stubTrailerComments(TrailerComments(status: status, items: const []));

        await pumpReviews(tester);

        expect(find.byKey(const Key('reviews-empty')), findsOneWidget);
        expect(find.text('Aún no hay comentarios.'), findsOneWidget);
        expect(find.byKey(const Key('trailer-comments-header')), findsNothing);
        expect(find.text('Una joya'), findsNothing, reason: 'los comentarios generados no se muestran');
        expect(tester.takeException(), isNull);
      });
    }

    testWidgets('si falla la consulta de los comentarios del tráiler, dice que aún no hay (sin error ni comentarios generados)', (tester) async {
      setAuth(AuthStatus.authenticated);
      stubReviews([review(id: 1, author: 'Lucía V.', comment: 'Una joya')]);
      when(() => repo.trailerComments(7, limit: any(named: 'limit'))).thenThrow(const ApiException.network());

      await pumpReviews(tester);

      expect(find.byKey(const Key('reviews-empty')), findsOneWidget);
      expect(find.text('Una joya'), findsNothing);
      expect(tester.takeException(), isNull);
    });

    testWidgets('mientras llegan los comentarios se ve un indicador, no el aviso de "aún no hay"', (tester) async {
      setAuth(AuthStatus.authenticated);
      stubReviews(const []);
      final gate = Completer<TrailerComments>();
      when(() => repo.trailerComments(7, limit: any(named: 'limit'))).thenAnswer((_) => gate.future);
      tester.view.physicalSize = const Size(800, 1600);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final router = GoRouter(initialLocation: '/movies/7/reviews', routes: [
        GoRoute(
          path: '/movies/:id/reviews',
          builder: (_, _) => MultiBlocProvider(
            providers: [BlocProvider(create: (_) => ReviewsCubit(repo, 7)), BlocProvider(create: (_) => TrailerCommentsCubit(repo, 7))],
            child: ReviewsPage(now: DateTime(2026, 10, 7, 12)),
          ),
        ),
      ]);
      await tester.pumpWidget(BlocProvider<AuthBloc>.value(value: auth, child: MaterialApp.router(routerConfig: router)));
      await tester.pump();
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.byKey(const Key('reviews-empty')), findsNothing);

      gate.complete(TrailerComments(status: 'ok', items: [yt(1)]));
      await tester.pumpAndSettle();
      expect(find.text('Comentario de YouTube 1'), findsOneWidget);
    });

    testWidgets('la barra de abajo sigue invitando a escribir', (tester) async {
      setAuth(AuthStatus.authenticated);
      stubReviews(const []);

      await pumpReviews(tester);

      expect(find.byKey(const Key('reviews-write')), findsOneWidget);
      expect(find.text('¿Qué te pareció esta película?'), findsOneWidget);
    });

    testWidgets('SIN SESIÓN, la barra de escribir lleva al login y recuerda volver a esta pantalla', (tester) async {
      setAuth(AuthStatus.unauthenticated);
      stubReviews([review()]);
      await pumpReviews(tester);

      await tester.tap(find.byKey(const Key('reviews-write')));
      await tester.pumpAndSettle();

      expect(find.text('LOGIN desde /movies/7/reviews'), findsOneWidget);
      verifyNever(() => repo.saveReview(any(), rating: any(named: 'rating'), comment: any(named: 'comment')));
    });

    testWidgets('el botón de enviar hace lo mismo que la barra: sin sesión, al login', (tester) async {
      setAuth(AuthStatus.unauthenticated);
      stubReviews([review()]);
      await pumpReviews(tester);

      await tester.tap(find.byKey(const Key('reviews-send')));
      await tester.pumpAndSettle();

      expect(find.text('LOGIN desde /movies/7/reviews'), findsOneWidget);
    });

    testWidgets('con sesión: no se puede guardar sin puntuación; al elegir estrellas se envía lo escrito y el formulario se cierra', (tester) async {
      setAuth(AuthStatus.authenticated);
      var saved = false;
      when(() => repo.reviews(7, page: any(named: 'page'), pageSize: any(named: 'pageSize')))
          .thenAnswer((_) async => ReviewPage(items: saved ? [review(mine: true, rating: 4, comment: 'Muy buena')] : const [], page: 1, pageSize: 20, totalCount: saved ? 1 : 0));
      when(() => repo.saveReview(7, rating: 4, comment: 'Muy buena')).thenAnswer((_) async {
        saved = true;
        return review(mine: true, rating: 4);
      });
      await pumpReviews(tester);

      await tester.tap(find.byKey(const Key('reviews-write')));
      await tester.pumpAndSettle();
      expect(tester.widget<FilledButton>(find.byKey(const Key('review-save'))).onPressed, isNull, reason: 'sin estrellas no se puede enviar');

      await tester.tap(find.byKey(const Key('star-4')));
      await tester.enterText(find.byKey(const Key('review-comment')), 'Muy buena');
      await tester.pump();
      expect(tester.widget<FilledButton>(find.byKey(const Key('review-save'))).onPressed, isNotNull);
      await tester.tap(find.byKey(const Key('review-save')));
      await tester.pumpAndSettle();

      verify(() => repo.saveReview(7, rating: 4, comment: 'Muy buena')).called(1);
      expect(find.byKey(const Key('review-save')), findsNothing); // el formulario se cerró
      // La hoja muestra solo comentarios de YouTube: lo que escribe la persona se guarda (cuenta en la puntuación) pero no se lista aquí.
      expect(find.text('Muy buena'), findsNothing);
    });

    testWidgets('si el servidor lo rechaza, el motivo se muestra en el formulario y lo escrito NO se pierde', (tester) async {
      setAuth(AuthStatus.authenticated);
      stubReviews(const []);
      when(() => repo.saveReview(7, rating: any(named: 'rating'), comment: any(named: 'comment')))
          .thenThrow(const ApiException(status: 422, code: 'movie_not_released', message: 'x'));
      await pumpReviews(tester);
      await tester.tap(find.byKey(const Key('reviews-write')));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('star-5')));
      await tester.enterText(find.byKey(const Key('review-comment')), 'Lo escribí con cariño');
      await tester.pump();
      await tester.tap(find.byKey(const Key('review-save')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('review-error')), findsOneWidget);
      expect(find.text('Solo puedes opinar sobre una película que ya se estrenó.'), findsOneWidget);
      expect(find.text('Lo escribí con cariño'), findsOneWidget);
    });

    testWidgets('al editar, el formulario trae la opinión actual y permite borrarla', (tester) async {
      setAuth(AuthStatus.authenticated);
      var deleted = false;
      when(() => repo.reviews(7, page: any(named: 'page'), pageSize: any(named: 'pageSize')))
          .thenAnswer((_) async => ReviewPage(items: deleted ? const [] : [review(mine: true, rating: 2, comment: 'Regular')], page: 1, pageSize: 20, totalCount: deleted ? 0 : 1));
      when(() => repo.deleteReview(7)).thenAnswer((_) async => deleted = true);
      await pumpReviews(tester);

      await tester.tap(find.byKey(const Key('reviews-write')));
      await tester.pumpAndSettle();
      expect(find.text('Regular'), findsWidgets); // en la lista y en el campo
      expect(tester.widget<TextField>(find.byKey(const Key('review-comment'))).controller!.text, 'Regular');

      await tester.tap(find.byKey(const Key('review-delete')));
      await tester.pumpAndSettle();

      verify(() => repo.deleteReview(7)).called(1);
      expect(find.byKey(const Key('reviews-empty')), findsOneWidget);
    });

    testWidgets('las estrellas del formulario se anuncian con su puntuación y miden al menos 48 px', (tester) async {
      final handle = tester.ensureSemantics();
      setAuth(AuthStatus.authenticated);
      stubReviews([review(rating: 4)]);
      await pumpReviews(tester);

      await tester.tap(find.byKey(const Key('reviews-write')));
      await tester.pumpAndSettle();

      final size = tester.getSize(find.byKey(const Key('star-3')));
      expect(size.width, greaterThanOrEqualTo(48));
      expect(size.height, greaterThanOrEqualTo(48));
      handle.dispose();
    });

    testWidgets('tocar el fondo cierra la hoja', (tester) async {
      setAuth(AuthStatus.authenticated);
      stubReviews([review()]);
      final router = await pumpReviews(tester);
      router.go('/login'); // la hoja es la única ruta: se comprueba con una ruta debajo
      await tester.pumpAndSettle();
      unawaited(router.push('/movies/7/reviews'));
      await tester.pumpAndSettle();
      expect(find.text('Comentarios'), findsOneWidget);

      await tester.tapAt(const Offset(400, 100)); // sobre el fondo, por encima de la hoja
      await tester.pumpAndSettle();

      expect(find.text('Comentarios'), findsNothing);
    });
  });

  group('Inicio (cartelera)', () {
    const chimbote = City(name: 'Chimbote', region: 'Áncash', cinemaCount: 2);
    const trujillo = City(name: 'Trujillo', region: 'La Libertad', cinemaCount: 1);

    void stubHome({List<City> cities = const [chimbote, trujillo], Map<MovieStatus, List<MovieSummary>>? byTab}) {
      when(() => repo.cities()).thenAnswer((_) async => cities);
      when(() => repo.movies(search: any(named: 'search'), status: any(named: 'status'), city: any(named: 'city'), page: any(named: 'page'), pageSize: any(named: 'pageSize')))
          .thenAnswer((i) async => moviePage((byTab ?? {MovieStatus.nowShowing: [movieSummary()]})[i.namedArguments[#status] as MovieStatus] ?? const []));
    }

    Future<void> pumpHome(WidgetTester tester, {DeviceLocation? device}) async {
      tester.view.physicalSize = const Size(390, 900);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(MaterialApp(
        home: MultiBlocProvider(
          providers: [BlocProvider(create: (_) => MoviesCubit(repo)), BlocProvider(create: (_) => LocationCubit(repo, device))],
          child: const MoviesPage(),
        ),
      ));
      await tester.pumpAndSettle();
    }

    testWidgets('sin iniciar sesión se ven las películas de frente, con la ciudad en la cabecera', (tester) async {
      stubHome();

      await pumpHome(tester);

      expect(find.text('Marea Alta'), findsOneWidget);
      expect(find.text('CineFlash⚡'), findsOneWidget); // la marca de la cabecera no se toca
      expect(tester.widget<Text>(find.byKey(const Key('header-city'))).data, 'Chimbote'); // solo la ciudad, sin la región
      // La ciudad llega después de las películas: la cartelera se vuelve a pedir ya filtrada.
      verify(() => repo.movies(search: '', status: MovieStatus.nowShowing, city: 'Chimbote', page: 1, pageSize: 20)).called(1);
    });

    testWidgets('si una pestaña está vacía, las pestañas SIGUEN visibles para poder volver a otra', (tester) async {
      stubHome(byTab: {MovieStatus.nowShowing: [movieSummary()]});
      await pumpHome(tester);

      await tester.tap(find.byKey(const Key('tab-presale')));
      await tester.pumpAndSettle();

      expect(find.text('No hay películas en preventa.'), findsOneWidget);
      expect(find.byKey(const Key('tab-nowShowing')), findsOneWidget);
      expect(find.byKey(const Key('tab-comingSoon')), findsOneWidget);

      await tester.tap(find.byKey(const Key('tab-nowShowing'))); // y se puede volver
      await tester.pumpAndSettle();
      expect(find.text('Marea Alta'), findsOneWidget);
    });

    testWidgets('cada pestaña pide su estado y muestra sus películas', (tester) async {
      stubHome(byTab: {
        MovieStatus.nowShowing: [movieSummary(title: 'En cartelera')],
        MovieStatus.preSale: [movieSummary(id: 2, title: 'En preventa', status: MovieStatus.preSale)],
        MovieStatus.comingSoon: [movieSummary(id: 3, title: 'Muy pronto', status: MovieStatus.comingSoon, cinemas: const [], rating: null, reviewCount: 0)],
      });
      await pumpHome(tester);

      await tester.tap(find.byKey(const Key('tab-presale')));
      await tester.pumpAndSettle();
      expect(find.text('En preventa'), findsOneWidget);
      expect(find.text('En cartelera'), findsNothing);

      await tester.tap(find.byKey(const Key('tab-comingSoon')));
      await tester.pumpAndSettle();
      expect(find.text('Muy pronto'), findsOneWidget);
      expect(find.textContaining('Película disponible'), findsNothing);

      verify(() => repo.movies(search: '', status: MovieStatus.comingSoon, city: 'Chimbote', page: 1, pageSize: 20)).called(1);
    });

    testWidgets('el selector de ciudad lista las ciudades del servidor y filtra la cartelera con la elegida', (tester) async {
      stubHome();
      await pumpHome(tester);

      await tester.tap(find.byKey(const Key('city-picker')));
      await tester.pumpAndSettle();
      expect(find.text('Elige tu ciudad'), findsOneWidget);
      expect(find.text('Chimbote, Áncash'), findsWidgets);
      expect(find.text('Trujillo, La Libertad'), findsOneWidget);
      expect(find.text('2 cines'), findsOneWidget);
      expect(find.text('1 cine'), findsOneWidget);

      await tester.tap(find.byKey(const Key('city-Trujillo')));
      await tester.pumpAndSettle();

      expect(tester.widget<Text>(find.byKey(const Key('header-city'))).data, 'Trujillo');
      verify(() => repo.movies(search: '', status: MovieStatus.nowShowing, city: 'Trujillo', page: 1, pageSize: 20)).called(1);
    });


    testWidgets('al abrir Inicio se pide la ubicación del dispositivo UNA vez y la tarjeta muestra la distancia real', (tester) async {
      final device = FakeDeviceLocation(const LocationResult(LocationAccess.granted, nearChimbote));
      stubHome(byTab: {
        MovieStatus.nowShowing: [movieSummary(cinemas: [cinema(name: 'Cinerama - Chimbote')])],
      });

      await pumpHome(tester, device: device);

      expect(device.calls, 1);
      expect(device.asked, [true], reason: 'la primera vez se pide el permiso al sistema');
      expect(find.text('A 70 m cerca de ti'), findsOneWidget);
      expect(find.text('Película disponible en 1 cine cerca de ti'), findsOneWidget);
    });

    testWidgets('si la persona niega el permiso, la cartelera funciona igual y los cines muestran su dirección', (tester) async {
      final device = FakeDeviceLocation(const LocationResult(LocationAccess.denied));
      stubHome();

      await pumpHome(tester, device: device);

      expect(find.text('Marea Alta'), findsOneWidget);
      expect(find.text('Av. José Pardo 123'), findsOneWidget);
      expect(find.textContaining(' km'), findsNothing);
    });

    testWidgets('el selector de ciudad ofrece "Usar mi ubicación" mientras no haya ubicación, y volver a pedirla la activa', (tester) async {
      final device = FakeDeviceLocation(const LocationResult(LocationAccess.denied));
      stubHome();
      await pumpHome(tester, device: device);

      await tester.tap(find.byKey(const Key('city-picker')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('enable-location')), findsOneWidget);
      expect(find.text('Para ver qué tan cerca está cada cine. No se envía a ningún servidor.'), findsOneWidget);

      device.result = const LocationResult(LocationAccess.granted, nearChimbote); // la persona lo concede esta vez
      await tester.tap(find.byKey(const Key('enable-location')));
      await tester.pumpAndSettle();

      expect(device.calls, 2);
      expect(find.text('A 70 m cerca de ti'), findsOneWidget);
    });

    testWidgets('con el permiso bloqueado, "Usar mi ubicación" lleva a los ajustes en lugar de pedirlo otra vez', (tester) async {
      final device = FakeDeviceLocation(const LocationResult(LocationAccess.deniedForever));
      stubHome();
      await pumpHome(tester, device: device);

      await tester.tap(find.byKey(const Key('city-picker')));
      await tester.pumpAndSettle();
      expect(find.textContaining('actívalo en los ajustes'), findsOneWidget);
      await tester.tap(find.byKey(const Key('enable-location')));
      await tester.pumpAndSettle();

      expect(device.settingsOpened, 1);
      expect(device.calls, 1, reason: 'no se vuelve a pedir un permiso que el sistema ya bloqueó');
    });

    testWidgets('con la ubicación activa ya no se ofrece "Usar mi ubicación"', (tester) async {
      stubHome();
      await pumpHome(tester, device: FakeDeviceLocation());

      await tester.tap(find.byKey(const Key('city-picker')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('enable-location')), findsNothing);
    });

    testWidgets('al cambiar de pestaña el carrusel vuelve a la primera tarjeta', (tester) async {
      stubHome(byTab: {
        MovieStatus.nowShowing: [movieSummary(id: 1, title: 'A'), movieSummary(id: 2, title: 'B')],
        MovieStatus.preSale: [movieSummary(id: 3, title: 'C', status: MovieStatus.preSale), movieSummary(id: 4, title: 'D', status: MovieStatus.preSale)],
      });
      await pumpHome(tester);

      await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(-300, 0));
      await tester.pumpAndSettle();
      expect(tester.getRect(find.byKey(const Key('movie-2'))).center.dx, closeTo(195, 1));

      await tester.tap(find.byKey(const Key('tab-presale')));
      await tester.pumpAndSettle();
      expect(tester.getRect(find.byKey(const Key('movie-3'))).center.dx, closeTo(195, 1), reason: 'la primera de la pestaña nueva queda al centro');
    });

    testWidgets('si no se pudo cargar la lista de ciudades, la cartelera igual se ve (sin filtro)', (tester) async {
      when(() => repo.cities()).thenThrow(const ApiException.network());
      when(() => repo.movies(search: any(named: 'search'), status: any(named: 'status'), city: any(named: 'city'), page: any(named: 'page'), pageSize: any(named: 'pageSize')))
          .thenAnswer((_) async => moviePage([movieSummary()]));

      await runZonedGuarded(() => pumpHome(tester), (_, _) {});

      expect(find.text('Marea Alta'), findsOneWidget);
      expect(tester.widget<Text>(find.byKey(const Key('header-city'))).data, 'Elige tu ciudad');
    });
  });
}
