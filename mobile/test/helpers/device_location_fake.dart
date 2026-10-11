import 'package:cinerama_mobile/core/location/device_location.dart';

/// Ubicación del dispositivo controlada desde el test.
class FakeDeviceLocation implements DeviceLocation {
  FakeDeviceLocation([this.result = const LocationResult(LocationAccess.granted, GeoPoint(-9.0745, -78.5936))]);

  LocationResult result;
  int calls = 0;
  final asked = <bool>[];
  int settingsOpened = 0;

  @override
  Future<LocationResult> locate({bool askPermission = true}) async {
    calls++;
    asked.add(askPermission);
    return result;
  }

  @override
  Future<void> openSettings() async => settingsOpened++;
}

/// Posición cerca de Chimbote (a unos 120 m del cine "Cinerama - Chimbote" de los datos de prueba).
const nearChimbote = GeoPoint(-9.0750, -78.5940);

/// Mountain View, California: la ubicación por defecto del emulador de Android.
const mountainView = GeoPoint(37.4220, -122.0841);
