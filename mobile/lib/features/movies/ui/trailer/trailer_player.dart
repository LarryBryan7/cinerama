import 'package:flutter/widgets.dart';

/// Estado del tráiler, ya sin los detalles de YouTube.
enum TrailerStatus { loading, playing, paused, ended, error }

/// Lo mínimo que la app necesita de un reproductor de tráiler. El real es un WebView con el reproductor oficial de YouTube
/// ([YoutubeTrailerPlayer]); las pruebas usan uno falso, porque un WebView no existe fuera de un dispositivo.
abstract class TrailerPlayer {
  /// La vista del reproductor, a llenar el espacio con la proporción dada (ancho / alto).
  ///
  /// Nada debe dibujarse **encima** del reproductor: las condiciones de YouTube prohíben superponer elementos sobre cualquier
  /// parte de su reproductor (logo y controles incluidos) y piden un tamaño mínimo de 200 × 200. Además, en móvil el video se
  /// dibuja en la capa superior de la app y taparía cualquier widget puesto encima. Los botones propios van **al lado**.
  Widget buildView({required double aspectRatio});

  /// Cambios de estado. Termina con [TrailerStatus.ended] al acabar el video y con [TrailerStatus.error] si no se puede reproducir.
  Stream<TrailerStatus> get status;

  /// Segundo actual (0 si todavía no hay).
  Future<double> position();

  Future<void> seekTo(double seconds);
  Future<void> pause();

  /// Libera el WebView. Después de esto no se debe usar.
  Future<void> dispose();
}

/// Crea un reproductor para el video [videoId], empezando en [startSeconds]. Con [autoPlay] arranca solo.
typedef TrailerPlayerFactory = TrailerPlayer Function(String videoId, {double startSeconds, bool autoPlay});
