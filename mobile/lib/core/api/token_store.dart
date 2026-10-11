import 'package:dio/dio.dart';

import '../../features/auth/data/auth_models.dart';
import 'api_exception.dart';
import 'session_storage.dart';

/// Tokens de la sesión. El access token vive SOLO en memoria; el refresh token, en el almacén seguro.
///
/// La renovación es de un solo vuelo (*single-flight*): si tres solicitudes reciben 401 a la vez, hay un único
/// `POST /auth/refresh` y las tres esperan su resultado. Con rotación de refresh tokens (ADR-007) esto no es una
/// optimización sino una necesidad: dos refresh simultáneos con el mismo token se verían como una reutilización
/// y el servidor cerraría la sesión.
class TokenStore {
  TokenStore({required this.storage, required Dio bareDio, DateTime Function()? now})
      : _dio = bareDio,
        _now = now ?? DateTime.now;

  final SessionStorage storage;
  final Dio _dio; // sin interceptores: el refresh no puede depender de sí mismo
  final DateTime Function() _now;

  String? _accessToken;
  DateTime? _accessExpiresAt;
  Future<String>? _refreshInFlight;

  /// Cuántas veces se llamó al servidor para renovar (lo usan las pruebas del single-flight).
  int refreshCalls = 0;

  String? get currentAccessToken => _accessToken;

  void setSession(AuthSession session) {
    _accessToken = session.accessToken;
    _accessExpiresAt = session.accessTokenExpiresAt;
  }

  void clear() {
    _accessToken = null;
    _accessExpiresAt = null;
    _refreshInFlight = null;
  }

  bool get _accessIsFresh =>
      _accessToken != null && _accessExpiresAt != null && _accessExpiresAt!.isAfter(_now().add(const Duration(seconds: 30)));

  /// Un access token vigente, renovándolo si hace falta. Lo usan las solicitudes y la conexión de tiempo real.
  /// Devuelve `null` si no hay sesión. Lanza [SessionExpiredException] si la sesión ya no sirve y [ApiException]
  /// de red si no se pudo contactar al servidor (en cuyo caso la sesión se conserva).
  Future<String?> validAccessToken() async {
    if (_accessIsFresh) return _accessToken;
    if (await storage.readRefreshToken() == null) return null;
    return refresh();
  }

  Future<String> refresh() => _refreshInFlight ??= _doRefresh().whenComplete(() => _refreshInFlight = null);

  Future<String> _doRefresh() async {
    final refreshToken = await storage.readRefreshToken();
    if (refreshToken == null) throw const SessionExpiredException('no_session');

    refreshCalls++;
    try {
      final response = await _dio.post<Map<String, dynamic>>('auth/refresh', data: {'refreshToken': refreshToken});
      final session = AuthSession.fromJson(response.data!);
      setSession(session);
      await storage.save(refreshToken: session.refreshToken, user: session.user);
      return session.accessToken;
    } on DioException catch (e) {
      final error = ApiException.fromDio(e);
      if (error.isNetwork || error.isServerError) throw error; // sin red no se pierde la sesión
      await storage.clear();
      clear();
      throw SessionExpiredException(error.code); // invalid_refresh_token, refresh_token_reused, account_disabled…
    }
  }
}
