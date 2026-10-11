import 'package:dio/dio.dart';

import '../config/app_config.dart';
import 'api_exception.dart';
import 'auth_interceptor.dart';
import 'server_clock.dart';
import 'token_store.dart';

/// Construye los clientes HTTP. Hay dos: el "desnudo" (sin interceptores, para renovar la sesión y consultar /health)
/// y el de la API, con sesión automática y reloj del servidor.
class ApiClient {
  const ApiClient._();

  static Dio newBareDio(AppConfig config, {HttpClientAdapter? adapter}) {
    final dio = Dio(BaseOptions(
      baseUrl: config.apiV1,
      connectTimeout: const Duration(seconds: 8),
      receiveTimeout: const Duration(seconds: 20),
      sendTimeout: const Duration(seconds: 20),
      contentType: Headers.jsonContentType,
      // Dio lanza DioException ante un 4xx/5xx; guardApi lo traduce a ApiException con el code del ProblemDetails.
    ));
    if (adapter != null) dio.httpClientAdapter = adapter;
    return dio;
  }

  static Dio newAuthedDio({
    required AppConfig config,
    required TokenStore tokens,
    required ServerClock clock,
    required void Function(String code) onSessionLost,
    HttpClientAdapter? adapter,
  }) {
    final dio = newBareDio(config, adapter: adapter);
    dio.interceptors
      ..add(InterceptorsWrapper(onResponse: (response, handler) {
        clock.sync(response.headers.value('date'));
        handler.next(response);
      }))
      ..add(AuthInterceptor(tokenStore: tokens, dio: dio, onSessionLost: onSessionLost));
    return dio;
  }
}

/// Ejecuta una llamada de Dio y traduce sus errores a [ApiException] / [SessionExpiredException].
Future<T> guardApi<T>(Future<T> Function() call) async {
  try {
    return await call();
  } on DioException catch (e) {
    final inner = e.error;
    if (inner is SessionExpiredException) throw inner;
    if (inner is ApiException) throw inner;
    throw ApiException.fromDio(e);
  }
}
