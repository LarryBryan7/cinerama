import 'dart:async';

import 'package:flutter/services.dart';
import 'package:video_player/video_player.dart';

/// Un controlador de video que no toca la plataforma: el test decide cuándo termina de inicializar y si falla.
class FakeVideoController extends VideoPlayerController {
  FakeVideoController({this.failInit = false, this.gate, this.size = const Size(1280, 720)}) : super.networkUrl(Uri.parse('http://localhost/v.mp4'));

  final bool failInit;
  final Completer<void>? gate;
  final Size size;

  int initCalls = 0, playCalls = 0, pauseCalls = 0;
  bool? looping;
  double? volume;
  bool disposed = false;

  @override
  Future<void> initialize() async {
    initCalls++;
    await gate?.future;
    if (failInit) throw PlatformException(code: 'VideoError', message: 'no se pudo abrir');
    value = value.copyWith(isInitialized: true, size: size, duration: const Duration(seconds: 6));
  }

  @override
  Future<void> setLooping(bool looping) async {
    this.looping = looping;
    value = value.copyWith(isLooping: looping);
  }

  @override
  Future<void> setVolume(double volume) async {
    this.volume = volume;
    value = value.copyWith(volume: volume);
  }

  @override
  Future<void> play() async {
    playCalls++;
    value = value.copyWith(isPlaying: true);
  }

  @override
  Future<void> pause() async {
    pauseCalls++;
    value = value.copyWith(isPlaying: false);
  }

  void breakPlayback() => value = value.copyWith(errorDescription: 'se cortó la conexión');

  @override
  Future<void> dispose() async {
    disposed = true;
    await super.dispose();
  }
}
