import 'package:flutter/material.dart';

import '../theme/cine_tokens.dart';

/// Avatar circular de quien opinó: su foto si la tiene o su inicial sobre gris, con el trazo interior de Figma.
/// Mide 20 px en la tarjeta (trazo 1) y 30 px en la hoja de comentarios (trazo 0.5).
class CineAvatar extends StatelessWidget {
  const CineAvatar({super.key, required this.name, this.url, this.size = 20, this.strokeWidth = 1});

  final String name;
  final String? url;
  final double size;
  final double strokeWidth;

  Widget get _initial => ColoredBox(
        color: CineColors.grey,
        child: Center(child: Text(name.isEmpty ? '?' : name[0].toUpperCase(), style: cineText(size * .55, FontWeight.w600, CineColors.cinemaLetter))),
      );

  @override
  Widget build(BuildContext context) => SizedBox(
        width: size,
        height: size,
        child: Stack(children: [
          ClipOval(
            child: SizedBox(
              width: size,
              height: size,
              child: url == null ? _initial : Image.network(url!, fit: BoxFit.cover, errorBuilder: (_, _, _) => _initial),
            ),
          ),
          DecoratedBox(
            decoration: BoxDecoration(shape: BoxShape.circle, border: Border.all(color: CineColors.avatarStroke, width: strokeWidth)),
            child: SizedBox(width: size, height: size),
          ),
        ]),
      );
}
