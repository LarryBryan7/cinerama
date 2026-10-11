import 'dart:math' as math;

import 'device_location.dart';

/// Cálculos geográficos sencillos. Todo ocurre en el dispositivo.
class Geo {
  const Geo._();

  static const _earthRadiusKm = 6371.0088;

  /// Distancia en kilómetros sobre la superficie (fórmula de haversine) entre [from] y un punto dado.
  static double distanceKm(GeoPoint from, double latitude, double longitude) {
    final dLat = _rad(latitude - from.latitude);
    final dLon = _rad(longitude - from.longitude);
    final a = math.pow(math.sin(dLat / 2), 2) + math.cos(_rad(from.latitude)) * math.cos(_rad(latitude)) * math.pow(math.sin(dLon / 2), 2);
    return 2 * _earthRadiusKm * math.asin(math.min(1.0, math.sqrt(a)));
  }

  static double _rad(double degrees) => degrees * math.pi / 180;
}
