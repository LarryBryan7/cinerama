import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/location/device_location.dart';
import '../../../core/location/geo.dart';
import '../../../core/theme/cine_tokens.dart';
import '../../../core/theme/figma_icons.dart';
import '../../../core/util/formatters.dart';
import '../../../core/widgets/cine_avatar.dart';
import '../data/movie_models.dart';
import 'cartelera_mock.dart';
import 'movie_preview_video.dart';
import 'trailer/trailer_fullscreen_page.dart';
import 'trailer/trailer_inline.dart';
import 'trailer/trailer_player.dart';
import 'trailer/youtube_trailer_player.dart';

/// Coordenada Y de Figma donde empieza el área de contenido (justo bajo la cabecera de 75 px).
const double kCarteleraTop = 75;

/// Alto del área de contenido: de y=75 (bajo la cabecera) a y=662 (sobre la barra inferior).
const double kCarteleraContentHeight = 587;

/// Coloca [child] con las coordenadas absolutas de Figma (frame de 390×736). Sin alto para los textos: el chip de
/// 11 px de Figma es más bajo que su línea y un alto fijo recortaría los descendentes.
Widget _at(double x, double y, {double? w, double? h, required Widget child}) =>
    Positioned(left: x, top: y - kCarteleraTop, width: w, height: h, child: child);

/// [ellipsis] solo para textos con datos reales (título, géneros, sinopsis). Las etiquetas fijas de Figma no se truncan:
/// su caja mide lo mismo que el texto y media décima de diferencia de métricas no debe producir puntos suspensivos.
Widget _text(String data, TextStyle style, {TextAlign align = TextAlign.left, int maxLines = 1, bool ellipsis = false}) => Text(
      data,
      style: style,
      textAlign: align,
      maxLines: maxLines,
      overflow: ellipsis ? TextOverflow.ellipsis : TextOverflow.visible,
      softWrap: maxLines > 1,
    );

/// Imagen de la película: la real si el backend trae `posterUrl`; si no, el póster de muestra.
Widget _poster(String? url, String mockAsset) {
  final mock = Image.asset(mockAsset, fit: BoxFit.cover, width: double.infinity, height: double.infinity);
  if (url == null) return mock;
  return Image.network(
    url,
    fit: BoxFit.cover,
    width: double.infinity,
    height: double.infinity,
    errorBuilder: (_, _, _) => mock,
  );
}

class CarteleraTabs extends StatelessWidget {
  const CarteleraTabs({super.key, this.selected = MovieStatus.nowShowing, this.onSelected});

  final MovieStatus selected;
  final ValueChanged<MovieStatus>? onSelected;

  @override
  Widget build(BuildContext context) => Stack(
        clipBehavior: Clip.none,
        children: [
          _TabChip(status: MovieStatus.nowShowing, x: 38, w: 98, label: 'Cartelera', textLeft: 18, textWidth: 61, selected: selected, onSelected: onSelected),
          _TabChip(status: MovieStatus.preSale, x: 141, w: 89, label: 'Preventa', textLeft: 16, textWidth: 57, selected: selected, onSelected: onSelected),
          _TabChip(status: MovieStatus.comingSoon, x: 235, w: 116, label: 'Próximamente', textLeft: 14, textWidth: 92, selected: selected, onSelected: onSelected),
        ],
      );
}

class _TabChip extends StatelessWidget {
  const _TabChip({
    required this.status,
    required this.x,
    required this.w,
    required this.label,
    required this.textLeft,
    required this.textWidth,
    required this.selected,
    required this.onSelected,
  });

  final MovieStatus status;
  final double x;
  final double w;
  final String label;
  final double textLeft;
  final double textWidth;
  final MovieStatus selected;
  final ValueChanged<MovieStatus>? onSelected;

  bool get _isSelected => status == selected;

  @override
  Widget build(BuildContext context) => _at(
        x,
        95,
        w: w,
        h: 32,
        child: Semantics(
          button: true,
          selected: _isSelected,
          label: label,
          child: GestureDetector(
            key: Key('tab-${status.query}'),
            behavior: HitTestBehavior.opaque,
            onTap: onSelected == null ? null : () => onSelected!(status),
            child: DecoratedBox(
              decoration: BoxDecoration(
                color: _isSelected ? CineColors.tabSelected : CineColors.tabUnselected,
                borderRadius: BorderRadius.circular(20),
              ),
              child: Stack(children: [
                Positioned(
                  left: textLeft,
                  top: 8,
                  width: textWidth,
                  child: ExcludeSemantics(child: _text(label, cineText(13, FontWeight.w500, _isSelected ? CineColors.textOnLime : CineColors.textTab), align: TextAlign.center)),
                ),
              ]),
            ),
          ),
        ),
      );
}

/// Distancia (en km) hasta la que un cine se considera "cerca de ti".
const double kNearbyKm = 25;

