import 'dart:ui' show Tristate;

import 'package:cinerama_mobile/features/seats/data/seat_models.dart';
import 'package:cinerama_mobile/features/seats/ui/seat_grid.dart';
import 'package:cinerama_mobile/features/seats/ui/seat_tile.dart';
import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/seat_helpers.dart';

Widget host(Widget child) => MaterialApp(
      home: Scaffold(body: Center(child: SizedBox(width: 800, height: 600, child: child))),
    );

void main() {
  final until = DateTime.utc(2026, 10, 3, 20, 5);

  // Fila A: libre, mía, bloqueada por otra persona, pasillo (falta el 4), vendida.
  List<Seat> row() => [
        seat('A', 1),
        seat('A', 2, status: SeatStatus.locked, mine: true, until: until),
        seat('A', 3, status: SeatStatus.locked),
        seat('A', 5, status: SeatStatus.booked),
      ];

  testWidgets('cada estado se anuncia con una etiqueta para lectores de pantalla', (tester) async {
    final handle = tester.ensureSemantics();
    await tester.pumpWidget(host(SeatGrid(seats: row(), busy: const {}, onTap: (_) {})));

    expect(find.bySemanticsLabel('Fila A, asiento 1, disponible'), findsOneWidget);
    expect(find.bySemanticsLabel('Fila A, asiento 2, seleccionado por ti'), findsOneWidget);
    expect(find.bySemanticsLabel('Fila A, asiento 3, no disponible temporalmente'), findsOneWidget);
    expect(find.bySemanticsLabel('Fila A, asiento 5, vendido'), findsOneWidget);
    handle.dispose();
  });

  testWidgets('el estado NO depende solo del color: cada uno tiene su propio ícono', (tester) async {
    await tester.pumpWidget(host(SeatGrid(seats: row(), busy: const {}, onTap: (_) {})));

    IconData iconOf(String label) => tester.widget<Icon>(find.descendant(of: find.byKey(Key('seat-$label')), matching: find.byType(Icon))).icon!;

    final icons = {iconOf('A1'), iconOf('A2'), iconOf('A3'), iconOf('A5')};
    expect(icons.length, 4); // cuatro estados, cuatro íconos distintos
    expect(iconOf('A1'), Icons.event_seat_outlined);
    expect(iconOf('A2'), Icons.check);
    expect(iconOf('A3'), Icons.schedule);
    expect(iconOf('A5'), Icons.close);
  });

  testWidgets('solo los asientos libres y los propios son tocables para los lectores de pantalla', (tester) async {
    final handle = tester.ensureSemantics();
    await tester.pumpWidget(host(SeatGrid(seats: row(), busy: const {}, onTap: (_) {})));

    SemanticsData data(String label) => tester.getSemantics(find.bySemanticsLabel(label)).getSemanticsData();

    expect(data('Fila A, asiento 1, disponible').hasAction(SemanticsAction.tap), isTrue);
    expect(data('Fila A, asiento 2, seleccionado por ti').hasAction(SemanticsAction.tap), isTrue);
    expect(data('Fila A, asiento 2, seleccionado por ti').flagsCollection.isSelected, Tristate.isTrue);
    expect(data('Fila A, asiento 5, vendido').hasAction(SemanticsAction.tap), isFalse);
    handle.dispose();
  });

  testWidgets('tocar un asiento avisa con su id', (tester) async {
    final tapped = <int>[];
    await tester.pumpWidget(host(SeatGrid(seats: row(), busy: const {}, onTap: tapped.add)));

    await tester.tap(find.byKey(const Key('seat-A1')));
    await tester.tap(find.byKey(const Key('seat-A2')));

    expect(tapped, [idOf('A1'), idOf('A2')]);
  });

  testWidgets('un pasillo es un hueco en la numeración, del ancho exacto de un asiento', (tester) async {
    await tester.pumpWidget(host(SeatGrid(seats: row(), busy: const {}, onTap: (_) {})));

    final a3 = tester.getTopLeft(find.byKey(const Key('seat-A3'))).dx;
    final a5 = tester.getTopLeft(find.byKey(const Key('seat-A5'))).dx;
    final a2 = tester.getTopLeft(find.byKey(const Key('seat-A2'))).dx;

    final slot = a3 - a2; // dos asientos contiguos
    expect(a5 - a3, closeTo(slot * 2, 0.5)); // A3 → A5 deja un asiento de hueco (el pasillo en la posición 4)
  });

  testWidgets('el área táctil de cada asiento es de al menos 44 × 44', (tester) async {
    await tester.pumpWidget(host(SeatGrid(seats: row(), busy: const {}, onTap: (_) {})));

    final size = tester.getSize(find.byKey(const Key('seat-A1')));

    expect(size.width, greaterThanOrEqualTo(44));
    expect(size.height, greaterThanOrEqualTo(44));
  });

  testWidgets('un asiento con una operación en curso muestra un indicador de progreso', (tester) async {
    await tester.pumpWidget(host(SeatGrid(seats: row(), busy: {idOf('A1')}, onTap: (_) {})));

    expect(find.descendant(of: find.byKey(const Key('seat-A1')), matching: find.byType(CircularProgressIndicator)), findsOneWidget);
    expect(find.descendant(of: find.byKey(const Key('seat-A2')), matching: find.byType(CircularProgressIndicator)), findsNothing);
  });

  testWidgets('dibuja la pantalla y una fila por letra', (tester) async {
    await tester.pumpWidget(host(SeatGrid(seats: smallRoom(), busy: const {}, onTap: (_) {})));

    expect(find.text('PANTALLA'), findsOneWidget);
    expect(find.text('A'), findsOneWidget);
    expect(find.text('B'), findsOneWidget);
    expect(find.byType(SeatTile), findsNWidgets(8));
  });

  testWidgets('la leyenda explica los cuatro estados con texto', (tester) async {
    await tester.pumpWidget(host(const SeatLegend()));

    for (final text in ['Disponible', 'Seleccionado', 'No disponible', 'Vendido']) {
      expect(find.text(text), findsOneWidget);
    }
  });
}
