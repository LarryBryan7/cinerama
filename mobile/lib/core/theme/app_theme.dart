import 'package:flutter/material.dart';

/// Colores de los asientos. El estado NUNCA se comunica solo con el color (RNF-13): cada uno tiene además su ícono
/// y su etiqueta para lectores de pantalla.
class SeatPalette {
  const SeatPalette._();

  static const available = Color(0xFF66BB6A); // borde verde, relleno vacío
  static const mine = Color(0xFF42A5F5); // relleno azul
  static const lockedByOthers = Color(0xFF9E9E9E);
  static const booked = Color(0xFF616161);
}

class AppTheme {
  const AppTheme._();

  static ThemeData dark() {
    final scheme = ColorScheme.fromSeed(seedColor: const Color(0xFFD32F2F), brightness: Brightness.dark);
    return ThemeData(
      colorScheme: scheme,
      useMaterial3: true,
      visualDensity: VisualDensity.standard,
      inputDecorationTheme: const InputDecorationTheme(border: OutlineInputBorder()),
      snackBarTheme: const SnackBarThemeData(behavior: SnackBarBehavior.floating),
      appBarTheme: const AppBarTheme(centerTitle: false),
    );
  }
}
