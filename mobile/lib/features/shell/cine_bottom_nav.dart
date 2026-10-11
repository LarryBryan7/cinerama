import 'package:flutter/material.dart';

import '../../core/theme/cine_tokens.dart';
import '../../core/theme/figma_icons.dart';
import '../../core/widgets/design_canvas.dart';

class _NavTab {
  const _NavTab({
    required this.tabKey,
    required this.label,
    required this.icon,
    required this.hit,
    required this.labelX,
    required this.labelY,
    required this.labelWidth,
    required this.iconX,
    required this.iconY,
    required this.pillX,
  });

  final Key tabKey;
  final String label;
  final FigmaIconData icon;
  final Rect hit; // área táctil: el frame "nav-*" de Figma
  final double labelX, labelY, labelWidth;
  final double iconX, iconY;
  final double pillX; // x de la píldora de la pestaña activa (y=13, 42×26)
}

/// Barra inferior de 74 px (Figma: "bottom-nav"). Las coordenadas están medidas desde su borde superior (y=662 en el frame).
/// Solo existe el diseño de la pestaña Inicio activa; para las demás se repite el mismo tratamiento (píldora, ícono lima
/// y etiqueta lima oscuro) centrado en su ícono.
class CineBottomNav extends StatelessWidget {
  const CineBottomNav({super.key, required this.currentIndex, required this.onSelected});

  final int currentIndex;
  final ValueChanged<int> onSelected;

  static final _tabs = [
    _NavTab(
      tabKey: const Key('tab-movies'),
      label: 'Inicio',
      icon: FigmaIconData.home,
      hit: const Rect.fromLTWH(12, 2, 97, 55),
      labelX: 19,
      labelY: 42,
      labelWidth: 85,
      iconX: 54,
      iconY: 17,
      pillX: 40,
    ),
    _NavTab(
      tabKey: const Key('tab-bookings'),
      label: 'Entradas',
      icon: FigmaIconData.ticketNav,
      hit: const Rect.fromLTWH(109, 3, 92, 55),
      labelX: 109,
      labelY: 43,
      labelWidth: 92,
      iconX: 143,
      iconY: 10,
      pillX: 137.24,
    ),
    _NavTab(
      tabKey: const Key('tab-community'),
      label: 'Comunidad',
      icon: FigmaIconData.chat,
      hit: const Rect.fromLTWH(202, 3, 92, 55),
      labelX: 202,
      labelY: 43,
      labelWidth: 92,
      iconX: 247,
      iconY: 18,
      pillX: 235,
    ),
    _NavTab(
      tabKey: const Key('tab-profile'),
      label: 'Perfil',
      icon: FigmaIconData.user,
      hit: const Rect.fromLTWH(300, 2, 88, 55),
      labelX: 300,
      labelY: 42,
      labelWidth: 90,
      iconX: 334,
      iconY: 15,
      pillX: 322.5,
    ),
  ];

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      color: CineColors.background,
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        Stack(children: [
          DesignCanvas(height: 74, children: [
            for (var i = 0; i < _tabs.length; i++) ..._visuals(_tabs[i], active: i == currentIndex),
            for (var i = 0; i < _tabs.length; i++) _hitArea(_tabs[i], i),
          ]),
          // Borde superior de 1 px, adentro del alto de 74 (Figma: trazo "inside" solo arriba).
          const Positioned(top: 0, left: 0, right: 0, height: 1, child: ColoredBox(color: CineColors.navBorder)),
        ]),
        SizedBox(height: MediaQuery.paddingOf(context).bottom),
      ]),
    );
  }

  List<Widget> _visuals(_NavTab tab, {required bool active}) => [
        if (active)
          Positioned(
            left: tab.pillX,
            top: 13,
            width: 42,
            height: 26,
            child: DecoratedBox(decoration: BoxDecoration(color: CineColors.navPill, borderRadius: BorderRadius.circular(14))),
          ),
        Positioned(left: tab.iconX, top: tab.iconY, child: FigmaIcon(tab.icon, tint: active ? CineColors.lime : CineColors.soft80)),
        Positioned(
          left: tab.labelX,
          top: tab.labelY,
          width: tab.labelWidth,
          child: Text(
            tab.label,
            textAlign: TextAlign.center,
            softWrap: false,
            style: active ? cineText(12, FontWeight.w600, CineColors.limeDark) : cineText(12, FontWeight.w400, CineColors.soft80),
          ),
        ),
      ];

  Widget _hitArea(_NavTab tab, int index) => Positioned(
        left: tab.hit.left,
        top: tab.hit.top,
        width: tab.hit.width,
        height: tab.hit.height,
        child: Semantics(
          button: true,
          selected: index == currentIndex,
          label: tab.label,
          child: GestureDetector(key: tab.tabKey, behavior: HitTestBehavior.opaque, onTap: () => onSelected(index), child: const SizedBox.expand()),
        ),
      );
}
