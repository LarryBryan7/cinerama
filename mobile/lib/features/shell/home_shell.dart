import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'cine_bottom_nav.dart';

/// Barra de navegación inferior: Inicio (cartelera), Entradas (mis reservas), Comunidad y Perfil.
class HomeShell extends StatelessWidget {
  const HomeShell({super.key, required this.shell});

  final StatefulNavigationShell shell;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: shell,
      bottomNavigationBar: CineBottomNav(
        currentIndex: shell.currentIndex,
        onSelected: (index) => shell.goBranch(index, initialLocation: index == shell.currentIndex),
      ),
    );
  }
}
