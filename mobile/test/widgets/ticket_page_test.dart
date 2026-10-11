import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/core/connectivity/connectivity_cubit.dart';
import 'package:cinerama_mobile/features/bookings/bloc/ticket_cubit.dart';
import 'package:cinerama_mobile/features/bookings/data/bookings_repository.dart';
import 'package:cinerama_mobile/features/bookings/ui/ticket_page.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:mocktail/mocktail.dart';
import 'package:qr_flutter/qr_flutter.dart';

import '../helpers/booking_helpers.dart';
import '../helpers/fakes.dart';

class MockBookings extends Mock implements BookingsRepository {}

void main() {
  late MockBookings repo;

  setUpAll(() => initializeDateFormatting('es'));
  setUp(() => repo = MockBookings());

  Future<void> pumpTicket(WidgetTester tester, {required bool online}) async {
    tester.view.physicalSize = const Size(900, 2400); // alto de sobra: la lista es perezosa y el botón de cancelar queda al final
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final connectivity = FakeConnectivity(online: online);
    addTearDown(connectivity.dispose);
    await tester.pumpWidget(MaterialApp(
      home: MultiBlocProvider(
        providers: [
          BlocProvider(create: (_) => ConnectivityCubit(connectivity)),
          BlocProvider(create: (_) => TicketCubit(repo, 1)),
        ],
        child: const TicketPage(),
      ),
    ));
    await tester.pumpAndSettle();
  }

  testWidgets('SIN CONEXIÓN muestra el QR guardado con el contenido firmado por el servidor y aclara que es el guardado', (tester) async {
    when(() => repo.cachedById(1)).thenAnswer((_) async => booking(id: 1, ticket: 'K7QH3MXP.firma-del-servidor'));
    when(() => repo.fetchDetail(1)).thenThrow(const ApiException.network());

    await pumpTicket(tester, online: false);

    final qr = tester.widget<TicketQr>(find.byKey(const Key('ticket-qr')));
    expect(qr.data, 'K7QH3MXP.firma-del-servidor'); // exactamente lo que firmó el servidor, sin que la app invente nada
    expect(find.byKey(const Key('ticket-offline-note')), findsOneWidget);
    expect(find.textContaining('El cine confirma su validez'), findsOneWidget);
    expect(find.byKey(const Key('ticket-code')), findsOneWidget);
    expect(find.text('K7QH3MXP'), findsOneWidget);
  });

  testWidgets('sin conexión no se puede cancelar: el botón lo explica y está deshabilitado', (tester) async {
    when(() => repo.cachedById(1)).thenAnswer((_) async => booking(id: 1));
    when(() => repo.fetchDetail(1)).thenThrow(const ApiException.network());

    await pumpTicket(tester, online: false);

    final button = tester.widget<OutlinedButton>(find.byKey(const Key('cancel-booking')));
    expect(button.onPressed, isNull);
    expect(find.text('Cancelar (necesita conexión)'), findsOneWidget);
  });

  testWidgets('con conexión el ticket viene del servidor: sin aviso de "guardado" y con cancelación disponible', (tester) async {
    when(() => repo.cachedById(1)).thenAnswer((_) async => booking(id: 1));
    when(() => repo.fetchDetail(1)).thenAnswer((_) async => booking(id: 1));

    await pumpTicket(tester, online: true);

    expect(find.byKey(const Key('ticket-offline-note')), findsNothing);
    expect(find.byKey(const Key('ticket-qr')), findsOneWidget);
    expect(tester.widget<OutlinedButton>(find.byKey(const Key('cancel-booking'))).onPressed, isNotNull);
  });

  testWidgets('una reserva cancelada NO muestra QR y lo dice', (tester) async {
    when(() => repo.cachedById(1)).thenAnswer((_) async => booking(id: 1, status: 'Cancelled'));
    when(() => repo.fetchDetail(1)).thenAnswer((_) async => booking(id: 1, status: 'Cancelled', cancelledAt: DateTime.utc(2026, 10, 3)));

    await pumpTicket(tester, online: true);

    expect(find.byKey(const Key('ticket-qr')), findsNothing);
    expect(find.byKey(const Key('ticket-cancelled')), findsOneWidget);
    expect(find.byKey(const Key('cancel-booking')), findsNothing);
  });

  testWidgets('una reserva sin ticket descargado pide conectarse para obtener el QR', (tester) async {
    when(() => repo.cachedById(1)).thenAnswer((_) async => booking(id: 1, ticket: null));
    when(() => repo.fetchDetail(1)).thenThrow(const ApiException.network());

    await pumpTicket(tester, online: false);

    expect(find.byKey(const Key('ticket-qr')), findsNothing);
    expect(find.textContaining('Conéctate para descargar el código QR'), findsOneWidget);
  });

  testWidgets('el QR tiene fondo blanco y una descripción con el código de reserva', (tester) async {
    final handle = tester.ensureSemantics();
    when(() => repo.cachedById(1)).thenAnswer((_) async => booking(id: 1, code: 'K7QH3MXP'));
    when(() => repo.fetchDetail(1)).thenAnswer((_) async => booking(id: 1, code: 'K7QH3MXP'));

    await pumpTicket(tester, online: true);

    expect(find.bySemanticsLabel(RegExp('Código QR de tu entrada.*K 7 Q H 3 M X P')), findsOneWidget);
    expect(tester.widget<QrImageView>(find.descendant(of: find.byKey(const Key('ticket-qr')), matching: find.byType(QrImageView))).backgroundColor, Colors.white);
    handle.dispose();
  });
}
