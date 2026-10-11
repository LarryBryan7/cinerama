import 'package:intl/intl.dart';

class Formatters {
  const Formatters._();

  /// La moneda es configurable en el servidor; la app muestra soles (zona horaria del cine: Lima).
  static String money(num value) => 'S/ ${value.toStringAsFixed(2)}';

  /// Hora local del dispositivo a partir de un instante UTC del servidor.
  static String dateTime(DateTime utc) => DateFormat('EEE d MMM · HH:mm', 'es').format(utc.toLocal());

  static String day(DateTime utc) => DateFormat('EEEE d MMMM', 'es').format(utc.toLocal());

  static String time(DateTime utc) => DateFormat('HH:mm', 'es').format(utc.toLocal());

  static String duration(int minutes) => '${minutes ~/ 60} h ${(minutes % 60).toString().padLeft(2, '0')} min';

  /// Formato de la tarjeta de cartelera según Figma: "1h 57m" (sin ceros de relleno).
  static String durationCompact(int minutes) {
    final h = minutes ~/ 60, m = minutes % 60;
    return [if (h > 0) '${h}h', if (m > 0) '${m}m'].join(' ');
  }

  static String countdown(Duration d) {
    final s = d.inSeconds.clamp(0, 99 * 60 + 59);
    return '${(s ~/ 60).toString().padLeft(2, '0')}:${(s % 60).toString().padLeft(2, '0')}';
  }

  static String ago(DateTime utc, DateTime now) {
    final d = now.difference(utc);
    if (d.inMinutes < 1) return 'hace un momento';
    if (d.inMinutes < 60) return 'hace ${d.inMinutes} min';
    if (d.inHours < 24) return 'hace ${d.inHours} h';
    return 'hace ${d.inDays} d';
  }

  /// Día de una opinión según el calendario local, no según horas transcurridas: lo de anoche es "Ayer" aunque hayan pasado 3 h.
  static String relativeDay(DateTime utc, DateTime now) {
    final local = utc.toLocal();
    final days = DateTime(now.year, now.month, now.day).difference(DateTime(local.year, local.month, local.day)).inDays;
    if (days <= 0) return 'Hoy';
    if (days == 1) return 'Ayer';
    if (days < 30) return 'hace $days días';
    return DateFormat('d MMM y', 'es').format(local);
  }

  /// Antigüedad de un comentario, como en la hoja de comentarios de Figma: "2d", "1 sem". Figma solo muestra estos dos
  /// ejemplos; el resto de las unidades sigue su estilo (abreviadas, sin "hace").
  static String commentAge(DateTime utc, DateTime now) {
    final d = now.difference(utc);
    if (d.inMinutes < 1) return 'ahora';
    if (d.inMinutes < 60) return '${d.inMinutes}min';
    if (d.inHours < 24) return '${d.inHours}h';
    if (d.inDays < 7) return '${d.inDays}d';
    if (d.inDays < 30) return '${d.inDays ~/ 7} sem';
    if (d.inDays < 365) return '${d.inDays ~/ 30} mes';
    return '${d.inDays ~/ 365} a';
  }

  /// Cantidad corta para el "me gusta" de un comentario: 7, 999, 1,2 mil, 15 mil, 2,4 M. Cabe en el espacio de ~20 px del diseño.
  static String compactCount(int n) {
    if (n < 1000) return '$n';
    String trim(double v) => v.toStringAsFixed(1).replaceAll('.0', '').replaceAll('.', ',');
    if (n < 10000) return '${trim(n / 1000)} mil';
    if (n < 1000000) return '${n ~/ 1000} mil';
    return '${trim(n / 1000000)} M';
  }

  /// "11 oct": una fecha del calendario del cine (estreno, preventa).
  static String shortDate(DateTime date) => DateFormat('d MMM', 'es').format(date);

  /// Distancia legible: "850 m" por debajo del kilómetro, "7.2 km" hasta 100 km y "1 250 km" a partir de ahí.
  static String distance(double km) {
    if (km < 1) return '${((km * 1000) / 10).round() * 10} m';
    if (km < 100) return '${km.toStringAsFixed(1)} km';
    final rounded = km.round().toString();
    final grouped = rounded.replaceAllMapped(RegExp(r'\B(?=(\d{3})+(?!\d))'), (_) => ' ');
    return '$grouped km';
  }
}
