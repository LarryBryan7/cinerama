import 'dart:io';

/// Reloj alineado con el del servidor. El vencimiento de un bloqueo (`lockedUntil`) lo fija el servidor: si el reloj
/// del teléfono está adelantado o atrasado, la cuenta regresiva mentiría. Se corrige con la cabecera `Date` de cada respuesta.
class ServerClock {
  ServerClock({DateTime Function()? deviceNow}) : _deviceNow = deviceNow ?? DateTime.now;

  final DateTime Function() _deviceNow;
  Duration _offset = Duration.zero;

  DateTime now() => _deviceNow().add(_offset);

  Duration get offset => _offset;

  /// [dateHeader] es una fecha HTTP (RFC 1123), por ejemplo `Fri, 02 Oct 2026 20:19:06 GMT`.
  void sync(String? dateHeader) {
    if (dateHeader == null) return;
    try {
      _offset = HttpDate.parse(dateHeader).difference(_deviceNow());
    } on HttpException {
      // Un encabezado mal formado no debe romper ninguna solicitud.
    }
  }
}
