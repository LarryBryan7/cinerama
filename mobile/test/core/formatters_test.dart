import 'package:cinerama_mobile/core/util/formatters.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';

void main() {
  setUpAll(() => initializeDateFormatting('es'));

  group('relativeDay (día de una opinión, según el calendario local)', () {
    final now = DateTime(2026, 10, 7, 9, 30); // hora local

    String label(DateTime localMoment) => Formatters.relativeDay(localMoment.toUtc(), now);

    test('hoy, aunque haya pasado casi un día', () => expect(label(DateTime(2026, 10, 7, 0, 5)), 'Hoy'));
    test('una hora que aún no llegó se trata como hoy (reloj del servidor adelantado)', () => expect(label(DateTime(2026, 10, 7, 23, 59)), 'Hoy'));
    test('ayer, aunque hayan pasado solo 3 horas', () => expect(label(DateTime(2026, 10, 6, 23, 59)), 'Ayer'));
    test('hace dos días', () => expect(label(DateTime(2026, 10, 5, 12)), 'hace 2 días'));
    test('hace 29 días', () => expect(label(DateTime(2026, 9, 8, 12)), 'hace 29 días'));
    test('pasado un mes, la fecha completa', () => expect(label(DateTime(2026, 8, 1, 12)), contains('2026')));
  });

  test('shortDate muestra día y mes abreviado en español', () {
    expect(Formatters.shortDate(DateTime(2026, 10, 11)), '11 oct');
  });
}
