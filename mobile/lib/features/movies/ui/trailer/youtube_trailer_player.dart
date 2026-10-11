import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:youtube_player_iframe/youtube_player_iframe.dart';

import 'trailer_player.dart';

/// Parámetros del reproductor. Todos son **parámetros documentados** de la API de YouTube: nada del reproductor se modifica.
/// `fs=0` apaga su botón de pantalla completa (la app ofrece el suyo, fuera del reproductor); `iv_load_policy=3` apaga las anotaciones;
/// `rel=0` limita los relacionados al mismo canal.
const trailerPlayerParams = YoutubePlayerParams(
  showControls: true,
  showFullscreenButton: false,
  mute: false,
  loop: false,
  strictRelatedVideos: true,
  playsInline: true,
  interfaceLanguage: 'es',
  captionLanguage: 'es',
  showVideoAnnotations: false,
  privacyEnhancedMode: true,
);

/// Reproductor oficial de YouTube (IFrame). Es la forma permitida de mostrar un tráiler: el video lo sirve YouTube y respeta sus
/// condiciones; la app no descarga ni guarda nada. Usa el modo de privacidad mejorada (youtube-nocookie.com).
///
/// El reproductor **no se modifica**: solo se usan parámetros documentados de la API (controles, pantalla completa apagada, anotaciones
/// apagadas, videos relacionados del mismo canal). Nada se dibuja encima de él ni se intercepta ninguno de sus botones.
class YoutubeTrailerPlayer implements TrailerPlayer {
  YoutubeTrailerPlayer(String videoId, {double startSeconds = 0, bool autoPlay = true})
      : _controller = YoutubePlayerController(
          key: videoId,
          params: trailerPlayerParams,
        ) {
    final start = startSeconds > 0 ? startSeconds : null;
    unawaited(autoPlay
        ? _controller.loadVideoById(videoId: videoId, startSeconds: start)
        : _controller.cueVideoById(videoId: videoId, startSeconds: start));
  }

  final YoutubePlayerController _controller;

  @override
  Widget buildView({required double aspectRatio}) => YoutubePlayer(
        controller: _controller,
        aspectRatio: aspectRatio,
        enableFullScreenOnVerticalDrag: false,
        autoFullScreen: false,
      );

  @override
  Stream<TrailerStatus> get status => _controller.stream.map((value) {
        if (value.error != YoutubeError.none) return TrailerStatus.error;
        return switch (value.playerState) {
          PlayerState.playing => TrailerStatus.playing,
          PlayerState.paused => TrailerStatus.paused,
          PlayerState.ended => TrailerStatus.ended,
          _ => TrailerStatus.loading,
        };
      }).distinct();

  @override
  Future<double> position() => _controller.currentTime;

  @override
  Future<void> seekTo(double seconds) => _controller.seekTo(seconds: seconds, allowSeekAhead: true);

  @override
  Future<void> pause() => _controller.pauseVideo();

  @override
  Future<void> dispose() => _controller.close();
}

/// Fábrica de la app: un reproductor real de YouTube.
TrailerPlayer youtubeTrailerPlayerFactory(String videoId, {double startSeconds = 0, bool autoPlay = true}) =>
    YoutubeTrailerPlayer(videoId, startSeconds: startSeconds, autoPlay: autoPlay);
