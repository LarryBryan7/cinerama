import 'package:flutter/material.dart';

import '../../../core/theme/cine_tokens.dart';

/// Pestaña "Comunidad". Figma solo define la pestaña de la barra inferior, no su pantalla: queda como marcador
/// hasta que exista el diseño y la funcionalidad.
class CommunityPage extends StatelessWidget {
  const CommunityPage({super.key});

  @override
  Widget build(BuildContext context) => Scaffold(
        backgroundColor: CineColors.background,
        body: Center(child: Text('Comunidad', style: cineText(14, FontWeight.w300, CineColors.textTab))),
      );
}
