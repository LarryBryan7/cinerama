import 'package:flutter/material.dart';

/// Colores leídos de Figma (frame "Cartelera - dark - 1"). Son los valores exactos del archivo, no aproximaciones.
class CineColors {
  const CineColors._();

  static const background = Color(0xFF0E0E10); // contenido y barra inferior
  static const surface = Color(0xFF1C212B); // cabecera y tarjeta
  static const chip = Color(0xFF3B3B3F); // chips de reseña y de cines
  static const lime = Color(0xFFCCFF3E);
  static const limeDark = Color(0xFF93C800); // etiqueta de la pestaña activa
  static const red = Color(0xFFFF3F3F);
  static const grey = Color(0xFFD3D3D3);

  static const tabSelected = Color(0xFFF0F0F0);
  static const tabUnselected = Color(0xFF403F47);

  static const white = Color(0xFFFFFFFF);
  static const textOnLime = Color(0xFF020C00);
  static const textTitle = Color(0xFFF0F0F0);
  static const textBody = Color(0xFFEBEDEE);
  static const textSoft = Color(0xFFE4DCE4);
  static const textTab = Color(0xFFEAE9EF);
  static const avatarStroke = Color(0xFFF7F6F3);

  static const white90 = Color.fromRGBO(0xFF, 0xFF, 0xFF, .9);
  static const soft90 = Color.fromRGBO(0xE4, 0xDC, 0xE4, .9);
  static const soft80 = Color.fromRGBO(0xE4, 0xDC, 0xE4, .8);
  static const cinemaLetter = Color.fromRGBO(0xE5, 0x00, 0x47, .75);

  static const cardStroke = Color.fromRGBO(0xF8, 0xF8, 0xF8, .2);
  static const cardShadow = Color.fromRGBO(0x00, 0x00, 0x00, .07);
  static const reviewStroke = Color.fromRGBO(0xF4, 0xF4, 0xF4, .2);
  static const cinemaStroke = Color.fromRGBO(0xF0, 0xF0, 0xF0, .4);
  static const divider = Color.fromRGBO(0xEA, 0xE9, 0xEF, .22);
  static const cinemaDivider = Color.fromRGBO(0xF0, 0xF0, 0xF0, .22);
  static const navBorder = Color.fromRGBO(0xEE, 0xEE, 0xEE, .2);
  static const navPill = Color.fromRGBO(0xE5, 0xFF, 0x9E, .2);

  // Tarjeta: contador de video y botón de reproducir de vidrio.
  static const videoPill = Color.fromRGBO(0x0E, 0x0E, 0x0E, .95);
  static const glass = Color.fromRGBO(0xD9, 0xD9, 0xD9, .5);

  // Hoja de comentarios ("BottonSheet - Comment").
  static const sheetScrim = Color.fromRGBO(0x0E, 0x0E, 0x10, .7);
  static const sheetHandle = Color(0xFFDCDCDC);
  static const commentText = Color(0xFFF4F4F4);
  static const inputStroke = Color.fromRGBO(0x8E, 0x8E, 0x8E, .82);
  static const send = Color(0xFFBAE93C);
  static const sendShadow = Color.fromRGBO(0x86, 0x86, 0x86, .4);
  static const sendIcon = Color(0xFF202020);
  static const white85 = Color.fromRGBO(0xFF, 0xFF, 0xFF, .85);
}

/// Estilo de texto de Figma. La altura de línea queda en automático (null), igual que en el archivo: Inter 3.19 da 1.21 × tamaño.
TextStyle cineText(
  double size,
  FontWeight weight,
  Color color, {
  bool italic = false,
  bool underline = false,
}) =>
    TextStyle(
      fontFamily: 'Inter',
      fontSize: size,
      fontWeight: weight,
      color: color,
      fontStyle: italic ? FontStyle.italic : FontStyle.normal,
      decoration: underline ? TextDecoration.underline : TextDecoration.none,
      decorationColor: color,
    );
