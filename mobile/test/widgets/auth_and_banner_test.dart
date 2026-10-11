import 'package:bloc_test/bloc_test.dart';
import 'package:cinerama_mobile/core/connectivity/connectivity_cubit.dart';
import 'package:cinerama_mobile/core/widgets/common_widgets.dart';
import 'package:cinerama_mobile/features/auth/bloc/auth_bloc.dart';
import 'package:cinerama_mobile/features/auth/ui/login_page.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../helpers/fakes.dart';

class MockAuthBloc extends MockBloc<AuthEvent, AuthState> implements AuthBloc {}



void main() {
  setUpAll(() => registerFallbackValue(const AuthLoginRequested('', '')));

  group('login', () {
    late MockAuthBloc bloc;

    setUp(() {
      bloc = MockAuthBloc();
      when(() => bloc.state).thenReturn(const AuthState(status: AuthStatus.unauthenticated));
    });

    Future<void> pumpLogin(WidgetTester tester) => tester.pumpWidget(MaterialApp(home: BlocProvider<AuthBloc>.value(value: bloc, child: const LoginPage())));

    testWidgets('valida los campos antes de llamar al servidor', (tester) async {
      await pumpLogin(tester);

      await tester.tap(find.byKey(const Key('login-submit')));
      await tester.pump();

      expect(find.text('Ingresa un email válido'), findsOneWidget);
      expect(find.text('Ingresa tu contraseña'), findsOneWidget);
      verifyNever(() => bloc.add(any()));
    });

    testWidgets('con datos válidos envía el inicio de sesión', (tester) async {
      await pumpLogin(tester);

      await tester.enterText(find.byKey(const Key('login-email')), 'ana@example.test');
      await tester.enterText(find.byKey(const Key('login-password')), 'Clave123');
      await tester.tap(find.byKey(const Key('login-submit')));
      await tester.pump();

      final event = verify(() => bloc.add(captureAny())).captured.single as AuthLoginRequested;
      expect((event.email, event.password), ('ana@example.test', 'Clave123'));
    });

    testWidgets('muestra el mensaje de error y no deja enviar mientras trabaja', (tester) async {
      when(() => bloc.state).thenReturn(const AuthState(status: AuthStatus.unauthenticated, busy: true, message: 'El email o la contraseña no son correctos.'));
      await pumpLogin(tester);

      expect(find.byKey(const Key('auth-message')), findsOneWidget);
      expect(tester.widget<FilledButton>(find.byKey(const Key('login-submit'))).onPressed, isNull);
    });

    testWidgets('la contraseña se oculta', (tester) async {
      await pumpLogin(tester);

      final field = tester.widget<EditableText>(find.descendant(of: find.byKey(const Key('login-password')), matching: find.byType(EditableText)));
      expect(field.obscureText, isTrue);
    });
  });

  group('aviso de conexión', () {
    Future<FakeConnectivity> pumpBanner(WidgetTester tester, {required bool online}) async {
      final connectivity = FakeConnectivity(online: online);
      addTearDown(connectivity.dispose);
      await tester.pumpWidget(MaterialApp(
        home: BlocProvider(create: (_) => ConnectivityCubit(connectivity), child: const Scaffold(body: OfflineBanner())),
      ));
      return connectivity;
    }

    testWidgets('no aparece si hay conexión', (tester) async {
      await pumpBanner(tester, online: true);

      expect(find.textContaining('Sin conexión'), findsNothing);
    });

    testWidgets('aparece al perder la conexión, se anuncia a los lectores de pantalla y desaparece al recuperarla', (tester) async {
      final handle = tester.ensureSemantics();
      final connectivity = await pumpBanner(tester, online: true);

      connectivity.set(false);
      await tester.pump();

      expect(find.textContaining('Sin conexión'), findsOneWidget);
      expect(find.bySemanticsLabel(RegExp('Sin conexión con el servidor')), findsOneWidget);
      expect(tester.getSemantics(find.bySemanticsLabel(RegExp('Sin conexión con el servidor'))).getSemanticsData().flagsCollection.isLiveRegion, isTrue);

      connectivity.set(true);
      await tester.pumpAndSettle();
      expect(find.textContaining('Sin conexión'), findsNothing);
      handle.dispose();
    });
  });
}