/// Ancho de cada página del carrusel (px de Figma): la tarjeta de 284 px más 19 px de aire. Con 390 px de pantalla, la
/// tarjeta activa queda centrada (x = 53..337) y las vecinas asoman unos 35 px a cada lado, como en el diseño.
const double _kPageWidth = 303;
const double _kCardWidth = 284;

/// El tráiler de YouTube mientras suena: 261 × 200, en el lugar del póster pero más alto (el mínimo que exige YouTube es 200 × 200).
const double kTrailerLeft = 62;
const double kTrailerTop = 155;
const double kTrailerWidth = 261;
const double kTrailerHeight = 200;
const double _kCardHeight = 328;

/// Coordenada Y de Figma donde empieza la tarjeta, y holgura para su sombra.
const double _kCardTop = 142;
const double _kShadowPad = 8;

/// Qué cines mostrar de una película y a qué distancia: los de la ciudad elegida, del más cercano al más lejano si se
/// conoce la ubicación del dispositivo (o por nombre si no).
class NearbyCinemas {
  NearbyCinemas._(this.cinemas, this._km);

  factory NearbyCinemas.of(MovieSummary movie, {String? city, GeoPoint? position}) {
    final inCity = city == null ? movie.cinemas : movie.cinemas.where((c) => c.city == city).toList();
    final list = [...(inCity.isEmpty ? movie.cinemas : inCity)];
    final km = <int, double>{};
    if (position != null) {
      for (final c in list) {
        km[c.id] = Geo.distanceKm(position, c.latitude, c.longitude);
      }
      list.sort((a, b) => km[a.id]!.compareTo(km[b.id]!));
    } else {
      list.sort((a, b) => a.name.compareTo(b.name));
    }
    return NearbyCinemas._(list, km);
  }

  final List<CinemaRef> cinemas;
  final Map<int, double> _km;

  /// Distancia en km, o `null` si no se conoce la ubicación del dispositivo.
  double? kmTo(CinemaRef cinema) => _km[cinema.id];

  /// Segunda línea de cada cine: la distancia ("A 1.1 km cerca de ti") si se conoce la ubicación; si no, la dirección.
  String detailOf(CinemaRef cinema) {
    final km = kmTo(cinema);
    if (km == null) return cinema.address;
    return km <= kNearbyKm ? 'A ${Formatters.distance(km)} cerca de ti' : 'A ${Formatters.distance(km)}';
  }
}

/// Carrusel de tarjetas de la cartelera (Figma: "resumen_card"). Cada tarjeta es una página: al arrastrar, la tarjeta
/// activa se desplaza con el dedo, las vecinas asoman a los lados con menos opacidad y tamaño, y al soltar se ajusta a la
/// más cercana. Tocar una vecina la centra; tocar la activa abre el detalle. Debajo, los cines y el botón de la película activa.
///
/// Se coloca dentro de un `DesignCanvas` de [kCarteleraContentHeight] de alto. Para volver a la primera tarjeta (otra
/// pestaña u otra ciudad) se le cambia la `key`.
class CarteleraCarousel extends StatefulWidget {
  const CarteleraCarousel({
    super.key,
    required this.movies,
    required this.onOpen,
    this.onOpenReviews,
    this.onNearEnd,
    this.cityName,
    this.position,
    this.now,
    this.videoControllerFactory,
    this.trailerPlayerFactory = youtubeTrailerPlayerFactory,
  });

  final List<MovieSummary> movies;
  final ValueChanged<MovieSummary> onOpen;
  final ValueChanged<MovieSummary>? onOpenReviews;

  /// Quedan pocas tarjetas por ver: es el momento de pedir la página siguiente.
  final VoidCallback? onNearEnd;

  /// Ciudad elegida y ubicación del dispositivo (para "cerca de ti" y las distancias).
  final String? cityName;
  final GeoPoint? position;

  /// Hora local para etiquetar la opinión ("Hoy", "Ayer"). Se inyecta en las pruebas.
  final DateTime? now;

  /// Cómo se crea el reproductor del video de la tarjeta activa. Se inyecta en las pruebas; en la app es un video de red.
  final VideoControllerFactory? videoControllerFactory;

  /// Cómo se crea el reproductor del tráiler (YouTube). Se inyecta en las pruebas.
  final TrailerPlayerFactory trailerPlayerFactory;

  @override
  State<CarteleraCarousel> createState() => _CarteleraCarouselState();
}

class _CarteleraCarouselState extends State<CarteleraCarousel> {
  static const _prefetchMargin = 3; // cuando quedan 3 tarjetas por ver, se pide la página siguiente

  final _controller = PageController(viewportFraction: _kPageWidth / 390);
  var _index = 0;

