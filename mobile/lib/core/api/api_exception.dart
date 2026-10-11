import 'package:dio/dio.dart';

/// Un asiento en conflicto, tal como lo informa la API (`conflicts` en el ProblemDetails).
class SeatConflict {
  const SeatConflict(this.seatId, this.label);

  final int seatId;
  final String label;
}

/// Error de la API ya interpretado. Los clientes deciden por [code], nunca por el texto del mensaje (ADR-007).
class ApiException implements Exception {
  const ApiException({
    required this.status,
    required this.code,
    required this.message,
    this.conflicts = const [],
    this.fieldErrors = const {},
  });

  /// Sin respuesta del servidor: sin red, tiempo agotado o servidor caído.
  const ApiException.network([this.message = 'No hay conexión con el servidor.'])
      : status = 0,
        code = 'network_error',
        conflicts = const [],
        fieldErrors = const {};

  final int status;
  final String code;
  final String message;
  final List<SeatConflict> conflicts;
  final Map<String, List<String>> fieldErrors;

  bool get isNetwork => status == 0;
  bool get isServerError => status >= 500;

  /// ¿Vale la pena reintentar la misma solicitud? Solo si nunca llegó o el servidor falló: jamás ante un 4xx.
  bool get isRetryable => isNetwork || isServerError;

  factory ApiException.fromDio(DioException error) {
    final response = error.response;
    if (response == null) return const ApiException.network();

    final data = response.data;
    if (data is Map<String, dynamic>) {
      return ApiException(
        status: response.statusCode ?? 0,
        code: (data['code'] as String?) ?? 'error',
        message: (data['detail'] as String?) ?? (data['title'] as String?) ?? 'Ocurrió un error.',
        conflicts: [
          for (final c in (data['conflicts'] as List<dynamic>? ?? const []))
            SeatConflict((c as Map<String, dynamic>)['seatId'] as int, c['label'] as String),
        ],
        fieldErrors: {
          for (final e in (data['errors'] as Map<String, dynamic>? ?? const {}).entries)
            e.key: [for (final m in e.value as List<dynamic>) m as String],
        },
      );
    }

    return ApiException(status: response.statusCode ?? 0, code: 'error', message: 'Ocurrió un error inesperado.');
  }

  @override
  String toString() => 'ApiException($status, $code)';
}

/// La operación exige conexión real con el servidor y no la hay (ADR-010): no se encola nada.
class OfflineException implements Exception {
  const OfflineException();

  @override
  String toString() => 'OfflineException';
}

/// La sesión ya no sirve (refresh token inválido, reutilizado o cuenta desactivada): hay que volver a iniciar sesión.
class SessionExpiredException implements Exception {
  const SessionExpiredException(this.code);

  final String code;

  @override
  String toString() => 'SessionExpiredException($code)';
}
