import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../../../core/theme/cine_tokens.dart';
import 'trailer_player.dart';
import 'youtube_trailer_player.dart';

/// Cómo terminó la pantalla completa: el video llegó al final, la persona la cerró (con la X o con atrás) o falló.
sealed class TrailerFullscreenResult {
  const TrailerFullscreenResult();
}

class TrailerEnded extends TrailerFullscreenResult {
  const TrailerEnded();
}

class TrailerClosed extends TrailerFullscreenResult {
  const TrailerClosed(this.seconds);

  /// Dónde se quedó, para que el reproductor de la tarjeta continúe desde ahí.
  final double seconds;
}

class TrailerFailed extends TrailerFullscreenResult {
  const TrailerFailed();
}

/// Tráiler a pantalla completa: gira el dispositivo a horizontal, oculta las barras del sistema y se cierra con la X (en una
/// franja al lado del video), con el botón atrás o con el botón de pantalla completa del propio YouTube. Es una pantalla encima de
/// Inicio (una ruta), así que Inicio queda como estaba, sin recargarse. Al terminar el video se cierra sola. Al salir se
/// devuelve la orientación y las barras del sistema.
class TrailerFullscreenPage extends StatefulWidget {
  const TrailerFullscreenPage({super.key, required this.videoId, this.startSeconds = 0, this.playerFactory = youtubeTrailerPlayerFactory});

  final String videoId;
  final double startSeconds;
  final TrailerPlayerFactory playerFactory;

  /// Abre la pantalla completa y devuelve cómo terminó (nunca nulo: salir de cualquier forma es [TrailerClosed]).
  static Future<TrailerFullscreenResult> open(
    BuildContext context, {
    required String videoId,
    double startSeconds = 0,
    TrailerPlayerFactory playerFactory = youtubeTrailerPlayerFactory,
  }) async {
    final result = await Navigator.of(context, rootNavigator: true).push<TrailerFullscreenResult>(MaterialPageRoute(
      fullscreenDialog: true,
      builder: (_) => TrailerFullscreenPage(videoId: videoId, startSeconds: startSeconds, playerFactory: playerFactory),
    ));
    return result ?? TrailerClosed(startSeconds);
  }

  @override
  State<TrailerFullscreenPage> createState() => _TrailerFullscreenPageState();
}

class _TrailerFullscreenPageState extends State<TrailerFullscreenPage> {
  late final TrailerPlayer _player;
  StreamSubscription<TrailerStatus>? _subscription;
  var _finished = false;

  @override
  void initState() {
    super.initState();
    unawaited(SystemChrome.setPreferredOrientations(const [DeviceOrientation.landscapeLeft, DeviceOrientation.landscapeRight]));
    unawaited(SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky));
    _player = widget.playerFactory(widget.videoId, startSeconds: widget.startSeconds, autoPlay: true);
    _subscription = _player.status.listen((status) {
      if (status == TrailerStatus.ended) _finish(const TrailerEnded());
      if (status == TrailerStatus.error) _finish(const TrailerFailed());
    });
  }

  void _finish(TrailerFullscreenResult result) {
    if (_finished || !mounted) return;
    _finished = true;
    Navigator.of(context).pop(result);
  }

  Future<void> _close() async {
    if (_finished) return;
    final seconds = await _player.position().catchError((_) => widget.startSeconds);
    _finish(TrailerClosed(seconds));
  }

  @override
  void dispose() {
    unawaited(_subscription?.cancel());
    unawaited(_player.dispose());
    // Se devuelve lo que había: sin orientación fija y con las barras del sistema.
    unawaited(SystemChrome.setPreferredOrientations(const []));
    unawaited(SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge));
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return PopScope(
      canPop: false, // el botón atrás pasa por _close, que recuerda el segundo en que se quedó
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) unawaited(_close());
      },
      child: Scaffold(
        backgroundColor: Colors.black,
        // El video llena el alto y deja una franja a cada lado: la X vive en la franja izquierda, FUERA del reproductor. YouTube no
        // admite elementos superpuestos a su reproductor (ni a su logo ni a sus controles).
        body: SafeArea(
          child: LayoutBuilder(builder: (context, constraints) {
            const strip = 56.0;
            const aspect = 16 / 9;
            final width = (constraints.maxWidth - 2 * strip).clamp(0.0, double.infinity);
            final videoWidth = width < constraints.maxHeight * aspect ? width : constraints.maxHeight * aspect;
            return Stack(children: [
              Center(child: SizedBox(key: const Key('trailer-video-box'), width: videoWidth, height: videoWidth / aspect, child: _player.buildView(aspectRatio: aspect))),
              Positioned(
                left: 4,
                top: 4,
                child: Semantics(
                  button: true,
                  label: 'Cerrar el tráiler',
                  child: IconButton(
                    key: const Key('trailer-fullscreen-close'),
                    tooltip: 'Cerrar',
                    style: IconButton.styleFrom(backgroundColor: Colors.white12, minimumSize: const Size(48, 48)),
                    icon: const Icon(Icons.close, color: CineColors.white),
                    onPressed: _close,
                  ),
                ),
              ),
            ]);
          }),
        ),
      ),
    );
  }
}
