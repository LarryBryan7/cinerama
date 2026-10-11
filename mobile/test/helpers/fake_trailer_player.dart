import 'dart:async';

import 'package:cinerama_mobile/features/movies/ui/trailer/trailer_player.dart';
import 'package:flutter/material.dart';

/// Reproductor de tráiler que no toca YouTube ni un WebView: el test decide qué estado emite y qué posición devuelve.
class FakeTrailerPlayer implements TrailerPlayer {
  FakeTrailerPlayer(this.videoId, {this.startSeconds = 0, this.autoPlay = true, this.positionSeconds = 0});

  final String videoId;
  final double startSeconds;
  final bool autoPlay;
  double positionSeconds;

  final _status = StreamController<TrailerStatus>.broadcast();
  bool disposed = false;
  int pauseCalls = 0;
  final seeks = <double>[];

  void emit(TrailerStatus status) => _status.add(status);

  @override
  Widget buildView({required double aspectRatio}) => ColoredBox(key: Key('fake-trailer-$videoId-${startSeconds.toInt()}'), color: Colors.black, child: const SizedBox.expand());

  @override
  Stream<TrailerStatus> get status => _status.stream;

  @override
  Future<double> position() async => positionSeconds;

  @override
  Future<void> seekTo(double seconds) async => seeks.add(seconds);

  @override
  Future<void> pause() async => pauseCalls++;

  @override
  Future<void> dispose() async {
    disposed = true;
    await _status.close();
  }
}

/// Fábrica que guarda cada reproductor creado, para inspeccionarlos.
class FakeTrailerPlayers {
  FakeTrailerPlayers({this.positionSeconds = 0});

  final double positionSeconds;
  final created = <FakeTrailerPlayer>[];

  FakeTrailerPlayer get last => created.last;
  List<FakeTrailerPlayer> get alive => created.where((p) => !p.disposed).toList();

  TrailerPlayer call(String videoId, {double startSeconds = 0, bool autoPlay = true}) {
    final player = FakeTrailerPlayer(videoId, startSeconds: startSeconds, autoPlay: autoPlay, positionSeconds: positionSeconds);
    created.add(player);
    return player;
  }
}
