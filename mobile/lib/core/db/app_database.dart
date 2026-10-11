import 'package:drift/drift.dart';

part 'app_database.g.dart';

/// Reservas guardadas en el dispositivo, con su ticket firmado, para poder mostrar el QR sin conexión (ADR-010).
/// Hay un archivo de base por cuenta (`cinerama_<userId>`): una consulta nueva no puede mezclar datos de dos
/// cuentas porque no hay una tabla compartida (aislamiento correcto por construcción).
class CachedBookings extends Table {
  IntColumn get id => integer()();
  TextColumn get code => text()();
  TextColumn get status => text()();
  IntColumn get showtimeId => integer().nullable()();
  TextColumn get movieTitle => text()();
  TextColumn get roomName => text()();
  DateTimeColumn get startsAt => dateTime()();

  /// Lista de etiquetas en JSON, por ejemplo `["F7","F8"]`.
  TextColumn get seatLabels => text()();

  /// Importe como texto decimal (`"25.00"`): evita errores de redondeo de los flotantes.
  TextColumn get totalAmount => text()();

  /// Contenido firmado del QR (`{código}.{firma}`). Solo el servidor puede firmarlo: la app lo guarda y lo muestra,
  /// pero no puede fabricar uno válido.
  TextColumn get ticketPayload => text().nullable()();

  DateTimeColumn get createdAt => dateTime()();
  DateTimeColumn get cancelledAt => dateTime().nullable()();

  /// Cuándo se sincronizó esta fila con el servidor por última vez.
  DateTimeColumn get syncedAt => dateTime()();

  @override
  Set<Column> get primaryKey => {id};
}

@DriftDatabase(tables: [CachedBookings])
class AppDatabase extends _$AppDatabase {
  AppDatabase(super.e);

  @override
  int get schemaVersion => 1;
}
