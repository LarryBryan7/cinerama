import '../../features/auth/data/auth_models.dart';
import '../../features/bookings/data/bookings_local_store.dart';
import '../../features/bookings/data/bookings_repository.dart';
import '../db/app_database.dart';

/// Todo lo que pertenece a UNA cuenta con la sesión iniciada: su base local y los repositorios que la usan.
class UserSession {
  UserSession({required this.user, required this.database, required this.bookings});

  final AppUser user;
  final AppDatabase database;
  final BookingsRepository bookings;
}

typedef OpenDatabase = AppDatabase Function(int userId);
typedef DestroyDatabase = Future<void> Function(int userId, AppDatabase? open);

/// Abre los recursos de la cuenta al iniciar sesión y los destruye al cerrarla. Así los datos de una cuenta no se
/// mezclan con los de otra ni quedan en un teléfono compartido (ADR-010).
class SessionManager {
  SessionManager({
    required this.openDatabase,
    required this.destroyDatabase,
    required this.bookingsFactory,
  });

  final OpenDatabase openDatabase;
  final DestroyDatabase destroyDatabase;
  final BookingsRepository Function(BookingsLocalStore store) bookingsFactory;

  UserSession? _current;

  UserSession? get current => _current;

  UserSession require() => _current ?? (throw StateError('No hay una sesión abierta.'));

  Future<UserSession> open(AppUser user) async {
    final existing = _current;
    if (existing != null && existing.user.id == user.id) {
      _current = UserSession(user: user, database: existing.database, bookings: existing.bookings);
      return _current!;
    }
    await close(destroy: false);

    final database = openDatabase(user.id);
    _current = UserSession(user: user, database: database, bookings: bookingsFactory(BookingsLocalStore(database)));
    return _current!;
  }

  /// Cierra la base de la cuenta; con [destroy], además borra su archivo del dispositivo.
  Future<void> close({required bool destroy}) async {
    final session = _current;
    _current = null;
    if (session == null) return;

    if (destroy) {
      await destroyDatabase(session.user.id, session.database);
    } else {
      await session.database.close();
    }
  }
}
