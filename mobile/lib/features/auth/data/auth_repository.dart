import 'package:dio/dio.dart';

import '../../../core/api/api_client.dart';
import '../../../core/api/api_exception.dart';
import '../../../core/api/session_storage.dart';
import '../../../core/api/token_store.dart';
import 'auth_models.dart';

/// Resultado de recuperar la sesión al abrir la app.
class RestoredSession {
  const RestoredSession(this.user, {required this.offline});

  final AppUser user;

  /// `true` si no se pudo contactar al servidor y se usa el perfil guardado: la sesión sigue, pero las operaciones
  /// que exigen conexión quedan bloqueadas hasta que vuelva.
  final bool offline;
}

class AuthRepository {
  AuthRepository({required this.dio, required this.tokens, required this.storage});

  final Dio dio;
  final TokenStore tokens;
  final SessionStorage storage;

  Future<AppUser> login(String email, String password) async {
    final response = await guardApi(() => dio.post<Map<String, dynamic>>('auth/login', data: {'email': email, 'password': password}));
    return _startSession(AuthSession.fromJson(response.data!));
  }

  Future<AppUser> register(String fullName, String email, String password) async {
    final response = await guardApi(
        () => dio.post<Map<String, dynamic>>('auth/register', data: {'fullName': fullName, 'email': email, 'password': password}));
    return _startSession(AuthSession.fromJson(response.data!));
  }

  Future<AppUser> _startSession(AuthSession session) async {
    tokens.setSession(session);
    await storage.save(refreshToken: session.refreshToken, user: session.user);
    return session.user;
  }

  /// Recupera la sesión guardada. `null` si no hay ninguna o si el servidor ya no la acepta.
  Future<RestoredSession?> restore() async {
    if (await storage.readRefreshToken() == null) return null;

    try {
      await tokens.refresh();
      final user = await storage.readUser();
      return user == null ? null : RestoredSession(user, offline: false);
    } on SessionExpiredException {
      return null;
    } on ApiException {
      // Sin red (o servidor caído): se conserva la sesión con el perfil guardado, en modo sin conexión.
      final user = await storage.readUser();
      return user == null ? null : RestoredSession(user, offline: true);
    }
  }

  Future<AppUser> updateName(String fullName) async {
    final response = await guardApi(() => dio.put<Map<String, dynamic>>('me', data: {'fullName': fullName}));
    final user = AppUser.fromJson(response.data!);
    final refreshToken = await storage.readRefreshToken();
    if (refreshToken != null) await storage.save(refreshToken: refreshToken, user: user);
    return user;
  }

  /// Cierra la sesión. El borrado local ocurre SIEMPRE; avisar al servidor (revocar el refresh token) es lo mejor posible.
  Future<void> logout() async {
    try {
      if (await tokens.validAccessToken() != null) {
        final refreshToken = await storage.readRefreshToken(); // se lee después: la renovación lo rota
        await dio.post<void>('auth/logout', data: {'refreshToken': refreshToken});
      }
    } on Object {
      // Sin red o sesión ya inválida: da igual, igualmente se cierra localmente.
    }
    tokens.clear();
    await storage.clear();
  }
}
