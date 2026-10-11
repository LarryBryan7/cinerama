import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../features/auth/data/auth_models.dart';

/// Dónde se guarda la sesión entre ejecuciones de la app. El refresh token va al almacén seguro del sistema
/// (Keychain / Keystore), nunca a SharedPreferences ni a la base local.
abstract class SessionStorage {
  Future<String?> readRefreshToken();
  Future<AppUser?> readUser();
  Future<void> save({required String refreshToken, required AppUser user});
  Future<void> saveRefreshToken(String refreshToken);
  Future<void> clear();
}

class SecureSessionStorage implements SessionStorage {
  SecureSessionStorage([FlutterSecureStorage? storage]) : _storage = storage ?? const FlutterSecureStorage();

  static const _refreshKey = 'cinerama.refresh_token';
  static const _userKey = 'cinerama.user';

  final FlutterSecureStorage _storage;

  @override
  Future<String?> readRefreshToken() => _storage.read(key: _refreshKey);

  @override
  Future<AppUser?> readUser() async {
    final raw = await _storage.read(key: _userKey);
    if (raw == null) return null;
    try {
      return AppUser.fromJson(jsonDecode(raw) as Map<String, dynamic>);
    } on FormatException {
      return null;
    }
  }

  @override
  Future<void> save({required String refreshToken, required AppUser user}) async {
    await _storage.write(key: _refreshKey, value: refreshToken);
    await _storage.write(key: _userKey, value: jsonEncode(user.toJson()));
  }

  @override
  Future<void> saveRefreshToken(String refreshToken) => _storage.write(key: _refreshKey, value: refreshToken);

  @override
  Future<void> clear() async {
    await _storage.delete(key: _refreshKey);
    await _storage.delete(key: _userKey);
  }
}

/// Para pruebas: en memoria.
class MemorySessionStorage implements SessionStorage {
  String? refreshToken;
  AppUser? user;

  @override
  Future<String?> readRefreshToken() async => refreshToken;

  @override
  Future<AppUser?> readUser() async => user;

  @override
  Future<void> save({required String refreshToken, required AppUser user}) async {
    this.refreshToken = refreshToken;
    this.user = user;
  }

  @override
  Future<void> saveRefreshToken(String refreshToken) async => this.refreshToken = refreshToken;

  @override
  Future<void> clear() async {
    refreshToken = null;
    user = null;
  }
}
