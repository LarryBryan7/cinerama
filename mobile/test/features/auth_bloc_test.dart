import 'dart:async';

import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:cinerama_mobile/core/api/session_storage.dart';
import 'package:cinerama_mobile/core/api/token_store.dart';
import 'package:cinerama_mobile/core/db/app_database.dart';
import 'package:cinerama_mobile/core/session/session_manager.dart';
import 'package:cinerama_mobile/features/auth/bloc/auth_bloc.dart';
import 'package:cinerama_mobile/features/auth/data/auth_models.dart';
import 'package:cinerama_mobile/features/auth/data/auth_repository.dart';
import 'package:cinerama_mobile/features/bookings/data/bookings_repository.dart';
import 'package:drift/drift.dart' show driftRuntimeOptions;
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import '../helpers/fakes.dart';

class MockAuthRepository extends Mock implements AuthRepository {}

class MockTokenStore extends Mock implements TokenStore {}

const ana = AppUser(id: 1, fullName: 'Ana Pérez', email: 'ana@example.test', role: 'Customer');

void main() {
  late MockAuthRepository repo;
  late MemorySessionStorage storage;
  late MockTokenStore tokens;
  late List<String> events;
  late SessionManager sessions;

  setUp(() {
    driftRuntimeOptions.dontWarnAboutMultipleDatabases = true;
    repo = MockAuthRepository();
    storage = MemorySessionStorage();
    tokens = MockTokenStore();
    events = [];
    when(() => repo.storage).thenReturn(storage);
    when(() => repo.tokens).thenReturn(tokens);
    sessions = SessionManager(
      openDatabase: (id) {
        events.add('open:$id');
        return AppDatabase(NativeDatabase.memory());
      },
      destroyDatabase: (id, db) async {
        events.add('destroy:$id');
        await db?.close();
      },
      bookingsFactory: (local) => BookingsRepository(dio: dioWith(ScriptedAdapter((_) => noNetwork)), local: local, connectivity: FakeConnectivity()),
    );
  });

  AuthBloc build({Stream<String>? lost}) => AuthBloc(repository: repo, sessions: sessions, sessionLost: lost);

  Future<void> settle() => pumpEventQueue();

  group('al abrir la app', () {
    test('sin sesión guardada, lleva al login', () async {
      when(() => repo.restore()).thenAnswer((_) async => null);
      final bloc = build()..add(const AuthStarted());
      await settle();

      expect(bloc.state.status, AuthStatus.unauthenticated);
      expect(events, isEmpty);
    });

    test('con sesión guardada, entra y abre la base local de esa cuenta', () async {
      when(() => repo.restore()).thenAnswer((_) async => const RestoredSession(ana, offline: false));
      final bloc = build()..add(const AuthStarted());
      await settle();

      expect(bloc.state.status, AuthStatus.authenticated);
      expect(bloc.state.user, ana);
      expect(bloc.state.offline, isFalse);
      expect(events, ['open:1']);
    });

    test('sin conexión pero con sesión guardada: entra en modo sin conexión (sus tickets guardados siguen disponibles)', () async {
      when(() => repo.restore()).thenAnswer((_) async => const RestoredSession(ana, offline: true));
      final bloc = build()..add(const AuthStarted());
      await settle();

      expect(bloc.state.status, AuthStatus.authenticated);
      expect(bloc.state.offline, isTrue);
      expect(sessions.current?.user, ana);
    });
  });

  group('iniciar sesión', () {
    test('éxito: autenticado y con su base abierta', () async {
      when(() => repo.login('ana@example.test', 'Clave123')).thenAnswer((_) async => ana);
      final bloc = build()..add(const AuthLoginRequested(' ana@example.test ', 'Clave123'));
      await settle();

      expect(bloc.state.status, AuthStatus.authenticated);
      expect(events, ['open:1']);
    });

    test('credenciales incorrectas: mensaje claro derivado del code, sin abrir nada', () async {
      when(() => repo.login(any(), any())).thenThrow(const ApiException(status: 401, code: 'invalid_credentials', message: 'x'));
      final bloc = build()..add(const AuthLoginRequested('ana@example.test', 'mala'));
      await settle();

      expect(bloc.state.status, AuthStatus.unauthenticated);
      expect(bloc.state.message, contains('no son correctos'));
      expect(bloc.state.busy, isFalse);
      expect(events, isEmpty);
    });

    test('sin red: lo dice', () async {
      when(() => repo.login(any(), any())).thenThrow(const ApiException.network());
      final bloc = build()..add(const AuthLoginRequested('a@x.test', 'x'));
      await settle();

      expect(bloc.state.message, contains('conexión'));
    });

    test('demasiados intentos: lo explica', () async {
      when(() => repo.login(any(), any())).thenThrow(const ApiException(status: 429, code: 'rate_limited', message: 'x'));
      final bloc = build()..add(const AuthLoginRequested('a@x.test', 'x'));
      await settle();

      expect(bloc.state.message, contains('Demasiados intentos'));
    });

    test('registro con email repetido: muestra el motivo', () async {
      when(() => repo.register(any(), any(), any())).thenThrow(const ApiException(status: 409, code: 'email_already_registered', message: 'x'));
      final bloc = build()..add(const AuthRegisterRequested('Ana', 'ana@example.test', 'Clave123'));
      await settle();

      expect(bloc.state.message, contains('Ya existe una cuenta'));
    });

    test('registro con campos inválidos: conserva los errores por campo del servidor', () async {
      when(() => repo.register(any(), any(), any())).thenThrow(const ApiException(
        status: 400,
        code: 'validation_failed',
        message: 'x',
        fieldErrors: {'password': ['Muy corta']},
      ));
      final bloc = build()..add(const AuthRegisterRequested('Ana', 'a@x.test', '1'));
      await settle();

      expect(bloc.state.fieldErrors['password'], ['Muy corta']);
    });
  });

  group('cerrar sesión y sesión perdida', () {
    Future<AuthBloc> signedIn({Stream<String>? lost}) async {
      when(() => repo.restore()).thenAnswer((_) async => const RestoredSession(ana, offline: false));
      final bloc = build(lost: lost)..add(const AuthStarted());
      await settle();
      events.clear();
      return bloc;
    }

    test('cerrar sesión borra la base local de la cuenta del dispositivo', () async {
      when(() => repo.logout()).thenAnswer((_) async {});
      final bloc = await signedIn();

      bloc.add(const AuthLogoutRequested());
      await settle();

      expect(bloc.state.status, AuthStatus.unauthenticated);
      expect(events, ['destroy:1']); // los tickets guardados no quedan en un teléfono compartido
      expect(sessions.current, isNull);
    });

    test('si el servidor ya no acepta la sesión: cierra, borra lo local y explica', () async {
      when(() => tokens.clear()).thenReturn(null);
      final lost = StreamController<String>.broadcast();
      final bloc = await signedIn(lost: lost.stream);

      lost.add('invalid_refresh_token');
      await settle();

      expect(bloc.state.status, AuthStatus.unauthenticated);
      expect(bloc.state.message, contains('sesión expiró'));
      expect(events, ['destroy:1']);
      await lost.close();
    });

    test('una reutilización del refresh token (posible robo) explica que se cerró por seguridad', () async {
      when(() => tokens.clear()).thenReturn(null);
      final lost = StreamController<String>.broadcast();
      final bloc = await signedIn(lost: lost.stream);

      lost.add('refresh_token_reused');
      await settle();

      expect(bloc.state.message, contains('Por seguridad'));
      await lost.close();
    });

    test('un aviso de sesión perdida estando ya sin sesión no hace nada', () async {
      when(() => repo.restore()).thenAnswer((_) async => null);
      final lost = StreamController<String>.broadcast();
      build(lost: lost.stream).add(const AuthStarted());
      await settle();

      lost.add('invalid_token');
      await settle();

      expect(events, isEmpty);
      await lost.close();
    });
  });

  group('SessionManager', () {
    test('iniciar con otra cuenta cierra primero la base de la anterior', () async {
      await sessions.open(ana);
      await sessions.open(const AppUser(id: 2, fullName: 'Luis', email: 'l@x.test', role: 'Customer'));

      expect(events, ['open:1', 'open:2']);
      expect(sessions.current?.user.id, 2);
    });

    test('abrir la misma cuenta dos veces reutiliza la base', () async {
      await sessions.open(ana);
      await sessions.open(ana.copyWith(fullName: 'Ana P.'));

      expect(events, ['open:1']);
      expect(sessions.current?.user.fullName, 'Ana P.');
    });

    test('require() sin sesión es un error de programación', () {
      expect(() => sessions.require(), throwsStateError);
    });
  });
}