  /// Película cuyo tráiler se está reproduciendo en su tarjeta (solo puede ser la activa), o `null`. El carrusel es dueño del
  /// reproductor: lo crea al tocar play y lo libera al cerrar, terminar, fallar o cambiar de tarjeta.
  int? _trailerMovieId;
  TrailerPlayer? _trailer;
  String? _trailerVideoId;
  StreamSubscription<TrailerStatus>? _trailerSubscription;
  var _trailerInFullscreen = false;

  @override
  void dispose() {
    _releaseTrailer();
    _controller.dispose();
    super.dispose();
  }

  void _onPageChanged(int index) {
    // Al pasar a otra tarjeta, el tráiler de la anterior se cierra (se libera su WebView) y volver no lo reanuda solo.
    setState(() {
      _index = index;
      _releaseTrailer();
    });
    if (index >= widget.movies.length - _prefetchMargin) widget.onNearEnd?.call();
  }

  /// Abre el tráiler en la propia tarjeta: la zona del póster crece de 261 × 140 a 261 × 200 mientras suena (YouTube exige un
  /// reproductor de al menos 200 × 200). Solo una tarjeta a la vez, y un segundo toque no abre otro.
  void _playTrailer(MovieSummary movie) {
    final videoId = movie.trailerVideoId;
    if (videoId == null || _trailerMovieId != null) return;
    final player = widget.trailerPlayerFactory(videoId, startSeconds: 0, autoPlay: true);
    _trailer = player;
    _trailerVideoId = videoId;
    _trailerSubscription = player.status.listen((status) {
      if (_trailerInFullscreen) return; // mientras está en pantalla completa, lo que pase en esta vista no cuenta
      if (status == TrailerStatus.ended) _closeTrailer();
      if (status == TrailerStatus.error) _closeTrailer(failed: true);
    });
    setState(() => _trailerMovieId = movie.id);
  }

  /// Libera el reproductor del tráiler (sin tocar el estado de la pantalla). Se puede llamar aunque no haya ninguno.
  void _releaseTrailer() {
    unawaited(_trailerSubscription?.cancel());
    unawaited(_trailer?.dispose());
    _trailerSubscription = null;
    _trailer = null;
    _trailerVideoId = null;
    _trailerMovieId = null;
  }

  /// Cierra el tráiler (con el botón, al terminar o si falla; en ese caso, con un aviso). La tarjeta vuelve a como estaba.
  void _closeTrailer({bool failed = false}) {
    if (!mounted || _trailerMovieId == null) return;
    setState(_releaseTrailer);
    if (failed) {
      ScaffoldMessenger.maybeOf(context)
        ?..hideCurrentSnackBar()
        ..showSnackBar(const SnackBar(content: Text('No se pudo reproducir el tráiler. Inténtalo más tarde.')));
    }
  }

  /// Pantalla completa, en horizontal: una pantalla aparte con un segundo reproductor que sigue desde el segundo en que iba. El de
  /// la tarjeta se pausa (no deben sonar dos a la vez). Con la X o atrás se vuelve a la tarjeta, con el tráiler en pausa donde se
  /// dejó; si el video termina, Inicio queda como estaba.
  Future<void> _maximizeTrailer() async {
    final player = _trailer;
    final videoId = _trailerVideoId;
    if (player == null || videoId == null || _trailerInFullscreen) return;
    _trailerInFullscreen = true;
    final seconds = await player.position().catchError((_) => 0.0);
    await player.pause();
    if (!mounted) return;

    final result = await TrailerFullscreenPage.open(context, videoId: videoId, startSeconds: seconds, playerFactory: widget.trailerPlayerFactory);
    _trailerInFullscreen = false;
    if (!mounted || _trailer != player) return; // se cerró mientras tanto

    switch (result) {
      case TrailerEnded():
        _closeTrailer(); // vio el tráiler completo: Inicio vuelve a como estaba
      case TrailerFailed():
        _closeTrailer(failed: true);
      case TrailerClosed(:final seconds):
        await player.seekTo(seconds);
        await player.pause();
    }
  }

  void _onTapCard(int index) {
    if (index == _index) {
      widget.onOpen(widget.movies[index]);
    } else {
      // Tocar una tarjeta vecina la trae al centro (no abre nada).
      unawaited(_controller.animateToPage(index, duration: const Duration(milliseconds: 300), curve: Curves.easeOut));
    }
  }

  /// Posición continua del carrusel (por ejemplo 1.4 = a 40 % entre la segunda y la tercera tarjeta).
  double get _page => _controller.hasClients && _controller.position.haveDimensions ? (_controller.page ?? _index.toDouble()) : _index.toDouble();

