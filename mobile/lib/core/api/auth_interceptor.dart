import 'package:dio/dio.dart';

import 'api_exception.dart';
import 'token_store.dart';

/// Agrega el access token a cada solicitud y, si el servidor responde `401 token_expired`, renueva la sesión una vez
/// y repite la solicitud sin que la persona lo note. Cualquier otro 401 (token inválido, sin sesión) significa que la
/// sesión se perdió: no se intenta renovar (ADR-007).
class AuthInterceptor extends Interceptor {
  AuthInterceptor({required this.tokenStore, required this.dio, required this.onSessionLost});

  final TokenStore tokenStore;
  final Dio dio; // el mismo cliente, para repetir la solicitud original
  final void Function(String code) onSessionLost;

  static const _retriedKey = 'cinerama.retried';
  static const _publicPaths = ['auth/login', 'auth/register', 'auth/refresh'];

  bool _isPublic(RequestOptions options) => _publicPaths.any(options.path.endsWith);

  @override
  Future<void> onRequest(RequestOptions options, RequestInterceptorHandler handler) async {
    if (_isPublic(options)) return handler.next(options);

    try {
      final token = await tokenStore.validAccessToken();
      if (token != null) options.headers['Authorization'] = 'Bearer $token';
      handler.next(options);
    } on SessionExpiredException catch (e) {
      onSessionLost(e.code);
      handler.reject(DioException(requestOptions: options, error: e, type: DioExceptionType.cancel));
    } on ApiException catch (e) {
      handler.reject(DioException(requestOptions: options, error: e, type: DioExceptionType.connectionError));
    }
  }

  @override
  Future<void> onError(DioException err, ErrorInterceptorHandler handler) async {
    final options = err.requestOptions;
    if (err.response?.statusCode != 401 || _isPublic(options)) return handler.next(err);

    final code = (err.response?.data is Map<String, dynamic>) ? (err.response!.data as Map<String, dynamic>)['code'] : null;

    if (code != 'token_expired' || options.extra[_retriedKey] == true) {
      onSessionLost(code is String ? code : 'unauthorized');
      return handler.next(err);
    }

    try {
      final token = await tokenStore.refresh();
      options.extra[_retriedKey] = true;
      options.headers['Authorization'] = 'Bearer $token';
      handler.resolve(await dio.fetch<dynamic>(options));
    } on SessionExpiredException catch (e) {
      onSessionLost(e.code);
      handler.next(err);
    } on ApiException {
      handler.next(err); // sin red: se informa el error original, la sesión se conserva
    } on DioException catch (e) {
      handler.next(e);
    }
  }
}
