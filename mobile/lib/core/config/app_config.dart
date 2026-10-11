import 'package:flutter/foundation.dart';

/// Configuración de la app. La URL de la API se puede fijar al compilar:
/// `flutter run --dart-define=API_BASE_URL=http://192.168.1.10:8090`.
class AppConfig {
  const AppConfig({required this.apiBaseUrl});

  final String apiBaseUrl;

  factory AppConfig.fromEnvironment() {
    const fromDefine = String.fromEnvironment('API_BASE_URL');
    if (fromDefine.isNotEmpty) return const AppConfig(apiBaseUrl: fromDefine);

    // 10.0.2.2 es la máquina anfitriona vista desde el emulador de Android.
    final host = defaultTargetPlatform == TargetPlatform.android ? '10.0.2.2' : 'localhost';
    return AppConfig(apiBaseUrl: 'http://$host:8090');
  }

  String get apiV1 => '$apiBaseUrl/api/v1/';
  String get healthUrl => '$apiBaseUrl/health';
  String get hubUrl => '$apiBaseUrl/hubs/seats';
}