  @override
  Widget build(BuildContext context) {
    final movies = widget.movies;
    final current = movies[_index.clamp(0, movies.length - 1)];
    final clock = widget.now ?? DateTime.now();
    // Con un tráiler en la tarjeta, el botón atrás lo cierra (y no sale de Inicio).
    return PopScope(
      canPop: _trailerMovieId == null,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) _closeTrailer();
      },
      child: Stack(clipBehavior: Clip.none, children: [
      Positioned(
        left: 0,
        top: _kCardTop - kCarteleraTop - _kShadowPad,
        width: 390,
        height: _kCardHeight + 2 * _kShadowPad,
        child: PageView.builder(
          key: const Key('cartelera-carousel'),
          controller: _controller,
          itemCount: movies.length,
          onPageChanged: _onPageChanged,
          clipBehavior: Clip.none,
          itemBuilder: (context, i) => AnimatedBuilder(
            animation: _controller,
            // Cuanto más lejos del centro, más pequeña y transparente (Figma: 70 % y 65 % de opacidad en las vecinas).
            builder: (context, child) {
              final distance = (_page - i).abs().clamp(0.0, 1.0);
              return Opacity(opacity: 1 - .35 * distance, child: Transform.scale(scale: 1 - .05 * distance, child: child));
            },
            child: _CarteleraCard(
              movie: movies[i],
              index: i,
              now: clock,
              active: i == _index,
              videoControllerFactory: widget.videoControllerFactory,
              trailerPlayer: _trailerMovieId == movies[i].id && i == _index ? _trailer : null,
              onPlayTrailer: () => _playTrailer(movies[i]),
              onCloseTrailer: _closeTrailer,
              onMaximizeTrailer: () => unawaited(_maximizeTrailer()),
              onTap: () => _onTapCard(i),
              onOpenReviews: widget.onOpenReviews == null ? null : () => widget.onOpenReviews!(movies[i]),
            ),
          ),
        ),
      ),
      Positioned.fill(
        child: AnimatedSwitcher(
          duration: const Duration(milliseconds: 200),
          layoutBuilder: (current, previous) => Stack(clipBehavior: Clip.none, alignment: Alignment.topLeft, children: [...previous, ?current]),
          child: _CarteleraFooter(
            key: ValueKey(current.id),
            movie: current,
            cityName: widget.cityName,
            position: widget.position,
            onOpen: () => widget.onOpen(current),
          ),
        ),
      ),
    ]),
    );
  }
}

/// Coloca [child] con las coordenadas de Figma dentro de una página del carrusel: x e y son las del archivo y se
/// reubican respecto de la página (la tarjeta queda centrada en ella) y de su holgura superior.
Widget _inCard(double x, double y, {double? w, double? h, double? bottom, required Widget child}) => Positioned(
      left: x - (53 - (_kPageWidth - _kCardWidth) / 2),
      top: bottom == null ? y - _kCardTop + _kShadowPad : null,
      bottom: bottom,
      width: w,
      height: h,
      child: child,
    );

/// Una tarjeta del carrusel: póster, título, sinopsis, opinión destacada y botón de opiniones.
/// Todo sale de los datos de [movie] (servidor); solo el póster tiene una imagen de respaldo.
class _CarteleraCard extends StatelessWidget {
  const _CarteleraCard({required this.movie, required this.index, required this.now, required this.active, required this.onTap, required this.trailerPlayer, required this.onPlayTrailer, required this.onCloseTrailer, required this.onMaximizeTrailer, this.onOpenReviews, this.videoControllerFactory});

  final MovieSummary movie;
  final int index;
  final DateTime now;

  /// Es la tarjeta del centro. Solo la activa reproduce su video: al llegar a ella arranca y al irse se libera.
  final bool active;
  final VideoControllerFactory? videoControllerFactory;

  /// El reproductor del tráiler de esta tarjeta, o `null` si no se está reproduciendo.
  final TrailerPlayer? trailerPlayer;

  /// Tocar el botón de play: abre el tráiler en la tarjeta. [onCloseTrailer] lo cierra (la X, el final del video o un error).
  final VoidCallback onPlayTrailer;
  final void Function({bool failed}) onCloseTrailer;
  final VoidCallback onMaximizeTrailer;
  final VoidCallback onTap;
  final VoidCallback? onOpenReviews;

  String get _subtitle => [
        movie.genres.join(', '),
        Formatters.durationCompact(movie.durationMinutes),
        movie.classification,
      ].where((part) => part.isNotEmpty).join(' | ');

  String get _statusBadge => switch (movie.status) {
        MovieStatus.preSale => movie.releaseDate == null ? 'Preventa' : 'Preventa · estreno ${Formatters.shortDate(movie.releaseDate!)}',
        MovieStatus.comingSoon => movie.releaseDate == null ? 'Próximamente' : 'Próximamente · ${Formatters.shortDate(movie.releaseDate!)}',
        MovieStatus.nowShowing => '',
      };

