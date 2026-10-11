import 'dart:async';

import 'package:cinerama_mobile/features/movies/bloc/movie_detail_cubit.dart';
import 'package:cinerama_mobile/features/movies/data/catalog_repository.dart';
import 'package:cinerama_mobile/features/movies/data/movie_models.dart';
import 'package:cinerama_mobile/features/movies/ui/movie_detail_page.dart';
import 'package:cinerama_mobile/features/movies/ui/movie_preview_video.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:mocktail/mocktail.dart';

import '../helpers/fake_video_controller.dart';


class MockCatalog extends Mock implements CatalogRepository {}

void main() {
  setUpAll(() => initializeDateFormatting('es'));

  Future<List<FakeVideoController>> pumpVideo(
    WidgetTester tester, {
    FakeVideoController Function()? make,
    String url = 'http://localhost/media/posters/verity.mp4',
    String? poster = 'http://localhost/media/posters/verity.jpg',
    Size size = const Size(390, 800),
  }) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final created = <FakeVideoController>[];
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: Center(
          child: MoviePreviewVideo(
            videoUrl: url,
            posterUrl: poster,
            controllerFactory: (_) {
              final c = (make ?? FakeVideoController.new)();
              created.add(c);
              return c;
            },
          ),
        ),
      ),
    ));
    return created;
  }

  testWidgets('mientras se inicializa muestra el póster y un indicador; al terminar muestra el video', (tester) async {
    final gate = Completer<void>();
    final created = await pumpVideo(tester, make: () => FakeVideoController(gate: gate));

    expect(find.byKey(const Key('preview-loading')), findsOneWidget);
    expect(find.byKey(const Key('preview-poster')), findsOneWidget);
    expect(find.byKey(const Key('preview-video')), findsNothing);

    gate.complete();
    await tester.pump();
    await tester.pump();

    expect(find.byKey(const Key('preview-video')), findsOneWidget);
    expect(find.byKey(const Key('preview-loading')), findsNothing);
    expect(find.byKey(const Key('preview-poster')), findsNothing, reason: 'con el video en marcha el póster ya no hace falta');
    expect(created, hasLength(1));
  });

  testWidgets('arranca solo, sin sonido y en bucle', (tester) async {
    final created = await pumpVideo(tester);
    await tester.pump();
    await tester.pump();

    final c = created.single;
    expect(c.playCalls, 1, reason: 'autoplay');
    expect(c.looping, isTrue, reason: 'bucle');
    expect(c.volume, 0, reason: 'sin sonido');
    expect(c.value.isPlaying, isTrue);
  });

  testWidgets('ocupa el ancho disponible en 16:9 y recorta con cover (no deforma)', (tester) async {
    await pumpVideo(tester, size: const Size(390, 800));
    await tester.pump();
    await tester.pump();

    final box = tester.getSize(find.byType(AspectRatio));
    expect(box.width, 390);
    expect(box.height, closeTo(390 / 16 * 9, .01));
    expect(tester.widget<FittedBox>(find.byKey(const Key('preview-video'))).fit, BoxFit.cover);
  });

  testWidgets('un video que no es 16:9 (vertical) igual llena el recuadro 16:9, recortado', (tester) async {
    await pumpVideo(tester, make: () => FakeVideoController(size: const Size(720, 1280)));
    await tester.pump();
    await tester.pump();

    expect(tester.getSize(find.byType(AspectRatio)).aspectRatio, closeTo(16 / 9, .001));
    expect(tester.takeException(), isNull);
  });

  testWidgets('si el video no se puede cargar se queda el póster (sin indicador ni hueco vacío) y no se rompe nada', (tester) async {
    final created = await pumpVideo(tester, make: () => FakeVideoController(failInit: true));
    await tester.pump();
    await tester.pump();

    expect(find.byKey(const Key('preview-poster')), findsOneWidget);
    expect(find.byKey(const Key('preview-loading')), findsNothing);
    expect(find.byKey(const Key('preview-video')), findsNothing);
    expect(created.single.disposed, isTrue, reason: 'el controlador fallido se libera');
    expect(tester.takeException(), isNull);
  });

  testWidgets('si falla a mitad de la reproducción vuelve al póster', (tester) async {
    final created = await pumpVideo(tester);
    await tester.pump();
    await tester.pump();
    expect(find.byKey(const Key('preview-video')), findsOneWidget);

    created.single.breakPlayback();
    await tester.pump(); // el aviso del controlador
    await tester.pump(); // la liberación diferida y el redibujado

    expect(find.byKey(const Key('preview-video')), findsNothing);
    expect(find.byKey(const Key('preview-poster')), findsOneWidget);
    expect(created.single.disposed, isTrue);
  });

  testWidgets('una dirección inválida cae al póster sin intentar reproducir', (tester) async {
    final created = await pumpVideo(tester, url: 'no es una url');
    await tester.pump();

    expect(created, isEmpty);
    expect(find.byKey(const Key('preview-poster')), findsOneWidget);
  });

  testWidgets('sin póster y con el video caído se ve un recuadro neutro, no un hueco', (tester) async {
    await pumpVideo(tester, make: () => FakeVideoController(failInit: true), poster: null);
    await tester.pump();
    await tester.pump();

    expect(find.byIcon(Icons.movie_outlined), findsOneWidget);
    expect(tester.getSize(find.byType(AspectRatio)).height, greaterThan(0));
  });

  testWidgets('al salir de la pantalla el controlador se libera', (tester) async {
    final created = await pumpVideo(tester);
    await tester.pump();
    await tester.pump();
    expect(created.single.disposed, isFalse);

    await tester.pumpWidget(const MaterialApp(home: SizedBox()));
    await tester.pump();

    expect(created.single.disposed, isTrue);
  });

  testWidgets('si la pantalla se cierra mientras el video se inicializa, el controlador tardío también se libera', (tester) async {
    final gate = Completer<void>();
    final created = await pumpVideo(tester, make: () => FakeVideoController(gate: gate));

    await tester.pumpWidget(const MaterialApp(home: SizedBox()));
    gate.complete();
    await tester.pump();
    await tester.pump();

    expect(created.single.disposed, isTrue, reason: 'no queda un reproductor huérfano');
  });

  testWidgets('se pausa en segundo plano y continúa al volver', (tester) async {
    final created = await pumpVideo(tester);
    await tester.pump();
    await tester.pump();
    final c = created.single;

    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
    await tester.pump();
    expect(c.pauseCalls, 1);
    expect(c.value.isPlaying, isFalse);

    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pump();
    expect(c.playCalls, 2);
    expect(c.value.isPlaying, isTrue);
  });

  testWidgets('si cambia la dirección del video se crea otro controlador y se libera el anterior', (tester) async {
    tester.view.physicalSize = const Size(390, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final created = <FakeVideoController>[];
    Widget app(String url) => MaterialApp(
          home: Scaffold(
            body: MoviePreviewVideo(videoUrl: url, controllerFactory: (_) => FakeVideoController().also(created.add)),
          ),
        );

    await tester.pumpWidget(app('http://localhost/a.mp4'));
    await tester.pump();
    await tester.pump();
    await tester.pumpWidget(app('http://localhost/b.mp4'));
    await tester.pump();
    await tester.pump();

    expect(created, hasLength(2));
    expect(created.first.disposed, isTrue);
    expect(created.last.disposed, isFalse);
  });

  group('en la pantalla de detalle', () {
    late MockCatalog repo;
    setUp(() => repo = MockCatalog());

    MovieDetail detail({String? video}) => MovieDetail(
          id: 4,
          title: 'Verity: la sombra de un engaño',
          durationMinutes: 117,
          classification: '+14',
          genres: const ['Thriller'],
          showtimes: const [],
          posterUrl: 'http://localhost/media/posters/verity.jpg',
          previewVideoUrl: video,
        );

    Future<void> pumpDetail(WidgetTester tester, MovieDetail movie) async {
      tester.view.physicalSize = const Size(390, 900);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      when(() => repo.movie(4)).thenAnswer((_) async => movie);
      await tester.pumpWidget(MaterialApp(home: BlocProvider(create: (_) => MovieDetailCubit(repo, 4), child: const MovieDetailPage())));
      await tester.pump();
      await tester.pump();
    }

    testWidgets('una película con video lo muestra arriba del título', (tester) async {
      await pumpDetail(tester, detail(video: 'http://localhost/media/posters/verity.mp4'));

      expect(find.byType(MoviePreviewVideo), findsOneWidget);
      final videoBottom = tester.getBottomLeft(find.byType(MoviePreviewVideo)).dy;
      final titleTop = tester.getTopLeft(find.text('Verity: la sombra de un engaño')).dy;
      expect(videoBottom, lessThan(titleTop));
    });

    testWidgets('la calificación es de la app y los comentarios son de YouTube: van en filas separadas, sin mezclar las cuentas', (tester) async {
      await pumpDetail(
        tester,
        MovieDetail(
          id: 4, title: 'Verity', durationMinutes: 117, classification: '+14', genres: const ['Thriller'], showtimes: const [],
          averageRating: 4.2, reviewCount: 16,
        ),
      );

      expect(tester.widget<Text>(find.byKey(const Key('detail-rating'))).data, '4.2 · 16 opiniones en la app');
      expect(find.text('Comentarios del tráiler en YouTube'), findsOneWidget);
      expect(find.byKey(const Key('detail-reviews')), findsOneWidget);
      expect(find.textContaining('16 opiniones en la app'), findsOneWidget);
      // La fila que abre la hoja de comentarios no lleva la cuenta de opiniones de la app.
      expect(find.descendant(of: find.byKey(const Key('detail-reviews')), matching: find.textContaining('16')), findsNothing);
    });

    testWidgets('una película sin video no deja ningún recuadro ni espacio', (tester) async {
      await pumpDetail(tester, detail());

      expect(find.byType(MoviePreviewVideo), findsNothing);
      expect(find.byType(AspectRatio), findsNothing);
      expect(find.text('Verity: la sombra de un engaño'), findsOneWidget);
    });
  });
}

extension<T> on T {
  T also(void Function(T value) block) {
    block(this);
    return this;
  }
}
