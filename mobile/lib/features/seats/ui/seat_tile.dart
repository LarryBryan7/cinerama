import 'package:flutter/material.dart';

import '../../../core/theme/app_theme.dart';
import '../data/seat_models.dart';

/// Un asiento. El estado se comunica con color, ÍCONO y etiqueta para lectores de pantalla: nunca solo con el color.
class SeatTile extends StatelessWidget {
  const SeatTile({super.key, required this.seat, required this.busy, required this.onTap});

  final Seat seat;
  final bool busy;
  final VoidCallback onTap;

  /// Lado visible; con el margen, el área táctil es de 48 (≥ 44 pt).
  static const double size = 40;
  static const double margin = 4;

  String get description {
    if (seat.isMine) return 'seleccionado por ti';
    return switch (seat.status) {
      SeatStatus.available => 'disponible',
      SeatStatus.locked => 'no disponible temporalmente',
      SeatStatus.booked => 'vendido',
    };
  }

  @override
  Widget build(BuildContext context) {
    final interactive = seat.isAvailable || seat.isMine;
    final (icon, fill, border, foreground) = switch ((seat.isMine, seat.status)) {
      (true, _) => (Icons.check, SeatPalette.mine, SeatPalette.mine, Colors.black),
      (_, SeatStatus.available) => (Icons.event_seat_outlined, Colors.transparent, SeatPalette.available, SeatPalette.available),
      (_, SeatStatus.locked) => (Icons.schedule, SeatPalette.lockedByOthers.withValues(alpha: 0.25), SeatPalette.lockedByOthers, SeatPalette.lockedByOthers),
      (_, SeatStatus.booked) => (Icons.close, SeatPalette.booked.withValues(alpha: 0.35), SeatPalette.booked, Colors.white54),
    };

    return Semantics(
      label: 'Fila ${seat.row}, asiento ${seat.number}, $description',
      button: interactive,
      enabled: interactive,
      selected: seat.isMine,
      excludeSemantics: true,
      onTap: interactive ? onTap : null,
      child: GestureDetector(
        key: Key('seat-${seat.label}'),
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(margin),
          child: Container(
            width: size,
            height: size,
            decoration: BoxDecoration(
              color: fill,
              borderRadius: BorderRadius.circular(8),
              border: Border.all(color: border, width: seat.isMine ? 2 : 1.5),
            ),
            child: busy
                ? const Center(child: SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)))
                : Column(mainAxisAlignment: MainAxisAlignment.center, children: [
                    Icon(icon, size: 16, color: foreground),
                    Text('${seat.number}', style: TextStyle(fontSize: 11, color: foreground, fontWeight: FontWeight.w600)),
                  ]),
          ),
        ),
      ),
    );
  }
}

class SeatLegend extends StatelessWidget {
  const SeatLegend({super.key});

  @override
  Widget build(BuildContext context) {
    Widget item(IconData icon, Color color, String text) => Row(mainAxisSize: MainAxisSize.min, children: [
          Icon(icon, size: 16, color: color),
          const SizedBox(width: 4),
          Text(text, style: const TextStyle(fontSize: 12)),
        ]);

    return Wrap(spacing: 14, runSpacing: 4, alignment: WrapAlignment.center, children: [
      item(Icons.event_seat_outlined, SeatPalette.available, 'Disponible'),
      item(Icons.check, SeatPalette.mine, 'Seleccionado'),
      item(Icons.schedule, SeatPalette.lockedByOthers, 'No disponible'),
      item(Icons.close, Colors.white54, 'Vendido'),
    ]);
  }
}