  /// El botón abre la hoja de comentarios, que muestra solo los del tráiler en YouTube. No lleva la cuenta de opiniones de la app:
  /// serían de otra fuente y se leería como si fueran lo que muestra la hoja (YouTube pide no mezclar sus resultados con otros).
  String get _reviewsLabel => 'Ver \ncomentarios';

  bool get trailerPlaying => trailerPlayer != null;

  @override
  Widget build(BuildContext context) {
    final review = movie.featuredReview;
    return SizedBox(
      width: _kPageWidth,
      child: Stack(clipBehavior: Clip.none, children: [
        // Tarjeta (resumen_card).
        _inCard(
          53,
          142,
          w: _kCardWidth,
          h: _kCardHeight,
          child: DecoratedBox(
            decoration: BoxDecoration(
              color: CineColors.surface,
              borderRadius: BorderRadius.circular(20),
              border: Border.all(color: CineColors.cardStroke, width: 1.2),
              boxShadow: const [BoxShadow(color: CineColors.cardShadow, offset: Offset(0, 4), blurRadius: 4)],
            ),
          ),
        ),
        _inCard(
          62,
          155,
          w: 261,
          h: 140,
          child: ClipRRect(
            borderRadius: const BorderRadius.only(topLeft: Radius.circular(15), topRight: Radius.circular(15)),
            // En este espacio va el video corto de la tarjeta activa (sin ninguna marca) o el póster. Mientras suena el tráiler,
            // el póster (el tráiler se dibuja aparte, más grande).
            child: trailerPlaying
                ? _poster(movie.posterUrl, CarteleraMock.posterFor(index))
                : active && movie.previewVideoUrl != null
                ? MoviePreviewVideo(
                    key: ValueKey('card-video-${movie.id}'),
                    videoUrl: movie.previewVideoUrl!,
                    posterUrl: movie.posterUrl,
                    fill: true,
                    controllerFactory: videoControllerFactory ?? defaultVideoControllerFactory,
                  )
                : _poster(movie.posterUrl, CarteleraMock.posterFor(index)),
          ),
        ),
        // Insignia de estado (no está en Figma): solo para preventa y próximamente, con la fecha de estreno.
        if (movie.status != MovieStatus.nowShowing)
          _inCard(
            72,
            164,
            child: DecoratedBox(
              key: const Key('movie-status-badge'),
              decoration: BoxDecoration(color: CineColors.tabSelected, borderRadius: BorderRadius.circular(8)),
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                child: Text(_statusBadge, style: cineText(11, FontWeight.w600, CineColors.textOnLime)),
              ),
            ),
          ),
        // Contador del video (maqueta: ver CarteleraMock.videoLength). Solo en las películas con video o tráiler.
        if ((movie.previewVideoUrl != null || movie.trailerUrl != null) && !trailerPlaying)
          _inCard(
            70,
            270,
            w: 42,
            h: 16,
            child: DecoratedBox(
              key: const Key('movie-video-length'),
              decoration: BoxDecoration(color: CineColors.videoPill, borderRadius: BorderRadius.circular(5)),
              child: Center(child: Text(CarteleraMock.videoLength, style: cineText(10, FontWeight.w400, CineColors.white))),
            ),
          ),
        // Botón de reproducir el tráiler (Figma). Solo existe si la película tiene un tráiler de YouTube válido.
        if (movie.trailerVideoId != null && !trailerPlaying) ...[
          _inCard(179, 211, w: 28, h: 28, child: const _GlassDisc()),
          _inCard(187, 218, child: FigmaIcon(FigmaIconData.play)),
        ],
        _inCard(
          280,
          302,
          w: 30,
          child: Semantics(
            label: movie.averageRating == null ? 'Sin calificación' : 'Calificación ${movie.averageRating!.toStringAsFixed(1)} de 5',
            child: ExcludeSemantics(
              child: _text(movie.averageRating?.toStringAsFixed(1) ?? '–', cineText(12, FontWeight.w500, CineColors.white90), align: TextAlign.right),
            ),
          ),
        ),
        _inCard(311, 303, child: FigmaIcon(FigmaIconData.star)),
        _inCard(63, 301, w: 214, child: _text(movie.title, cineText(13, FontWeight.w700, CineColors.white), ellipsis: true)),
        _inCard(63, 323.35, w: 223.14, child: _text(_subtitle, cineText(12, FontWeight.w400, CineColors.grey), ellipsis: true)),
        _inCard(64.09, 344.59, w: 245.86, h: 1.12, child: const ColoredBox(color: CineColors.divider)),
        _inCard(64, 350, w: _SynopsisWithLink.width, child: _SynopsisWithLink(text: movie.synopsis ?? '')),

        // El tráiler, en el espacio del póster pero más alto (261 × 200): YouTube exige un reproductor de al menos 200 × 200. Crece
        // hacia abajo y tapa el título y parte de la sinopsis mientras suena; al cerrarlo la tarjeta vuelve a verse entera. El
        // reproductor real se dibuja en la capa superior de la app, sobre este recuadro.
        if (trailerPlaying && movie.trailerVideoId != null)
          _inCard(
            kTrailerLeft,
            kTrailerTop,
            w: kTrailerWidth,
            h: kTrailerHeight,
            child: TrailerView(key: ValueKey('trailer-${movie.id}'), player: trailerPlayer!, aspectRatio: kTrailerWidth / kTrailerHeight),
          ),

        // Opinión destacada (la más reciente con comentario) y botón de opiniones.
        _inCard(
          64,
          406,
          w: 177,
          h: 50,
          child: DecoratedBox(
            decoration: BoxDecoration(
              color: CineColors.chip,
              borderRadius: const BorderRadius.only(topLeft: Radius.circular(10), topRight: Radius.circular(10), bottomRight: Radius.circular(10)),
              border: Border.all(color: CineColors.reviewStroke, width: .3),
            ),
          ),
        ),
        if (review != null) ...[
          _inCard(72, 411, w: 20, h: 20, child: CineAvatar(url: review.authorAvatarUrl, name: review.authorName)),
          _inCard(99, 414, w: 100, child: _text(review.authorName, cineText(11, FontWeight.w400, CineColors.textSoft), ellipsis: true)),
          _inCard(190, 411, w: 44, child: _text(Formatters.relativeDay(review.createdAt, now), cineText(10, FontWeight.w300, CineColors.textSoft), align: TextAlign.right, ellipsis: true)),
          _inCard(
            75,
            436,
            w: 160,
            child: _text(review.comment ?? '', cineText(11, FontWeight.w200, CineColors.textSoft, italic: true), ellipsis: true),
          ),
        ] else
          _inCard(
            75,
            422,
            w: 160,
            child: _text(
              movie.status == MovieStatus.nowShowing ? 'Aún no hay opiniones' : 'Podrás opinar desde el estreno',
              cineText(11, FontWeight.w200, CineColors.textSoft, italic: true),
              ellipsis: true,
            ),
          ),
        _inCard(
          252,
          406,
          w: 78,
          h: 50,
          child: DecoratedBox(
            decoration: BoxDecoration(
              color: CineColors.chip,
              borderRadius: BorderRadius.circular(8),
              border: Border.all(color: CineColors.reviewStroke, width: .6),
            ),
          ),
        ),
        _inCard(285, 411, child: FigmaIcon(FigmaIconData.arrowUpRight)),
        // Texto de dos líneas alineado al borde inferior de su caja (Figma: alineación vertical "abajo", y=432..451).
        _inCard(256, 0, w: 71, bottom: _kShadowPad + _kCardTop + _kCardHeight - 451, child: _text(_reviewsLabel, cineText(9, FontWeight.w300, CineColors.textSoft, underline: true), align: TextAlign.center, maxLines: 2)),

        // Toda la tarjeta abre el detalle (la activa) o se centra (una vecina).
        _inCard(
          53,
          142,
          w: _kCardWidth,
          h: _kCardHeight,
          child: Semantics(
            button: true,
            label: 'Ver ${movie.title}',
            child: GestureDetector(key: Key('movie-${movie.id}'), behavior: HitTestBehavior.opaque, onTap: onTap, child: const SizedBox.expand()),
          ),
        ),
        // Pantalla completa y cerrar: van FUERA del reproductor (debajo de él). YouTube prohíbe elementos delante de cualquier parte del
        // reproductor, y no se toca ninguno de sus botones: su botón de pantalla completa está apagado (fs=0) y este lo reemplaza.
        if (trailerPlaying && movie.trailerVideoId != null) ...[
          _inCard(kTrailerLeft, kTrailerTop + kTrailerHeight + 6, w: 146, h: 36,
              child: _TrailerButton(key: const Key('trailer-maximize'), icon: Icons.fullscreen, label: 'Pantalla completa', onTap: onMaximizeTrailer)),
          _inCard(kTrailerLeft + kTrailerWidth - 108, kTrailerTop + kTrailerHeight + 6, w: 108, h: 36,
              child: _TrailerButton(key: const Key('trailer-close'), icon: Icons.close, label: 'Cerrar', onTap: () => onCloseTrailer())),
        ],
        // Por encima de la tarjeta: tocar el botón de play abre el tráiler (en la tarjeta activa; en una vecina, la centra).
        if (movie.trailerVideoId != null && !trailerPlaying)
          _inCard(
            169,
            201,
            w: 48,
            h: 48,
            child: Semantics(
              button: true,
              label: 'Ver el tráiler de ${movie.title}',
              child: GestureDetector(
                key: Key('movie-trailer-button-${movie.id}'),
                behavior: HitTestBehavior.opaque,
                onTap: active ? onPlayTrailer : onTap,
                child: const SizedBox.expand(),
              ),
            ),
          ),
        // Por encima del área anterior: el botón de opiniones abre la lista de opiniones, no el detalle.
        _inCard(
          252,
          406,
          w: 78,
          h: 50,
          child: Semantics(
            button: true,
            label: 'Opiniones de ${movie.title}',
            child: GestureDetector(key: Key('movie-reviews-button-${movie.id}'), behavior: HitTestBehavior.opaque, onTap: onOpenReviews, child: const SizedBox.expand()),
          ),
        ),
      ]),
    );
  }
}

