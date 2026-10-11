import 'dart:math' as math;

import 'package:flutter/material.dart';

/// Lienzo con las medidas de Figma (390 px de ancho). Los hijos se colocan con `Positioned` usando las coordenadas
/// absolutas del archivo, así cada margen, ancho y alto es el medido, sin recalcular proporciones.
///
/// - Pantalla más ancha que [width]: el lienzo queda centrado y a tamaño real (los textos conservan su tamaño exacto).
/// - Pantalla más angosta: se reduce todo de forma proporcional para que no se corte nada.
/// - El texto no se escala con la configuración del sistema dentro del lienzo: las cajas de Figma son fijas y un
///   texto más grande se desbordaría.
class DesignCanvas extends StatelessWidget {
  const DesignCanvas({super.key, required this.height, required this.children, this.width = 390});

  final double width;
  final double height;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final scale = math.min(1.0, constraints.maxWidth / width);
        return SizedBox(
          width: constraints.maxWidth,
          height: height * scale,
          child: Transform.scale(
            scale: scale,
            alignment: Alignment.topCenter,
            child: OverflowBox(
              alignment: Alignment.topCenter,
              minWidth: width,
              maxWidth: width,
              minHeight: height,
              maxHeight: height,
              child: MediaQuery(
                data: MediaQuery.of(context).copyWith(textScaler: TextScaler.noScaling),
                child: SizedBox(width: width, height: height, child: Stack(clipBehavior: Clip.none, children: children)),
              ),
            ),
          ),
        );
      },
    );
  }
}
