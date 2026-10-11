import 'package:flutter/material.dart';

import 'trailer_player.dart';

/// El reproductor del tráiler dentro de la tarjeta del carrusel, en el espacio que le da la tarjeta (261 × 200 mientras suena:
/// YouTube exige un reproductor de al menos 200 × 200). Solo lo dibuja: el reproductor lo crea y lo libera el carrusel, que además
/// pone **fuera** del reproductor sus botones (cerrar y pantalla completa); las condiciones de YouTube prohíben dibujar nada encima.
class TrailerView extends StatelessWidget {
  const TrailerView({super.key, required this.player, required this.aspectRatio});

  final TrailerPlayer player;

  /// Proporción (ancho / alto). No tiene por qué ser 16:9: el video se ve entero, con barras negras.
  final double aspectRatio;

  @override
  Widget build(BuildContext context) => ColoredBox(color: Colors.black, child: player.buildView(aspectRatio: aspectRatio));
}