/// Lo que va debajo del carrusel y cambia con la película activa: los cines donde se proyecta y el botón principal.
class _CarteleraFooter extends StatelessWidget {
  const _CarteleraFooter({super.key, required this.movie, required this.onOpen, this.cityName, this.position});

  final MovieSummary movie;
  final VoidCallback onOpen;
  final String? cityName;
  final GeoPoint? position;

  String get _buttonLabel => switch (movie.status) {
        MovieStatus.nowShowing => 'Ver Precios y Horarios',
        MovieStatus.preSale => 'Reservar en preventa',
        MovieStatus.comingSoon => 'Ver detalles',
      };

  @override
  Widget build(BuildContext context) {
    final nearby = NearbyCinemas.of(movie, city: cityName, position: position);
    final cinemas = nearby.cinemas;
    return Stack(clipBehavior: Clip.none, children: [
      // Cines con funciones (solo si los hay: una película "próximamente" no tiene).
      if (cinemas.isNotEmpty) ...[
        _at(75, 478, child: FigmaIcon(FigmaIconData.monitorPlay)),
        _at(
          98,
          481,
          w: 232,
          child: _text(
            'Película disponible en ${cinemas.length} ${cinemas.length == 1 ? 'cine' : 'cines'} cerca de ti',
            cineText(11, FontWeight.w400, CineColors.soft90, underline: true),
            ellipsis: true,
          ),
        ),

        // Chip con los dos cines más cercanos.
        _at(
          53,
          509,
          w: 286,
          h: 45,
          child: DecoratedBox(
            decoration: BoxDecoration(
              color: CineColors.chip,
              borderRadius: BorderRadius.circular(15),
              border: Border.all(color: CineColors.cinemaStroke, width: .5),
            ),
          ),
        ),
        ..._cinemaEntry(cinemas[0], nearby.detailOf(cinemas[0]), badgeX: 58, textX: 82, textW: 107),
        if (cinemas.length > 1) ...[
          _at(195, 520, w: .97, h: 31, child: const ColoredBox(color: CineColors.cinemaDivider)),
          ..._cinemaEntry(cinemas[1], nearby.detailOf(cinemas[1]), badgeX: 203, textX: 229, textW: 100),
        ],
      ],

      // Botón principal.
      _at(
        53,
        600,
        w: 292,
        h: 42,
        child: Semantics(
          button: true,
          label: _buttonLabel,
          child: GestureDetector(
            key: const Key('movie-showtimes-button'),
            behavior: HitTestBehavior.opaque,
            onTap: onOpen,
            child: Stack(clipBehavior: Clip.none, children: [
              Positioned.fill(child: DecoratedBox(decoration: BoxDecoration(color: CineColors.lime, borderRadius: BorderRadius.circular(20)))),
              Positioned(left: 54.93, top: 3.77, child: FigmaIcon(FigmaIconData.ticketButton)),
              Positioned(left: 93.07, top: 13, width: 155.13, child: _text(_buttonLabel, cineText(14, FontWeight.w600, CineColors.textOnLime))),
            ]),
          ),
        ),
      ),
    ]);
  }

