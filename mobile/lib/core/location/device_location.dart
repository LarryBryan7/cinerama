import 'dart:async';

import 'package:equatable/equatable.dart';
import 'package:geolocator/geolocator.dart';

/// Un punto del mapa (latitud y longitud en grados).
class GeoPoint extends Equatable {
  const GeoPoint(this.latitude, this.longitude);

  final double latitude;
  final double longitude;

  @override
  List<Object?> get props => [latitude, longitude];
}

/// Por qué hay o no hay ubicación. La pantalla solo necesita distinguir "tengo la posición" de "no la tengo y por qué".
enum LocationAccess {
  /// Todavía no se ha preguntado.
  unknown,

  /// Hay permiso y se obtuvo la posición.
  granted,

  /// La persona no concedió el permiso (se puede volver a pedir).
  denied,

  /// La persona eligió "no volver a preguntar": solo se puede cambiar en los ajustes del dispositivo.
  deniedForever,

  /// El servicio de ubicación del dispositivo está apagado.
  serviceOff,

  /// Hay permiso, pero no se pudo obtener la posición (sin señal, tiempo agotado).
  unavailable,
}

class LocationResult extends Equatable {
  const LocationResult(this.access, [this.point]);

  final LocationAccess access;
  final GeoPoint? point;

  @override
  List<Object?> get props => [access, point];
}

/// Ubicación aproximada del dispositivo. Se usa SOLO para calcular distancias a los cines EN el dispositivo:
/// nunca se envía al servidor ni se guarda.
abstract class DeviceLocation {
  /// Pide el permiso si hace falta (cuando [askPermission] es verdadero) y devuelve la posición, o el motivo por el que no hay.
  Future<LocationResult> locate({bool askPermission = true});

  /// Abre los ajustes de la app (para cuando el permiso quedó bloqueado).
  Future<void> openSettings();
}

/// Implementación con el GPS / red del dispositivo. Pide precisión baja: alcanza para saber qué cine queda cerca y no
/// gasta batería ni pide más de lo necesario.
class GeolocatorDeviceLocation implements DeviceLocation {
  const GeolocatorDeviceLocation({this.timeout = const Duration(seconds: 8)});

  final Duration timeout;

  @override
  Future<LocationResult> locate({bool askPermission = true}) async {
    try {
      if (!await Geolocator.isLocationServiceEnabled()) return const LocationResult(LocationAccess.serviceOff);

      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied && askPermission) {
        permission = await Geolocator.requestPermission();
      }
      if (permission == LocationPermission.deniedForever) return const LocationResult(LocationAccess.deniedForever);
      if (permission == LocationPermission.denied || permission == LocationPermission.unableToDetermine) {
        return const LocationResult(LocationAccess.denied);
      }

      final position = await Geolocator.getCurrentPosition(
        locationSettings: LocationSettings(accuracy: LocationAccuracy.low, timeLimit: timeout),
      ).timeout(timeout + const Duration(seconds: 2));
      return LocationResult(LocationAccess.granted, GeoPoint(position.latitude, position.longitude));
    } on TimeoutException {
      return _lastKnown();
    } on Exception {
      return _lastKnown();
    }
  }

  /// Si no hubo posición nueva a tiempo, la última conocida sirve (la distancia a un cine no cambia en minutos).
  Future<LocationResult> _lastKnown() async {
    try {
      final last = await Geolocator.getLastKnownPosition();
      if (last != null) return LocationResult(LocationAccess.granted, GeoPoint(last.latitude, last.longitude));
    } on Exception {
      // Sin última posición conocida: se informa como no disponible.
    }
    return const LocationResult(LocationAccess.unavailable);
  }

  @override
  Future<void> openSettings() async {
    await Geolocator.openAppSettings();
  }
}
