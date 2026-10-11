import 'package:flutter/material.dart';

import '../data/seat_models.dart';
import 'seat_tile.dart';

/// La sala completa: pantalla arriba y una fila por letra. Un número que falta en la numeración es un pasillo.
/// Se ajusta al ancho de la pantalla y se puede acercar y desplazar con los dedos.
class SeatGrid extends StatelessWidget {
  const SeatGrid({super.key, required this.seats, required this.busy, required this.onTap});

  final List<Seat> seats;
  final Set<int> busy;
  final void Function(int seatId) onTap;

  @override
  Widget build(BuildContext context) {
    final rows = <String, Map<int, Seat>>{};
    for (final seat in seats) {
      rows.putIfAbsent(seat.row, () => {})[seat.number] = seat;
    }
    final letters = rows.keys.toList()..sort();
    final maxNumber = seats.fold<int>(0, (max, s) => s.number > max ? s.number : max);
    const slot = SeatTile.size + SeatTile.margin * 2;

    final grid = Column(mainAxisSize: MainAxisSize.min, children: [
      const _Screen(),
      const SizedBox(height: 16),
      for (final letter in letters)
        Row(mainAxisSize: MainAxisSize.min, children: [
          SizedBox(width: 28, child: ExcludeSemantics(child: Text(letter, textAlign: TextAlign.center, style: const TextStyle(fontWeight: FontWeight.bold)))),
          for (var n = 1; n <= maxNumber; n++)
            rows[letter]!.containsKey(n)
                ? SeatTile(
                    seat: rows[letter]![n]!,
                    busy: busy.contains(rows[letter]![n]!.seatId),
                    onTap: () => onTap(rows[letter]![n]!.seatId),
                  )
                : const SizedBox(width: slot, height: slot), // pasillo
        ]),
    ]);

    return InteractiveViewer(
      minScale: 1,
      maxScale: 4,
      boundaryMargin: const EdgeInsets.all(48),
      child: Center(child: FittedBox(fit: BoxFit.scaleDown, child: Padding(padding: const EdgeInsets.all(8), child: grid))),
    );
  }
}

class _Screen extends StatelessWidget {
  const _Screen();

  @override
  Widget build(BuildContext context) => ExcludeSemantics(
        child: Column(children: [
          Container(
            width: 260,
            height: 6,
            decoration: BoxDecoration(color: Theme.of(context).colorScheme.primary, borderRadius: BorderRadius.circular(3)),
          ),
          const SizedBox(height: 4),
          const Text('PANTALLA', style: TextStyle(fontSize: 11, letterSpacing: 2)),
        ]),
      );
}
