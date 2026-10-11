import 'dart:async';
import 'dart:ui' show Tristate;

import 'package:cinerama_mobile/core/location/device_location.dart';
import 'package:cinerama_mobile/core/widgets/design_canvas.dart';
import 'package:cinerama_mobile/features/movies/data/movie_models.dart';
import 'package:cinerama_mobile/features/movies/ui/cartelera_slide.dart';
import 'package:cinerama_mobile/features/movies/ui/movie_preview_video.dart';
import 'package:cinerama_mobile/features/movies/ui/trailer/trailer_player.dart';
import 'package:cinerama_mobile/features/movies/ui/trailer/youtube_trailer_player.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';

import '../helpers/device_location_fake.dart';
import '../helpers/fake_trailer_player.dart';
import '../helpers/fake_video_controller.dart';
import '../helpers/movie_helpers.dart';

void main() {
  setUpAll(() => initializeDateFormatting('es'));

  final now = DateTime(2026, 10, 7, 12);

  /// El carrusel dentro de su lienzo de 390 px, como en la pantalla de Inicio.
  Future<void> pumpCarousel(
    WidgetTester tester,
    List<MovieSummary> movies, {
    ValueChanged<MovieSummary>? onOpen,
    ValueChanged<MovieSummary>? onOpenReviews,
    VoidCallback? onNearEnd,
    String? city,
    GeoPoint? position,
    VideoControllerFactory? videoFactory,
    FakeTrailerPlayers? trailers,
  }) async {
    tester.view.physicalSize = const Size(390, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: DesignCanvas(height: kCarteleraContentHeight, children: [
          Positioned.fill(
            child: CarteleraCarousel(
              movies: movies,
              onOpen: onOpen ?? (_) {},
              onOpenReviews: onOpenReviews,
              onNearEnd: onNearEnd,
              cityName: city,
              position: position,
              now: now,
              videoControllerFactory: videoFactory,
              trailerPlayerFactory: (trailers ?? FakeTrailerPlayers()).call,
            ),
          ),
        ]),
      ),
    ));
  }

  Future<void> pumpSlide(WidgetTester tester, MovieSummary movie, {ValueChanged<MovieSummary>? onOpen, ValueChanged<MovieSummary>? onOpenReviews, String? city, GeoPoint? position}) =>
      pumpCarousel(tester, [movie], onOpen: onOpen, onOpenReviews: onOpenReviews, city: city, position: position);

  group('tarjeta con datos reales', () {
    testWidgets('una película en cartelera muestra SUS datos: calificación, opinión destacada, cines y botón de precios', (tester) async {
      await pumpSlide(
        tester,
        movieSummary(
          rating: 4.5,
          reviewCount: 20,
          featured: review(author: 'David F.', comment: 'Excelente película, me encantó', at: DateTime(2026, 10, 7, 8).toUtc()),
          cinemas: [cinema(id: 1, name: 'Cinerama - Chimbote', address: 'Av. José Pardo 123'), cinema(id: 2, name: 'Cine Star - Chimbote', address: 'Referencial, Chimbote')],
        ),
      );

      expect(find.text('Marea Alta'), findsOneWidget);
      expect(find.text('Aventura, Drama | 2h 1m | +14'), findsOneWidget);
      expect(find.text('Dos hermanos heredan un faro. Ver más información'), findsOneWidget); // la sinopsis de ESTA película, completa, y el enlace
      expect(find.text('4.5'), findsOneWidget);
      expect(find.text('David F.'), findsOneWidget);
      expect(find.text('Hoy'), findsOneWidget);
      expect(find.text('Excelente película, me encantó'), findsOneWidget);
      expect(find.text('Ver \ncomentarios'), findsOneWidget, reason: 'la hoja muestra comentarios de YouTube: no lleva la cuenta de opiniones de la app');
      expect(find.textContaining('opiniones'), findsNothing);
      expect(find.text('Película disponible en 2 cines cerca de ti'), findsOneWidget);
      expect(find.text('Cinerama - Chimbote'), findsOneWidget);
      expect(find.text('Cine Star - Chimbote'), findsOneWidget);
      expect(find.text('Ver Precios y Horarios'), findsOneWidget);
      expect(find.byKey(const Key('movie-status-badge')), findsNothing);
    });

    testWidgets('NO queda ningún dato de maqueta en pantalla (ni el autor, ni los cines, ni la sinopsis de muestra)', (tester) async {
      await pumpSlide(tester, movieSummary(featured: review(author: 'Lucía V.')));

      for (final mock in ['David Flores', 'Lowen Ashleigh', 'Cine · Star - Chimbote', 'A 7.2 km cerca de ti', 'A 1.1 km cerca de ti', 'Ver 20 \nopiniones más', 'Ver 20 \nopiniones']) {
        expect(find.textContaining(mock), findsNothing, reason: mock);
      }
    });

    testWidgets('una sinopsis larga se corta en tres líneas, con puntos suspensivos y el enlace "Ver más información" al final', (tester) async {
      await pumpSlide(tester, movieSummary(synopsis: List.filled(60, 'palabra').join(' ')));

      final synopsis = find.textContaining('Ver más información');
      expect(synopsis, findsOneWidget);
      expect(tester.widget<Text>(synopsis).textSpan!.toPlainText(), matches(RegExp(r'palabra\.\.\. Ver más información$')));
      expect(tester.getSize(synopsis).height, lessThanOrEqualTo(3 * 12 * 1.22), reason: 'tres líneas como máximo (Figma: caja de 49 px)');
    });

    testWidgets('sin contador de video para una película sin video ni tráiler', (tester) async {
      await pumpSlide(tester, movieSummary());

      expect(find.byKey(const Key('movie-video-length')), findsNothing);
    });

    testWidgets('en preventa se avisa con una insignia con la fecha de estreno, y el botón invita a reservar', (tester) async {
      await pumpSlide(tester, movieSummary(status: MovieStatus.preSale, releaseDate: DateTime(2026, 10, 11)));

      expect(find.byKey(const Key('movie-status-badge')), findsOneWidget);
      expect(find.text('Preventa · estreno 11 oct'), findsOneWidget);
      expect(find.text('Reservar en preventa'), findsOneWidget);
      expect(find.text('Película disponible en 1 cine cerca de ti'), findsOneWidget); // se puede reservar: tiene cines con funciones
    });

    testWidgets('"próximamente" no muestra cines (no hay funciones), no promete opiniones y el botón solo abre el detalle', (tester) async {
      await pumpSlide(tester, movieSummary(status: MovieStatus.comingSoon, releaseDate: DateTime(2026, 11, 28), rating: null, reviewCount: 0, cinemas: const []));

      expect(find.text('Próximamente · 28 nov'), findsOneWidget);
      expect(find.textContaining('Película disponible'), findsNothing);
      expect(find.text('Cinerama - Chimbote'), findsNothing);
      expect(find.text('Ver detalles'), findsOneWidget);
      expect(find.text('Podrás opinar desde el estreno'), findsOneWidget);
      expect(find.text('Ver \ncomentarios'), findsOneWidget);
      expect(find.text('–'), findsOneWidget); // sin calificación no se inventa un número
    });

    testWidgets('una película estrenada sin opiniones invita a ser el primero', (tester) async {
      await pumpSlide(tester, movieSummary(rating: null, reviewCount: 0, featured: null));

      expect(find.text('Aún no hay opiniones'), findsOneWidget);
      expect(find.text('Ver \ncomentarios'), findsOneWidget, reason: 'el botón abre los comentarios de YouTube, haya o no opiniones en la app');
    });

    testWidgets('con un solo cine se muestra solo ese cine', (tester) async {
      await pumpSlide(tester, movieSummary(cinemas: [cinema(name: 'Único Cine')]));

      expect(find.text('Película disponible en 1 cine cerca de ti'), findsOneWidget);
      expect(find.text('Único Cine'), findsOneWidget);
    });

    testWidgets('un cine sin logo muestra su inicial; si el logo no se puede descargar, también', (tester) async {
      await pumpSlide(tester, movieSummary(cinemas: [cinema(name: 'Cinerama - Chimbote'), cinema(id: 2, name: 'Zeta Cine', logo: 'http://localhost/media/cinemas/no-existe.png')]));
      await tester.pump();

      expect(find.text('C'), findsOneWidget);
      expect(find.text('Z'), findsOneWidget);
    });

    testWidgets('los textos largos no se desbordan: título, cine y comentario se recortan con puntos suspensivos', (tester) async {
      await pumpSlide(
        tester,
        movieSummary(
          title: 'Un título larguísimo que no cabe en una sola línea de la tarjeta de cartelera',
          featured: review(author: 'María Fernanda de los Ángeles R.', comment: 'Un comentario muy largo ' * 10),
          cinemas: [cinema(name: 'Cinerama Multicines Centro Comercial Gran Plaza - Chimbote', address: 'Avenida muy larga ' * 5)],
        ),
      );

      expect(tester.takeException(), isNull);
    });

    testWidgets('tocar la tarjeta o el botón principal abre el detalle; tocar "opiniones" abre las opiniones y NO el detalle', (tester) async {
      final opened = <int>[], reviews = <int>[];
      await pumpSlide(tester, movieSummary(), onOpen: (m) => opened.add(m.id), onOpenReviews: (m) => reviews.add(m.id));

      await tester.tap(find.byKey(const Key('movie-1')), warnIfMissed: false);
      await tester.tap(find.byKey(const Key('movie-showtimes-button')));
      expect(opened, [1, 1]);

      await tester.tap(find.byKey(const Key('movie-reviews-button-1')));
      expect(reviews, [1]);
      expect(opened, [1, 1], reason: 'el botón de opiniones está encima de la tarjeta y no debe abrir el detalle');
    });

    testWidgets('la calificación se anuncia a los lectores de pantalla', (tester) async {
      final handle = tester.ensureSemantics();
      await pumpSlide(tester, movieSummary(rating: 4.5));

      expect(find.bySemanticsLabel(RegExp('Calificación 4.5 de 5')), findsOneWidget);
      handle.dispose();
    });
  });

  group('cerca de ti: ciudad y ubicación del dispositivo', () {
    final twoCinemas = [
      cinema(id: 1, name: 'Cinerama - Chimbote', address: 'Av. José Pardo 123'), // -9.0745, -78.5936
      const CinemaRef(id: 2, name: 'Cine Star - Chimbote', address: 'Referencial', city: 'Chimbote', latitude: -9.1239, longitude: -78.5207),
    ];

    testWidgets('sin ubicación (permiso negado) cada cine muestra su dirección, no una distancia inventada', (tester) async {
      await pumpSlide(tester, movieSummary(cinemas: twoCinemas), city: 'Chimbote');

      expect(find.text('Av. José Pardo 123'), findsOneWidget);
      expect(find.text('Referencial'), findsOneWidget);
      expect(find.textContaining(' km'), findsNothing);
    });

    testWidgets('con ubicación, cada cine muestra su distancia real y el más cercano va primero', (tester) async {
      await pumpSlide(tester, movieSummary(cinemas: [twoCinemas[1], twoCinemas[0]]), city: 'Chimbote', position: nearChimbote);

      expect(find.text('A 70 m cerca de ti'), findsOneWidget); // Cinerama: unos 70 m
      expect(find.textContaining('km cerca de ti'), findsOneWidget); // el otro cine, a unos 7 km
      final first = tester.getTopLeft(find.text('Cinerama - Chimbote')).dx;
      final second = tester.getTopLeft(find.text('Cine Star - Chimbote')).dx;
      expect(first, lessThan(second), reason: 'el cine más cercano va en el primer lugar del chip');
    });

    testWidgets('lejos de todo (el emulador está en California) muestra la distancia pero NO dice "cerca de ti" en cada cine', (tester) async {
      await pumpSlide(tester, movieSummary(cinemas: twoCinemas), city: 'Chimbote', position: mountainView);

      expect(find.textContaining('A 6 '), findsWidgets); // miles de km, con separador
      expect(find.textContaining('km cerca de ti'), findsNothing);
      // El aviso habla de la ciudad elegida, que es donde la persona dijo estar.
      expect(find.text('Película disponible en 2 cines cerca de ti'), findsOneWidget);
    });

    testWidgets('el aviso cuenta solo los cines de la ciudad elegida', (tester) async {
      final lima = const CinemaRef(id: 3, name: 'Cine Lima', address: 'Calle 1', city: 'Lima', latitude: -12.04, longitude: -77.04);
      await pumpSlide(tester, movieSummary(cinemas: [twoCinemas[0], lima]), city: 'Chimbote');

      expect(find.text('Película disponible en 1 cine cerca de ti'), findsOneWidget);
      expect(find.text('Cine Lima'), findsNothing);
    });

    testWidgets('si la ciudad elegida no tiene la película, se muestran los cines que sí la dan (no un chip vacío)', (tester) async {
      await pumpSlide(tester, movieSummary(cinemas: [twoCinemas[0]]), city: 'Trujillo');

      expect(find.text('Cinerama - Chimbote'), findsOneWidget);
    });
  });

  group('carrusel', () {
    final three = [movieSummary(id: 1, title: 'Primera'), movieSummary(id: 2, title: 'Segunda', status: MovieStatus.preSale), movieSummary(id: 3, title: 'Tercera')];

    testWidgets('la tarjeta activa está centrada y las vecinas asoman a los lados (efecto carrusel)', (tester) async {
      await pumpCarousel(tester, three);

      final center = tester.getRect(find.byKey(const Key('movie-1')));
      final next = tester.getRect(find.byKey(const Key('movie-2')));
      expect(center.center.dx, closeTo(195, 1)); // 390 / 2
      expect(center.width, closeTo(284, 1));
      expect(next.left - center.right, lessThan(30), reason: 'la vecina está a la separación del diseño (19 px más lo que encoge)');
      expect(next.left, lessThan(390), reason: 'la vecina asoma por el borde derecho');
      expect(next.right, greaterThan(390));
      expect(find.text('Segunda'), findsOneWidget); // ya está dibujada, aunque solo se vea un borde
    });

    testWidgets('las vecinas son más pequeñas y transparentes que la activa', (tester) async {
      await pumpCarousel(tester, three);

      double opacityOf(String title) => tester.widget<Opacity>(find.ancestor(of: find.text(title), matching: find.byType(Opacity)).first).opacity;
      expect(opacityOf('Primera'), 1.0);
      expect(opacityOf('Segunda'), closeTo(.65, .01));
    });

    testWidgets('arrastrar hacia un lado mueve las tarjetas y al soltar se ajusta a la siguiente; los cines y el botón cambian con ella', (tester) async {
      await pumpCarousel(tester, three);
      expect(find.text('Ver Precios y Horarios'), findsOneWidget);

      await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(-300, 0));
      await tester.pumpAndSettle();

      expect(tester.getRect(find.byKey(const Key('movie-2'))).center.dx, closeTo(195, 1), reason: 'la segunda quedó al centro');
      expect(find.text('Reservar en preventa'), findsOneWidget); // el botón es el de la película activa (está en preventa)
      expect(find.text('Ver Precios y Horarios'), findsNothing);
    });

    testWidgets('un arrastre corto vuelve a la tarjeta de la que se partió', (tester) async {
      await pumpCarousel(tester, three);

      await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(-40, 0));
      await tester.pumpAndSettle();

      expect(tester.getRect(find.byKey(const Key('movie-1'))).center.dx, closeTo(195, 1));
    });

    testWidgets('no se puede pasar del primero ni del último', (tester) async {
      await pumpCarousel(tester, three);

      await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(300, 0));
      await tester.pumpAndSettle();
      expect(tester.getRect(find.byKey(const Key('movie-1'))).center.dx, closeTo(195, 1));

      for (var i = 0; i < 5; i++) {
        await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(-300, 0));
        await tester.pumpAndSettle();
      }
      expect(tester.getRect(find.byKey(const Key('movie-3'))).center.dx, closeTo(195, 1));
    });

    testWidgets('tocar una vecina la centra y NO abre el detalle; tocar la activa sí lo abre', (tester) async {
      final opened = <int>[];
      await pumpCarousel(tester, three, onOpen: (m) => opened.add(m.id));

      // Se toca la parte de la vecina que asoma por el borde (su centro queda fuera de la pantalla).
      final peeking = tester.getRect(find.byKey(const Key('movie-2')));
      await tester.tapAt(Offset(peeking.left + 12, peeking.center.dy));
      await tester.pumpAndSettle();
      expect(opened, isEmpty);
      expect(tester.getRect(find.byKey(const Key('movie-2'))).center.dx, closeTo(195, 1));

      await tester.tap(find.byKey(const Key('movie-2')));
      expect(opened, [2]);
    });

    testWidgets('al llegar cerca del final se pide la página siguiente', (tester) async {
      var nearEnd = 0;
      final many = [for (var i = 1; i <= 6; i++) movieSummary(id: i, title: 'Película $i')];
      await pumpCarousel(tester, many, onNearEnd: () => nearEnd++);

      await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(-300, 0));
      await tester.pumpAndSettle();
      expect(nearEnd, 0, reason: 'en la segunda de seis todavía faltan muchas');

      for (var i = 0; i < 2; i++) {
        await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(-300, 0));
        await tester.pumpAndSettle();
      }
      expect(nearEnd, greaterThan(0), reason: 'en la cuarta de seis quedan solo tres por ver');
    });

    testWidgets('el opiniones de cada tarjeta abre las de SU película, no las de la activa', (tester) async {
      final reviews = <int>[];
      await pumpCarousel(tester, three, onOpenReviews: (m) => reviews.add(m.id));

      await tester.tap(find.byKey(const Key('movie-reviews-button-1')));

      expect(reviews, [1]);
    });

    testWidgets('con una sola película hay una tarjeta y no se puede arrastrar a ningún lado', (tester) async {
      await pumpCarousel(tester, [movieSummary()]);

      await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(-300, 0), warnIfMissed: false);
      await tester.pumpAndSettle();

      expect(tester.getRect(find.byKey(const Key('movie-1'))).center.dx, closeTo(195, 1));
      expect(tester.takeException(), isNull);
    });

    testWidgets('un gesto vertical sobre el carrusel no lo mueve (la pantalla puede desplazarse y recargar)', (tester) async {
      await pumpCarousel(tester, three);

      final gesture = await tester.startGesture(tester.getCenter(find.byKey(const Key('movie-1'))), kind: PointerDeviceKind.touch);
      await gesture.moveBy(const Offset(0, -120));
      await gesture.up();
      await tester.pumpAndSettle();

      expect(tester.getRect(find.byKey(const Key('movie-1'))).center.dx, closeTo(195, 1));
    });
  });


  group('video en la tarjeta activa', () {
    final withVideo = movieSummary(id: 2, title: 'Verity', videoUrl: 'http://localhost/media/posters/verity.mp4', posterUrl: 'http://localhost/media/posters/verity.jpg');
    final plain = [movieSummary(id: 1, title: 'Anterior'), withVideo, movieSummary(id: 3, title: 'Siguiente')];

    /// Un carrusel que registra cada reproductor que se crea.
    Future<List<FakeVideoController>> pumpWithVideos(WidgetTester tester, List<MovieSummary> movies, {FakeVideoController Function()? make}) async {
      final created = <FakeVideoController>[];
      await pumpCarousel(tester, movies, videoFactory: (_) {
        final c = (make ?? FakeVideoController.new)();
        created.add(c);
        return c;
      });
      await tester.pump();
      await tester.pump();
      return created;
    }

    Future<void> swipeTo(WidgetTester tester, String key) async {
      await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(-300, 0));
      await tester.pumpAndSettle();
    }

    testWidgets('al pasar de la película anterior a la que tiene video, el video arranca solo (autoplay, sin sonido, en bucle)', (tester) async {
      final created = await pumpWithVideos(tester, plain);
      expect(created, isEmpty, reason: 'la primera tarjeta no tiene video y la vecina no reproduce hasta ser la activa');
      expect(find.byKey(const Key('preview-video')), findsNothing);

      await swipeTo(tester, 'Verity');
      await tester.pump();
      await tester.pump();

      expect(created, hasLength(1));
      expect(find.byKey(const ValueKey('card-video-2')), findsOneWidget);
      expect(find.byKey(const Key('preview-video')), findsOneWidget);
      final c = created.single;
      expect(c.playCalls, 1);
      expect(c.value.isPlaying, isTrue);
      expect(c.looping, isTrue);
      expect(c.volume, 0);
    });

    testWidgets('la tarjeta con video muestra el contador de video (maqueta: 1:37)', (tester) async {
      await pumpWithVideos(tester, [withVideo]);

      expect(find.byKey(const Key('movie-video-length')), findsOneWidget);
      expect(find.text('1:37'), findsOneWidget);
    });

    testWidgets('el video queda DENTRO de la tarjeta: en el lugar y del tamaño del póster (261 × 140)', (tester) async {
      await pumpWithVideos(tester, [withVideo]);

      final video = tester.getRect(find.byKey(const ValueKey('card-video-2')));
      final card = tester.getRect(find.byKey(const Key('movie-2')));
      expect(video.width, closeTo(261, 1));
      expect(video.height, closeTo(140, 1));
      expect(video.left, greaterThan(card.left));
      expect(video.right, lessThan(card.right));
      expect(video.top, greaterThan(card.top));
      expect(video.bottom, lessThan(card.center.dy), reason: 'ocupa la parte de arriba de la tarjeta, no tapa el texto');
    });

    testWidgets('si la película con video es la primera, arranca al abrir Inicio', (tester) async {
      final created = await pumpWithVideos(tester, [withVideo, movieSummary(id: 3, title: 'Siguiente')]);

      expect(created, hasLength(1));
      expect(created.single.value.isPlaying, isTrue);
    });

    testWidgets('al irse a otra tarjeta, el video se libera (no sigue sonando ni consumiendo)', (tester) async {
      final created = await pumpWithVideos(tester, [withVideo, movieSummary(id: 3, title: 'Siguiente'), movieSummary(id: 4, title: 'Otra')]);
      expect(created.single.disposed, isFalse);

      await swipeTo(tester, 'Siguiente');
      await tester.pump();

      expect(created.single.disposed, isTrue);
      expect(find.byKey(const Key('preview-video')), findsNothing);
    });

    testWidgets('volver a la tarjeta con video crea un reproductor nuevo y vuelve a arrancar', (tester) async {
      final created = await pumpWithVideos(tester, [withVideo, movieSummary(id: 3, title: 'Siguiente')]);

      await swipeTo(tester, 'Siguiente');
      await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(300, 0));
      await tester.pumpAndSettle();
      await tester.pump();
      await tester.pump();

      expect(created, hasLength(2));
      expect(created.first.disposed, isTrue);
      expect(created.last.value.isPlaying, isTrue);
    });

    testWidgets('las películas sin video siguen mostrando su póster y no crean ningún reproductor', (tester) async {
      final created = await pumpWithVideos(tester, [movieSummary(id: 1), movieSummary(id: 3, title: 'Siguiente')]);

      expect(created, isEmpty);
      expect(find.byType(MoviePreviewVideo), findsNothing);
    });

    testWidgets('si el video falla, la tarjeta se queda con el póster y el resto sigue funcionando', (tester) async {
      final opened = <int>[];
      final created = <FakeVideoController>[];
      await pumpCarousel(tester, [withVideo], onOpen: (m) => opened.add(m.id), videoFactory: (_) {
        final c = FakeVideoController(failInit: true);
        created.add(c);
        return c;
      });
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const Key('preview-video')), findsNothing);
      expect(find.byKey(const Key('preview-poster')), findsOneWidget);
      expect(find.text('Verity'), findsOneWidget);
      await tester.tap(find.byKey(const Key('movie-showtimes-button')));
      expect(opened, [2]);
      expect(tester.takeException(), isNull);
    });

    testWidgets('mientras el video carga se ve el póster con el indicador, y el carrusel se puede arrastrar', (tester) async {
      final gate = Completer<void>();
      final created = await pumpWithVideos(tester, [withVideo, movieSummary(id: 3, title: 'Siguiente')], make: () => FakeVideoController(gate: gate));
      expect(find.byKey(const Key('preview-loading')), findsOneWidget);

      await swipeTo(tester, 'Siguiente'); // se va antes de que el video termine de cargar
      gate.complete();
      await tester.pump();
      await tester.pump();

      expect(created.single.disposed, isTrue, reason: 'el reproductor tardío no queda huérfano');
      expect(tester.takeException(), isNull);
    });

    testWidgets('cuando otra pantalla cubre Inicio (por ejemplo el detalle) el video se pausa, y al volver continúa', (tester) async {
      tester.view.physicalSize = const Size(390, 800);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final created = <FakeVideoController>[];
      Widget app({required bool covered}) => MaterialApp(
            home: Scaffold(
              body: TickerMode(
                enabled: !covered, // es lo que hace el Navigator con la ruta que quedó debajo
                child: DesignCanvas(height: kCarteleraContentHeight, children: [
                  Positioned.fill(
                    child: CarteleraCarousel(
                      movies: [withVideo],
                      onOpen: (_) {},
                      now: now,
                      videoControllerFactory: (_) => FakeVideoController().also(created.add),
                    ),
                  ),
                ]),
              ),
            ),
          );

      await tester.pumpWidget(app(covered: false));
      await tester.pump();
      await tester.pump();
      final c = created.single;
      expect(c.value.isPlaying, isTrue);

      await tester.pumpWidget(app(covered: true));
      await tester.pump();
      expect(c.value.isPlaying, isFalse);
      expect(c.disposed, isFalse, reason: 'solo se pausa: al volver no hay que cargarlo otra vez');

      await tester.pumpWidget(app(covered: false));
      await tester.pump();
      expect(c.value.isPlaying, isTrue);
    });

    testWidgets('si Inicio ya está cubierto cuando el video termina de cargar, queda en pausa hasta que vuelva', (tester) async {
      tester.view.physicalSize = const Size(390, 800);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final gate = Completer<void>();
      final created = <FakeVideoController>[];
      await tester.pumpWidget(MaterialApp(
        home: Scaffold(
          body: TickerMode(
            enabled: false,
            child: DesignCanvas(height: kCarteleraContentHeight, children: [
              Positioned.fill(
                child: CarteleraCarousel(
                  movies: [withVideo],
                  onOpen: (_) {},
                  now: now,
                  videoControllerFactory: (_) => FakeVideoController(gate: gate).also(created.add),
                ),
              ),
            ]),
          ),
        ),
      ));
      gate.complete();
      await tester.pump();
      await tester.pump();

      expect(created.single.value.isPlaying, isFalse);
    });
  });


  group('tráiler de YouTube en el carrusel', () {
    const url = 'https://www.youtube.com/watch?v=1McLG8FgNaI';
    final withTrailer = movieSummary(id: 2, title: 'Verity', trailerUrl: url, videoUrl: 'http://localhost/media/posters/verity.mp4', posterUrl: 'http://localhost/media/posters/verity.jpg');
    final others = [movieSummary(id: 1, title: 'Anterior'), withTrailer, movieSummary(id: 3, title: 'Siguiente')];

    /// Captura las órdenes de orientación y de barras del sistema que da la app.
    List<MethodCall> systemCalls(WidgetTester tester) {
      final calls = <MethodCall>[];
      tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(SystemChannels.platform, (call) async {
        calls.add(call);
        return null;
      });
      addTearDown(() => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(SystemChannels.platform, null));
      return calls;
    }

    Future<FakeTrailerPlayers> pump(WidgetTester tester, List<MovieSummary> movies, {FakeVideoController Function()? video, double position = 0}) async {
      final trailers = FakeTrailerPlayers(positionSeconds: position);
      await pumpCarousel(tester, movies, trailers: trailers, videoFactory: (_) => (video ?? FakeVideoController.new)());
      await tester.pump();
      await tester.pump();
      return trailers;
    }

    Future<void> tapPlay(WidgetTester tester) async {
      await tester.tap(find.byKey(const Key('movie-trailer-button-2')));
      await tester.pump();
    }

    testWidgets('solo las películas con un tráiler válido tienen botón de reproducir; el resto no deja un botón muerto', (tester) async {
      await pump(tester, [
        withTrailer,
        movieSummary(id: 3, title: 'Sin tráiler'),
        movieSummary(id: 4, title: 'Tráiler inválido', trailerUrl: 'https://www.youtube.com/watch?v=e'),
        movieSummary(id: 5, title: 'Otro sitio', trailerUrl: 'https://vimeo.com/123456'),
      ]);

      expect(find.byKey(const Key('movie-trailer-button-2')), findsOneWidget);
      expect(find.byKey(const Key('movie-trailer-button-3')), findsNothing);
      expect(find.byKey(const Key('movie-trailer-button-4')), findsNothing);
      expect(find.byKey(const Key('movie-trailer-button-5')), findsNothing);
    });

    testWidgets('al tocar play, el tráiler se reproduce DENTRO de la tarjeta, en el lugar del póster, y arranca solo', (tester) async {
      final trailers = await pump(tester, [withTrailer]);
      final poster = tester.getRect(find.byKey(const ValueKey('card-video-2')));
      final card = tester.getRect(find.byKey(const Key('movie-2')));

      await tapPlay(tester);

      final trailer = tester.getRect(find.byKey(const ValueKey('trailer-2')));
      expect(trailer.topLeft, poster.topLeft, reason: 'empieza donde estaba el póster');
      expect(trailer.left, greaterThan(card.left));
      expect(trailer.right, lessThan(card.right));
      expect(trailers.created, hasLength(1));
      expect(trailers.created.single.videoId, '1McLG8FgNaI');
      expect(trailers.created.single.autoPlay, isTrue);
      expect(find.byKey(const Key('trailer-fullscreen-close')), findsNothing, reason: 'no se abre la pantalla completa: sigue en el carrusel');
      expect(find.byKey(const ValueKey('card-video-2')), findsNothing, reason: 'el video corto cede el lugar al tráiler');
      expect(find.byKey(const Key('movie-trailer-button-2')), findsNothing, reason: 'ya está reproduciéndose');
      expect(find.byKey(const Key('movie-video-length')), findsNothing, reason: 'el contador no se dibuja encima del tráiler');
    });

    testWidgets('el reproductor mide al menos 200 × 200 (el mínimo de YouTube); la zona del póster crece de 140 a 200 de alto mientras suena', (tester) async {
      await pump(tester, [withTrailer]);
      final before = tester.getSize(find.byKey(const ValueKey('card-video-2')));
      expect(before.height, closeTo(140, 1), reason: 'el póster mide 261 × 140: por debajo del mínimo');

      await tapPlay(tester);

      final size = tester.getSize(find.byKey(const ValueKey('trailer-2')));
      expect(size.width, greaterThanOrEqualTo(200));
      expect(size.height, greaterThanOrEqualTo(200));
      expect(size.width, closeTo(261, 1));
    });

    testWidgets('NADA se superpone al reproductor: el botón de cerrar queda fuera (debajo) y dentro de la tarjeta', (tester) async {
      await pump(tester, [withTrailer]);
      await tapPlay(tester);

      final player = tester.getRect(find.byKey(const ValueKey('trailer-2')));
      final close = tester.getRect(find.byKey(const Key('trailer-close')));
      final card = tester.getRect(find.byKey(const Key('movie-2')));
      expect(player.overlaps(close), isFalse, reason: 'sus condiciones prohíben elementos delante de cualquier parte del reproductor');
      expect(close.top, greaterThanOrEqualTo(player.bottom));
      expect(card.contains(close.center), isTrue);
      // Tampoco el disco de play ni el contador de duración están dibujados sobre él.
      expect(find.byKey(const Key('movie-trailer-button-2')), findsNothing);
      expect(find.byKey(const Key('movie-video-length')), findsNothing);
    });

    testWidgets('los dos botones (pantalla completa y cerrar) están FUERA del reproductor, debajo de él y dentro de la tarjeta, sin tocarse', (tester) async {
      await pump(tester, [withTrailer]);
      await tapPlay(tester);

      final player = tester.getRect(find.byKey(const ValueKey('trailer-2')));
      final maximize = tester.getRect(find.byKey(const Key('trailer-maximize')));
      final close = tester.getRect(find.byKey(const Key('trailer-close')));
      final card = tester.getRect(find.byKey(const Key('movie-2')));
      for (final button in [maximize, close]) {
        expect(player.overlaps(button), isFalse);
        expect(button.top, greaterThanOrEqualTo(player.bottom));
        expect(card.contains(button.center), isTrue);
      }
      expect(maximize.overlaps(close), isFalse);
      expect(maximize.height, greaterThanOrEqualTo(32));
    });

    test('el reproductor no se modifica: solo parámetros documentados, con el botón de pantalla completa de YouTube apagado (fs=0) y sin anotaciones', () {
      expect(trailerPlayerParams.showFullscreenButton, isFalse, reason: 'la pantalla completa la ofrece la app con su propio botón; no se intercepta el de YouTube');
      expect(trailerPlayerParams.showControls, isTrue, reason: 'los controles de YouTube se dejan como son');
      expect(trailerPlayerParams.showVideoAnnotations, isFalse);
      expect(trailerPlayerParams.strictRelatedVideos, isTrue);
      expect(trailerPlayerParams.privacyEnhancedMode, isTrue);
      expect(trailerPlayerParams.mute, isFalse);
    });

    testWidgets('con la X el tráiler se cierra y la tarjeta queda como estaba (video corto y botón de play), sin tocar el carrusel', (tester) async {
      final trailers = await pump(tester, others);
      await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(-300, 0));
      await tester.pumpAndSettle();
      await tester.pump();
      await tester.pump();
      await tapPlay(tester);
      final cardBefore = tester.getRect(find.byKey(const Key('movie-2')));

      await tester.tap(find.byKey(const Key('trailer-close')));
      await tester.pump();
      await tester.pump();

      expect(trailers.last.disposed, isTrue);
      expect(find.byKey(const ValueKey('trailer-2')), findsNothing);
      expect(find.byKey(const ValueKey('card-video-2')), findsOneWidget);
      expect(find.byKey(const Key('movie-trailer-button-2')), findsOneWidget);
      expect(tester.getRect(find.byKey(const Key('movie-2'))), cardBefore, reason: 'el carrusel sigue en la misma tarjeta, sin recargar');
    });

    testWidgets('al terminar el tráiler, la tarjeta vuelve a como estaba y se libera el reproductor', (tester) async {
      final trailers = await pump(tester, [withTrailer]);
      await tapPlay(tester);

      trailers.last.emit(TrailerStatus.ended);
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const ValueKey('trailer-2')), findsNothing);
      expect(find.byKey(const Key('movie-trailer-button-2')), findsOneWidget);
      expect(trailers.last.disposed, isTrue);
      expect(find.text('Verity'), findsOneWidget);
      expect(find.textContaining('No se pudo reproducir'), findsNothing, reason: 'terminar no es un error');
    });

    testWidgets('si el tráiler no se puede reproducir, vuelve a la tarjeta con un aviso y se puede reintentar', (tester) async {
      final trailers = await pump(tester, [withTrailer]);
      await tapPlay(tester);

      trailers.last.emit(TrailerStatus.error);
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const ValueKey('trailer-2')), findsNothing);
      expect(find.textContaining('No se pudo reproducir el tráiler'), findsOneWidget);
      expect(find.byKey(const Key('movie-trailer-button-2')), findsOneWidget);
    });

    testWidgets('el botón atrás del sistema cierra el tráiler y no sale de Inicio', (tester) async {
      final trailers = await pump(tester, [withTrailer]);
      await tapPlay(tester);

      await tester.binding.handlePopRoute();
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const ValueKey('trailer-2')), findsNothing);
      expect(trailers.last.disposed, isTrue);
      expect(find.text('Verity'), findsOneWidget, reason: 'Inicio sigue ahí');
    });

    testWidgets('pasar a otra tarjeta mientras suena cierra el tráiler, y al volver NO arranca solo otra vez', (tester) async {
      final trailers = await pump(tester, [withTrailer, movieSummary(id: 3, title: 'Siguiente')]);
      await tapPlay(tester);

      await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(-300, 0));
      await tester.pumpAndSettle();
      expect(trailers.alive, isEmpty, reason: 'no queda ningún WebView vivo');

      await tester.drag(find.byKey(const Key('cartelera-carousel')), const Offset(300, 0));
      await tester.pumpAndSettle();
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const ValueKey('trailer-2')), findsNothing);
      expect(trailers.created, hasLength(1), reason: 'no se creó otro reproductor');
      expect(find.byKey(const Key('movie-trailer-button-2')), findsOneWidget);
    });

    testWidgets('un segundo toque en play no abre otro reproductor', (tester) async {
      final trailers = await pump(tester, [withTrailer]);
      final onTap = tester.widget<GestureDetector>(find.byKey(const Key('movie-trailer-button-2'))).onTap!;

      onTap();
      onTap();
      await tester.pump();

      expect(trailers.created, hasLength(1));
    });

    testWidgets('tocar el play de una tarjeta vecina la centra y NO abre su tráiler', (tester) async {
      final trailers = await pump(tester, [movieSummary(id: 1, title: 'Primera'), withTrailer]);

      // El play de la vecina queda fuera de la pantalla (su centro está más allá del borde); se toca su recuadro visible.
      final peek = tester.getRect(find.byKey(const Key('movie-2')));
      await tester.tapAt(Offset(peek.left + 12, peek.center.dy));
      await tester.pumpAndSettle();

      expect(trailers.created, isEmpty);
      expect(tester.getRect(find.byKey(const Key('movie-2'))).center.dx, closeTo(195, 1));
    });

    group('pantalla completa (botón propio, fuera del reproductor)', () {
      testWidgets('el botón de pantalla completa (propio, debajo del reproductor) abre una pantalla aparte en horizontal, desde el segundo en que iba, y pausa la de la tarjeta', (tester) async {
        final calls = systemCalls(tester);
        final trailers = await pump(tester, [withTrailer], position: 42.5);
        await tapPlay(tester);
        final inline = trailers.last;

        await tester.tap(find.byKey(const Key('trailer-maximize')));
        await tester.pumpAndSettle();

        expect(find.byKey(const Key('trailer-fullscreen-close')), findsOneWidget);
        final fullscreen = trailers.last;
        expect(fullscreen, isNot(same(inline)));
        expect(fullscreen.startSeconds, 42.5, reason: 'continúa donde iba');
        expect(fullscreen.autoPlay, isTrue);
        expect(inline.pauseCalls, 1, reason: 'no deben sonar dos a la vez');
        final orientation = calls.firstWhere((c) => c.method == 'SystemChrome.setPreferredOrientations').arguments as List;
        expect(orientation, ['DeviceOrientation.landscapeLeft', 'DeviceOrientation.landscapeRight']);
        expect(calls.any((c) => c.method == 'SystemChrome.setEnabledSystemUIMode' && (c.arguments as String).contains('immersiveSticky')), isTrue);
      });

      testWidgets('en pantalla completa tampoco se superpone nada: la X queda en una franja fuera del video, que mide más de 200 × 200', (tester) async {
        await pump(tester, [withTrailer]);
        await tapPlay(tester);
        await tester.tap(find.byKey(const Key('trailer-maximize')));
        tester.view.physicalSize = const Size(800, 360); // el teléfono ya girado
        await tester.pumpAndSettle();

        final video = tester.getRect(find.byKey(const Key('trailer-video-box')));
        final close = tester.getRect(find.byKey(const Key('trailer-fullscreen-close')));
        expect(video.overlaps(close), isFalse);
        expect(video.width, greaterThanOrEqualTo(200));
        expect(video.height, greaterThanOrEqualTo(200));
      });

      testWidgets('con la X vuelve a la tarjeta: se devuelve la orientación y las barras, y el tráiler de la tarjeta queda en pausa donde lo dejó', (tester) async {
        final calls = systemCalls(tester);
        final trailers = await pump(tester, [withTrailer], position: 30);
        await tapPlay(tester);
        final inline = trailers.last;
        final cardBefore = tester.getRect(find.byKey(const Key('movie-2')));
        await tester.tap(find.byKey(const Key('trailer-maximize')));
        await tester.pumpAndSettle();
        calls.clear();

        await tester.tap(find.byKey(const Key('trailer-fullscreen-close')));
        await tester.pumpAndSettle();

        expect(find.byKey(const Key('trailer-fullscreen-close')), findsNothing);
        expect(find.byKey(const ValueKey('trailer-2')), findsOneWidget, reason: 'sigue el tráiler de la tarjeta');
        expect(inline.disposed, isFalse);
        expect(inline.seeks, [30], reason: 'continúa desde el segundo en que se salió');
        expect(inline.pauseCalls, 2);
        expect(trailers.created.where((p) => p.disposed).length, 1, reason: 'solo el de pantalla completa se liberó');
        expect(calls.firstWhere((c) => c.method == 'SystemChrome.setPreferredOrientations').arguments, <dynamic>[], reason: 'se quita el bloqueo horizontal');
        expect(calls.any((c) => c.method == 'SystemChrome.setEnabledSystemUIMode' && (c.arguments as String).contains('edgeToEdge')), isTrue);
        expect(tester.getRect(find.byKey(const Key('movie-2'))), cardBefore, reason: 'Inicio no se movió ni se recargó');
      });

      testWidgets('el botón atrás del sistema sale de la pantalla completa hacia la tarjeta (no cierra el tráiler de la tarjeta)', (tester) async {
        final trailers = await pump(tester, [withTrailer], position: 12);
        await tapPlay(tester);
        await tester.tap(find.byKey(const Key('trailer-maximize')));
        await tester.pumpAndSettle();

        await tester.binding.handlePopRoute();
        await tester.pumpAndSettle();

        expect(find.byKey(const Key('trailer-fullscreen-close')), findsNothing);
        expect(find.byKey(const ValueKey('trailer-2')), findsOneWidget);
        expect(trailers.created.first.seeks, [12]);
      });

      testWidgets('si el tráiler llega al final en pantalla completa, se cierra sola y la tarjeta vuelve a como estaba', (tester) async {
        final calls = systemCalls(tester);
        final trailers = await pump(tester, [withTrailer]);
        await tapPlay(tester);
        await tester.tap(find.byKey(const Key('trailer-maximize')));
        await tester.pumpAndSettle();
        calls.clear();

        trailers.last.emit(TrailerStatus.ended);
        await tester.pumpAndSettle();

        expect(find.byKey(const Key('trailer-fullscreen-close')), findsNothing);
        expect(find.byKey(const ValueKey('trailer-2')), findsNothing, reason: 'vio el tráiler completo: ya no hay nada que reanudar');
        expect(find.byKey(const Key('movie-trailer-button-2')), findsOneWidget);
        expect(find.byKey(const ValueKey('card-video-2')), findsOneWidget);
        expect(trailers.alive, isEmpty);
        expect(calls.any((c) => c.method == 'SystemChrome.setPreferredOrientations'), isTrue, reason: 'se devuelve la orientación');
      });

      testWidgets('si falla en pantalla completa, vuelve a la tarjeta con un aviso', (tester) async {
        final trailers = await pump(tester, [withTrailer]);
        await tapPlay(tester);
        await tester.tap(find.byKey(const Key('trailer-maximize')));
        await tester.pumpAndSettle();

        trailers.last.emit(TrailerStatus.error);
        await tester.pumpAndSettle();

        expect(find.byKey(const Key('trailer-fullscreen-close')), findsNothing);
        expect(find.textContaining('No se pudo reproducir el tráiler'), findsOneWidget);
      });

      testWidgets('mientras está maximizado, lo que pase en el reproductor de la tarjeta no cierra nada', (tester) async {
        final trailers = await pump(tester, [withTrailer]);
        await tapPlay(tester);
        final inline = trailers.last;
        await tester.tap(find.byKey(const Key('trailer-maximize')));
        await tester.pumpAndSettle();

        inline.emit(TrailerStatus.ended); // por ejemplo, un evento tardío de la vista pausada
        await tester.pumpAndSettle();

        expect(find.byKey(const Key('trailer-fullscreen-close')), findsOneWidget);
        expect(tester.takeException(), isNull);
      });
    });
  });

  group('pestañas', () {
    Future<void> pumpTabs(WidgetTester tester, MovieStatus selected, ValueChanged<MovieStatus> onSelected) async {
      tester.view.physicalSize = const Size(390, 300);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(MaterialApp(
        home: Scaffold(body: DesignCanvas(height: 100, children: [Positioned.fill(child: CarteleraTabs(selected: selected, onSelected: onSelected))])),
      ));
    }

    testWidgets('cada pestaña avisa cuál se tocó', (tester) async {
      final tapped = <MovieStatus>[];
      await pumpTabs(tester, MovieStatus.nowShowing, tapped.add);

      await tester.tap(find.byKey(const Key('tab-presale')));
      await tester.tap(find.byKey(const Key('tab-comingSoon')));
      await tester.tap(find.byKey(const Key('tab-nowShowing')));

      expect(tapped, [MovieStatus.preSale, MovieStatus.comingSoon, MovieStatus.nowShowing]);
    });

    testWidgets('la pestaña elegida se anuncia como seleccionada', (tester) async {
      final handle = tester.ensureSemantics();
      await pumpTabs(tester, MovieStatus.preSale, (_) {});

      expect(tester.getSemantics(find.bySemanticsLabel('Preventa')).getSemanticsData().flagsCollection.isSelected, Tristate.isTrue);
      expect(tester.getSemantics(find.bySemanticsLabel('Cartelera')).getSemanticsData().flagsCollection.isSelected, Tristate.isFalse);
      handle.dispose();
    });
  });
}

extension<T> on T {
  T also(void Function(T value) block) {
    block(this);
    return this;
  }
}