  /// Un cine del chip: recuadro con el logo (fondo rojo) o con su inicial (fondo gris), nombre y distancia o dirección.
  List<Widget> _cinemaEntry(CinemaRef cinema, String detail, {required double badgeX, required double textX, required double textW}) => [
        _at(
          badgeX,
          519,
          w: 22,
          h: 24,
          child: DecoratedBox(decoration: BoxDecoration(color: cinema.logoUrl != null ? CineColors.red : CineColors.grey, borderRadius: BorderRadius.circular(8))),
        ),
        _at(
          badgeX + 1,
          520,
          w: 20,
          h: 21,
          child: cinema.logoUrl != null
              ? ClipRRect(
                  borderRadius: BorderRadius.circular(5),
                  child: Image.network(
                    cinema.logoUrl!,
                    fit: BoxFit.fill,
                    errorBuilder: (_, _, _) => Center(child: _text(cinema.initial, cineText(14, FontWeight.w600, CineColors.cinemaLetter), align: TextAlign.center)),
                  ),
                )
              : Center(child: _text(cinema.initial, cineText(14, FontWeight.w600, CineColors.cinemaLetter), align: TextAlign.center)),
        ),
        _at(textX, 517, w: textW, child: _text(cinema.name, cineText(10, FontWeight.w600, CineColors.textSoft), ellipsis: true)),
        _at(textX, 531, w: textW, child: _text(detail, cineText(10, FontWeight.w200, CineColors.textSoft), ellipsis: true)),
      ];
}

