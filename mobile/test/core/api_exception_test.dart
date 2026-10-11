import 'package:cinerama_mobile/core/api/api_exception.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

DioException _error(int status, Object? body) => DioException(
      requestOptions: RequestOptions(path: '/x'),
      response: Response(requestOptions: RequestOptions(path: '/x'), statusCode: status, data: body),
      type: DioExceptionType.badResponse,
    );

void main() {
  test('interpreta el code, el estado y el detalle del ProblemDetails', () {
    final e = ApiException.fromDio(_error(409, {'status': 409, 'code': 'seat_unavailable', 'title': 'Conflicto', 'detail': 'Ocupado'}));

    expect(e.status, 409);
    expect(e.code, 'seat_unavailable');
    expect(e.message, 'Ocupado');
    expect(e.isNetwork, isFalse);
  });

  test('lee la lista de asientos en conflicto', () {
    final e = ApiException.fromDio(_error(409, {
      'code': 'seat_unavailable',
      'conflicts': [
        {'seatId': 7, 'label': 'F7'},
        {'seatId': 8, 'label': 'F8'},
      ],
    }));

    expect(e.conflicts.map((c) => c.label), ['F7', 'F8']);
    expect(e.conflicts.first.seatId, 7);
  });

  test('lee los errores por campo de una validación', () {
    final e = ApiException.fromDio(_error(400, {
      'code': 'validation_failed',
      'errors': {
        'password': ['Muy corta', 'Sin números'],
      },
    }));

    expect(e.fieldErrors['password'], ['Muy corta', 'Sin números']);
  });

  test('sin respuesta del servidor es un error de red (reintentable)', () {
    final e = ApiException.fromDio(DioException.connectionError(requestOptions: RequestOptions(path: '/x'), reason: 'sin red'));

    expect(e.isNetwork, isTrue);
    expect(e.code, 'network_error');
    expect(e.isRetryable, isTrue);
  });

  test('un 4xx nunca se reintenta; un 5xx sí', () {
    expect(ApiException.fromDio(_error(409, {'code': 'x'})).isRetryable, isFalse);
    expect(ApiException.fromDio(_error(422, {'code': 'x'})).isRetryable, isFalse);
    expect(ApiException.fromDio(_error(500, {'code': 'internal_error'})).isRetryable, isTrue);
  });

  test('un cuerpo que no es ProblemDetails no rompe la interpretación', () {
    final e = ApiException.fromDio(_error(502, '<html>Bad gateway</html>'));

    expect(e.status, 502);
    expect(e.code, 'error');
  });
}
