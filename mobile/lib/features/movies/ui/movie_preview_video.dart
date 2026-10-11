import 'dart:async';

import 'package:flutter/material.dart';
import 'package:video_player/video_player.dart';

import '../../../core/theme/cine_tokens.dart';

/// Crea el controlador del video. Se inyecta en las pruebas; en la app es un video de red.
typedef VideoControllerFactory = VideoPlayerController Function(Uri url);

/// Reproductor de red por defecto (se mezcla con otro audio y nunca emite sonido).
VideoPlayerController defaultVideoControllerFactory(Uri url) => VideoPlayerController.networkUrl(url, videoPlayerOptions: VideoPlayerOptions(mixWithOthers: true));

/// Video corto de la película (en el detalle y en la tarjeta activa del carrusel): arranca solo, sin sonido y en bucle, en un recuadro 16:9 que se recorta con
/// `BoxFit.cover` (el video no se deforma). Mientras carga se ve el póster con un indicador, y si el video no se puede cargar
/// o falla al reproducirse se queda el póster (nunca un hueco vacío). Se pausa cuando la app pasa a segundo plano o cuando otra
/// pantalla lo cubre (por ejemplo, al abrir el detalle desde la tarjeta), y el controlador se libera al salir.
///
/// Con [fill] llena el espacio que le dé el padre (la tarjeta ya tiene su tamaño y sus esquinas); sin él, dibuja su propio
/// recuadro 16:9 con esquinas redondeadas.
class MoviePreviewVideo extends StatefulWidget {
  const MoviePreviewVideo({super.key, required this.videoUrl, this.posterUrl, this.fill = false, this.controllerFactory = defaultVideoControllerFactory});

  final String videoUrl;
  final String? posterUrl;
  final bool fill;
  final VideoControllerFactory controllerFactory;

  @override
  State<MoviePreviewVideo> createState() => _MoviePreviewVideoState();
}

enum _VideoStatus { loading, ready, failed }

class _MoviePreviewVideoState extends State<MoviePreviewVideo> with WidgetsBindingObserver {
  VideoPlayerController? _controller;
  var _status = _VideoStatus.loading;
  var _visible = true; // false cuando otra pantalla cubre esta (TickerMode desactivado por el Navigator)
  var _inBackground = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(_start());
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final visible = TickerMode.valuesOf(context).enabled;
    if (visible == _visible) return;
    _visible = visible;
    _syncPlayback();
  }

  /// Reproduce solo si la pantalla se ve y la app está al frente.
  void _syncPlayback() {
    final controller = _controller;
    if (controller == null || _status != _VideoStatus.ready) return;
    unawaited(_visible && !_inBackground ? controller.play() : controller.pause());
  }

  @override
  void didUpdateWidget(MoviePreviewVideo oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.videoUrl != widget.videoUrl) {
      unawaited(_release());
      setState(() => _status = _VideoStatus.loading);
      unawaited(_start());
    }
  }

  Future<void> _start() async {
    final uri = Uri.tryParse(widget.videoUrl);
    if (uri == null || !uri.hasScheme) {
      _fail();
      return;
    }

    final controller = widget.controllerFactory(uri);
    _controller = controller;
    try {
      await controller.initialize();
      // La pantalla se cerró (o cambió el video) mientras se inicializaba: este controlador ya no le sirve a nadie.
      if (!mounted || _controller != controller) {
        await controller.dispose();
        return;
      }
      await controller.setLooping(true);
      await controller.setVolume(0);
      controller.addListener(_onValue);
      if (!mounted) return;
      setState(() => _status = _VideoStatus.ready);
      _syncPlayback(); // arranca solo si se ve; si otra pantalla lo cubre, queda en pausa hasta que vuelva
    } on Object {
      if (_controller == controller) {
        _controller = null;
        await controller.dispose();
        _fail();
      }
    }
  }

  void _onValue() {
    final controller = _controller;
    // Falló a mitad de la reproducción. No se libera aquí: un controlador no puede destruirse mientras notifica a sus oyentes.
    if (controller != null && controller.value.hasError) unawaited(Future.microtask(_fail));
  }

  void _fail() {
    if (!mounted) return;
    final controller = _controller;
    _controller = null;
    if (controller != null) {
      controller.removeListener(_onValue);
      unawaited(controller.dispose());
    }
    setState(() => _status = _VideoStatus.failed);
  }

  Future<void> _release() async {
    final controller = _controller;
    _controller = null;
    if (controller == null) return;
    controller.removeListener(_onValue);
    await controller.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _inBackground = false;
    } else if (state == AppLifecycleState.paused || state == AppLifecycleState.hidden) {
      _inBackground = true;
    } else {
      return;
    }
    _syncPlayback();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    unawaited(_release());
    super.dispose();
  }

  Widget get _poster {
    final url = widget.posterUrl;
    final blank = ColoredBox(color: CineColors.surface, child: Center(child: Icon(Icons.movie_outlined, color: CineColors.textTab.withValues(alpha: .6), size: 48)));
    if (url == null) return blank;
    return Image.network(url, key: const Key('preview-poster'), fit: BoxFit.cover, errorBuilder: (_, _, _) => blank);
  }

  @override
  Widget build(BuildContext context) {
    final controller = _controller;
    final content = ColoredBox(
      color: CineColors.background,
      child: Stack(fit: StackFit.expand, children: [
        if (_status != _VideoStatus.ready) _poster,
        if (_status == _VideoStatus.loading)
          const ColoredBox(
            key: Key('preview-loading'),
            color: Color(0x66000000),
            child: Center(child: CircularProgressIndicator(color: CineColors.lime, strokeWidth: 3)),
          ),
        if (_status == _VideoStatus.ready && controller != null)
          FittedBox(
            key: const Key('preview-video'),
            fit: BoxFit.cover,
            clipBehavior: Clip.hardEdge,
            child: SizedBox(width: controller.value.size.width, height: controller.value.size.height, child: VideoPlayer(controller)),
          ),
      ]),
    );

    return Semantics(
      label: 'Video de la película',
      image: true,
      child: ExcludeSemantics(
        child: widget.fill
            ? content
            : ClipRRect(borderRadius: BorderRadius.circular(12), child: AspectRatio(aspectRatio: 16 / 9, child: content)),
      ),
    );
  }
}