/// Botón de reproducir (Figma: "Ellipse 1", 28 px, relleno #D9D9D9 al 50 % con efecto "Glass"). El efecto de vidrio de
/// Figma (refracción, dispersión, luz a -45°) no tiene equivalente en Flutter: se aproxima con el relleno translúcido y un
/// borde de luz de 1 px más intenso en las esquinas superior izquierda e inferior derecha.
class _GlassDisc extends StatelessWidget {
  const _GlassDisc();

  @override
  Widget build(BuildContext context) => const DecoratedBox(
        decoration: BoxDecoration(shape: BoxShape.circle, color: CineColors.glass),
        child: CustomPaint(painter: _GlassRimPainter()),
      );
}

class _GlassRimPainter extends CustomPainter {
  const _GlassRimPainter();

  @override
  void paint(Canvas canvas, Size size) {
    final rect = Offset.zero & size;
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..isAntiAlias = true
      ..shader = const LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [Color.fromRGBO(255, 255, 255, .8), Color.fromRGBO(255, 255, 255, 0), Color.fromRGBO(255, 255, 255, .45)],
        stops: [0, .5, 1],
      ).createShader(rect);
    canvas.drawCircle(rect.center, size.width / 2 - .5, paint);
  }

  @override
  bool shouldRepaint(_GlassRimPainter oldDelegate) => false;
}

/// Sinopsis recortada a tres líneas con el enlace "Ver más información" al final (Figma: texto Light 12 y enlace Regular 12
/// subrayado). Si la sinopsis cabe completa junto al enlace, se muestra entera; si no, se corta en una palabra y se agregan
/// puntos suspensivos antes del enlace. Tocar el enlace abre el detalle de la película (toda la tarjeta lo hace).
class _SynopsisWithLink extends StatelessWidget {
  const _SynopsisWithLink({required this.text});

  /// Ancho de la caja de Figma (x=64..323).
  static const width = 259.0;
  static const _maxLines = 3;
  static const _linkLabel = 'Ver más información';

  final String text;

  static final _body = cineText(12, FontWeight.w300, CineColors.textBody);
  static final _link = cineText(12, FontWeight.w400, CineColors.textBody, underline: true);

  /// ¿Caben [prefix] y el enlace en tres líneas?
  static bool _fits(String prefix) {
    final painter = TextPainter(
      text: TextSpan(children: [TextSpan(text: prefix, style: _body), TextSpan(text: _linkLabel, style: _link)]),
      textDirection: TextDirection.ltr,
      maxLines: _maxLines,
    )..layout(maxWidth: width);
    final fits = !painter.didExceedMaxLines;
    painter.dispose();
    return fits;
  }

  /// Parte de la sinopsis que se muestra antes del enlace, con su separador ("... " si hubo recorte, " " si no).
  static String visiblePart(String synopsis) {
    final full = synopsis.trim();
    if (full.isEmpty) return '';
    if (_fits('$full ')) return '$full ';
    var low = 0, high = full.length; // el mayor prefijo que cabe junto con "... " y el enlace
    while (low < high) {
      final mid = (low + high + 1) ~/ 2;
      if (_fits('${full.substring(0, mid).trimRight()}... ')) {
        low = mid;
      } else {
        high = mid - 1;
      }
    }
    var cut = full.substring(0, low);
    final space = cut.lastIndexOf(' ');
    if (space > 0) cut = cut.substring(0, space); // no se corta una palabra por la mitad
    return '${cut.trimRight()}... ';
  }

  @override
  Widget build(BuildContext context) => Text.rich(
        TextSpan(children: [TextSpan(text: visiblePart(text), style: _body), TextSpan(text: _linkLabel, style: _link)]),
        maxLines: _maxLines,
      );
}

/// Botón de la fila que va debajo del tráiler (fuera del reproductor): icono y texto sobre el fondo de los chips de la tarjeta.
class _TrailerButton extends StatelessWidget {
  const _TrailerButton({super.key, required this.icon, required this.label, required this.onTap});

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        label: label,
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: onTap,
          child: DecoratedBox(
            decoration: BoxDecoration(color: CineColors.chip, borderRadius: BorderRadius.circular(18), border: Border.all(color: CineColors.cardStroke, width: .6)),
            child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
              Icon(icon, size: 18, color: CineColors.white),
              const SizedBox(width: 4),
              Flexible(child: ExcludeSemantics(child: Text(label, softWrap: false, overflow: TextOverflow.ellipsis, style: cineText(12, FontWeight.w500, CineColors.white)))),
            ]),
          ),
        ),
      );
}
