import 'dart:async';

import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/api/api_exception.dart';
import '../../../core/session/session_manager.dart';
import '../data/auth_models.dart';
import '../data/auth_repository.dart';

enum AuthStatus { unknown, authenticated, unauthenticated }

class AuthState extends Equatable {
  const AuthState({
    this.status = AuthStatus.unknown,
    this.user,
    this.offline = false,
    this.busy = false,
    this.message,
    this.fieldErrors = const {},
  });

  final AuthStatus status;
  final AppUser? user;

  /// La sesión se recuperó sin poder contactar al servidor.
  final bool offline;
  final bool busy;
  final String? message;
  final Map<String, List<String>> fieldErrors;

  AuthState copyWith({AuthStatus? status, AppUser? user, bool? offline, bool? busy, String? message, Map<String, List<String>>? fieldErrors, bool clearMessage = false}) =>
      AuthState(
        status: status ?? this.status,
        user: user ?? this.user,
        offline: offline ?? this.offline,
        busy: busy ?? this.busy,
        message: clearMessage ? null : (message ?? this.message),
        fieldErrors: fieldErrors ?? (clearMessage ? const {} : this.fieldErrors),
      );

  @override
  List<Object?> get props => [status, user, offline, busy, message, fieldErrors];
}

sealed class AuthEvent {
  const AuthEvent();
}

class AuthStarted extends AuthEvent {
  const AuthStarted();
}

class AuthLoginRequested extends AuthEvent {
  const AuthLoginRequested(this.email, this.password);
  final String email;
  final String password;
}

class AuthRegisterRequested extends AuthEvent {
  const AuthRegisterRequested(this.fullName, this.email, this.password);
  final String fullName;
  final String email;
  final String password;
}

class AuthLogoutRequested extends AuthEvent {
  const AuthLogoutRequested();
}

/// El servidor ya no acepta la sesión (refresh token inválido o reutilizado, cuenta desactivada…).
class AuthSessionLost extends AuthEvent {
  const AuthSessionLost(this.code);
  final String code;
}

class AuthUserUpdated extends AuthEvent {
  const AuthUserUpdated(this.user);
  final AppUser user;
}

class AuthBloc extends Bloc<AuthEvent, AuthState> {
  AuthBloc({required this.repository, required this.sessions, Stream<String>? sessionLost}) : super(const AuthState()) {
    on<AuthStarted>(_onStarted);
    on<AuthLoginRequested>(_onLogin);
    on<AuthRegisterRequested>(_onRegister);
    on<AuthLogoutRequested>(_onLogout);
    on<AuthSessionLost>(_onSessionLost);
    on<AuthUserUpdated>((event, emit) => emit(state.copyWith(user: event.user)));

    _lostSubscription = sessionLost?.listen((code) => add(AuthSessionLost(code)));
  }

  final AuthRepository repository;
  final SessionManager sessions;
  StreamSubscription<String>? _lostSubscription;

  Future<void> _onStarted(AuthStarted event, Emitter<AuthState> emit) async {
    final restored = await repository.restore();
    if (restored == null) {
      emit(const AuthState(status: AuthStatus.unauthenticated));
      return;
    }
    await sessions.open(restored.user);
    emit(AuthState(status: AuthStatus.authenticated, user: restored.user, offline: restored.offline));
  }

  Future<void> _onLogin(AuthLoginRequested event, Emitter<AuthState> emit) =>
      _authenticate(emit, () => repository.login(event.email.trim(), event.password));

  Future<void> _onRegister(AuthRegisterRequested event, Emitter<AuthState> emit) =>
      _authenticate(emit, () => repository.register(event.fullName.trim(), event.email.trim(), event.password));

  Future<void> _authenticate(Emitter<AuthState> emit, Future<AppUser> Function() action) async {
    emit(state.copyWith(busy: true, clearMessage: true));
    try {
      final user = await action();
      await sessions.open(user);
      emit(AuthState(status: AuthStatus.authenticated, user: user));
    } on ApiException catch (e) {
      emit(AuthState(status: AuthStatus.unauthenticated, message: _messageFor(e), fieldErrors: e.fieldErrors));
    }
  }

  Future<void> _onLogout(AuthLogoutRequested event, Emitter<AuthState> emit) async {
    await repository.logout();
    await sessions.close(destroy: true); // los datos locales de la cuenta se borran del dispositivo
    emit(const AuthState(status: AuthStatus.unauthenticated));
  }

  Future<void> _onSessionLost(AuthSessionLost event, Emitter<AuthState> emit) async {
    if (state.status != AuthStatus.authenticated) return;
    repository.tokens.clear();
    await repository.storage.clear();
    await sessions.close(destroy: true);
    emit(AuthState(
      status: AuthStatus.unauthenticated,
      message: event.code == 'refresh_token_reused'
          ? 'Por seguridad cerramos tu sesión. Vuelve a iniciar sesión.'
          : 'Tu sesión expiró. Inicia sesión nuevamente.',
    ));
  }

  /// Los mensajes se derivan del `code`, nunca del texto que devuelve el servidor.
  static String _messageFor(ApiException e) => switch (e.code) {
        'invalid_credentials' => 'El email o la contraseña no son correctos.',
        'email_already_registered' => 'Ya existe una cuenta con ese email.',
        'validation_failed' => 'Revisa los datos ingresados.',
        'rate_limited' => 'Demasiados intentos. Espera un momento e inténtalo de nuevo.',
        'network_error' => 'No hay conexión con el servidor. Revisa tu conexión e inténtalo de nuevo.',
        _ => 'No se pudo completar la operación. Inténtalo de nuevo.',
      };

  @override
  Future<void> close() async {
    await _lostSubscription?.cancel();
    return super.close();
  }
}
