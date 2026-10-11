import 'dart:io';

import 'package:drift_flutter/drift_flutter.dart';
import 'package:path_provider/path_provider.dart';

import 'app_database.dart';

/// Abre y destruye la base local de UNA cuenta. Se abre al iniciar sesión y se borra al cerrarla: el ticket guardado
/// de una persona no queda en un teléfono compartido.
class UserDatabase {
  static String _name(int userId) => 'cinerama_$userId';

  static AppDatabase open(int userId) => AppDatabase(driftDatabase(name: _name(userId)));

  static Future<void> destroy(int userId, AppDatabase? open) async {
    await open?.close();
    final dir = await getApplicationDocumentsDirectory();
    for (final suffix in ['.sqlite', '.sqlite-wal', '.sqlite-shm', '.sqlite-journal']) {
      final file = File('${dir.path}/${_name(userId)}$suffix');
      if (await file.exists()) await file.delete();
    }
  }
}
