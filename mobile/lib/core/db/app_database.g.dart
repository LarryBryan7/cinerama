// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'app_database.dart';

// ignore_for_file: type=lint
class $CachedBookingsTable extends CachedBookings
    with TableInfo<$CachedBookingsTable, CachedBooking> {
  @override
  final GeneratedDatabase attachedDatabase;
  final String? _alias;
  $CachedBookingsTable(this.attachedDatabase, [this._alias]);
  static const VerificationMeta _idMeta = const VerificationMeta('id');
  @override
  late final GeneratedColumn<int> id = GeneratedColumn<int>(
    'id',
    aliasedName,
    false,
    type: DriftSqlType.int,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _codeMeta = const VerificationMeta('code');
  @override
  late final GeneratedColumn<String> code = GeneratedColumn<String>(
    'code',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _statusMeta = const VerificationMeta('status');
  @override
  late final GeneratedColumn<String> status = GeneratedColumn<String>(
    'status',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _showtimeIdMeta = const VerificationMeta(
    'showtimeId',
  );
  @override
  late final GeneratedColumn<int> showtimeId = GeneratedColumn<int>(
    'showtime_id',
    aliasedName,
    true,
    type: DriftSqlType.int,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _movieTitleMeta = const VerificationMeta(
    'movieTitle',
  );
  @override
  late final GeneratedColumn<String> movieTitle = GeneratedColumn<String>(
    'movie_title',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _roomNameMeta = const VerificationMeta(
    'roomName',
  );
  @override
  late final GeneratedColumn<String> roomName = GeneratedColumn<String>(
    'room_name',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _startsAtMeta = const VerificationMeta(
    'startsAt',
  );
  @override
  late final GeneratedColumn<DateTime> startsAt = GeneratedColumn<DateTime>(
    'starts_at',
    aliasedName,
    false,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _seatLabelsMeta = const VerificationMeta(
    'seatLabels',
  );
  @override
  late final GeneratedColumn<String> seatLabels = GeneratedColumn<String>(
    'seat_labels',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _totalAmountMeta = const VerificationMeta(
    'totalAmount',
  );
  @override
  late final GeneratedColumn<String> totalAmount = GeneratedColumn<String>(
    'total_amount',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _ticketPayloadMeta = const VerificationMeta(
    'ticketPayload',
  );
  @override
  late final GeneratedColumn<String> ticketPayload = GeneratedColumn<String>(
    'ticket_payload',
    aliasedName,
    true,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _createdAtMeta = const VerificationMeta(
    'createdAt',
  );
  @override
  late final GeneratedColumn<DateTime> createdAt = GeneratedColumn<DateTime>(
    'created_at',
    aliasedName,
    false,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _cancelledAtMeta = const VerificationMeta(
    'cancelledAt',
  );
  @override
  late final GeneratedColumn<DateTime> cancelledAt = GeneratedColumn<DateTime>(
    'cancelled_at',
    aliasedName,
    true,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _syncedAtMeta = const VerificationMeta(
    'syncedAt',
  );
  @override
  late final GeneratedColumn<DateTime> syncedAt = GeneratedColumn<DateTime>(
    'synced_at',
    aliasedName,
    false,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: true,
  );
  @override
  List<GeneratedColumn> get $columns => [
    id,
    code,
    status,
    showtimeId,
    movieTitle,
    roomName,
    startsAt,
    seatLabels,
    totalAmount,
    ticketPayload,
    createdAt,
    cancelledAt,
    syncedAt,
  ];
  @override
  String get aliasedName => _alias ?? actualTableName;
  @override
  String get actualTableName => $name;
  static const String $name = 'cached_bookings';
  @override
  VerificationContext validateIntegrity(
    Insertable<CachedBooking> instance, {
    bool isInserting = false,
  }) {
    final context = VerificationContext();
    final data = instance.toColumns(true);
    if (data.containsKey('id')) {
      context.handle(_idMeta, id.isAcceptableOrUnknown(data['id']!, _idMeta));
    }
    if (data.containsKey('code')) {
      context.handle(
        _codeMeta,
        code.isAcceptableOrUnknown(data['code']!, _codeMeta),
      );
    } else if (isInserting) {
      context.missing(_codeMeta);
    }
    if (data.containsKey('status')) {
      context.handle(
        _statusMeta,
        status.isAcceptableOrUnknown(data['status']!, _statusMeta),
      );
    } else if (isInserting) {
      context.missing(_statusMeta);
    }
    if (data.containsKey('showtime_id')) {
      context.handle(
        _showtimeIdMeta,
        showtimeId.isAcceptableOrUnknown(data['showtime_id']!, _showtimeIdMeta),
      );
    }
    if (data.containsKey('movie_title')) {
      context.handle(
        _movieTitleMeta,
        movieTitle.isAcceptableOrUnknown(data['movie_title']!, _movieTitleMeta),
      );
    } else if (isInserting) {
      context.missing(_movieTitleMeta);
    }
    if (data.containsKey('room_name')) {
      context.handle(
        _roomNameMeta,
        roomName.isAcceptableOrUnknown(data['room_name']!, _roomNameMeta),
      );
    } else if (isInserting) {
      context.missing(_roomNameMeta);
    }
    if (data.containsKey('starts_at')) {
      context.handle(
        _startsAtMeta,
        startsAt.isAcceptableOrUnknown(data['starts_at']!, _startsAtMeta),
      );
    } else if (isInserting) {
      context.missing(_startsAtMeta);
    }
    if (data.containsKey('seat_labels')) {
      context.handle(
        _seatLabelsMeta,
        seatLabels.isAcceptableOrUnknown(data['seat_labels']!, _seatLabelsMeta),
      );
    } else if (isInserting) {
      context.missing(_seatLabelsMeta);
    }
    if (data.containsKey('total_amount')) {
      context.handle(
        _totalAmountMeta,
        totalAmount.isAcceptableOrUnknown(
          data['total_amount']!,
          _totalAmountMeta,
        ),
      );
    } else if (isInserting) {
      context.missing(_totalAmountMeta);
    }
    if (data.containsKey('ticket_payload')) {
      context.handle(
        _ticketPayloadMeta,
        ticketPayload.isAcceptableOrUnknown(
          data['ticket_payload']!,
          _ticketPayloadMeta,
        ),
      );
    }
    if (data.containsKey('created_at')) {
      context.handle(
        _createdAtMeta,
        createdAt.isAcceptableOrUnknown(data['created_at']!, _createdAtMeta),
      );
    } else if (isInserting) {
      context.missing(_createdAtMeta);
    }
    if (data.containsKey('cancelled_at')) {
      context.handle(
        _cancelledAtMeta,
        cancelledAt.isAcceptableOrUnknown(
          data['cancelled_at']!,
          _cancelledAtMeta,
        ),
      );
    }
    if (data.containsKey('synced_at')) {
      context.handle(
        _syncedAtMeta,
        syncedAt.isAcceptableOrUnknown(data['synced_at']!, _syncedAtMeta),
      );
    } else if (isInserting) {
      context.missing(_syncedAtMeta);
    }
    return context;
  }

  @override
  Set<GeneratedColumn> get $primaryKey => {id};
  @override
  CachedBooking map(Map<String, dynamic> data, {String? tablePrefix}) {
    final effectivePrefix = tablePrefix != null ? '$tablePrefix.' : '';
    return CachedBooking(
      id: attachedDatabase.typeMapping.read(
        DriftSqlType.int,
        data['${effectivePrefix}id'],
      )!,
      code: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}code'],
      )!,
      status: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}status'],
      )!,
      showtimeId: attachedDatabase.typeMapping.read(
        DriftSqlType.int,
        data['${effectivePrefix}showtime_id'],
      ),
      movieTitle: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}movie_title'],
      )!,
      roomName: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}room_name'],
      )!,
      startsAt: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}starts_at'],
      )!,
      seatLabels: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}seat_labels'],
      )!,
      totalAmount: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}total_amount'],
      )!,
      ticketPayload: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}ticket_payload'],
      ),
      createdAt: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}created_at'],
      )!,
      cancelledAt: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}cancelled_at'],
      ),
      syncedAt: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}synced_at'],
      )!,
    );
  }

  @override
  $CachedBookingsTable createAlias(String alias) {
    return $CachedBookingsTable(attachedDatabase, alias);
  }
}

class CachedBooking extends DataClass implements Insertable<CachedBooking> {
  final int id;
  final String code;
  final String status;
  final int? showtimeId;
  final String movieTitle;
  final String roomName;
  final DateTime startsAt;

  /// Lista de etiquetas en JSON, por ejemplo `["F7","F8"]`.
  final String seatLabels;

  /// Importe como texto decimal (`"25.00"`): evita errores de redondeo de los flotantes.
  final String totalAmount;

  /// Contenido firmado del QR (`{código}.{firma}`). Solo el servidor puede firmarlo: la app lo guarda y lo muestra,
  /// pero no puede fabricar uno válido.
  final String? ticketPayload;
  final DateTime createdAt;
  final DateTime? cancelledAt;

  /// Cuándo se sincronizó esta fila con el servidor por última vez.
  final DateTime syncedAt;
  const CachedBooking({
    required this.id,
    required this.code,
    required this.status,
    this.showtimeId,
    required this.movieTitle,
    required this.roomName,
    required this.startsAt,
    required this.seatLabels,
    required this.totalAmount,
    this.ticketPayload,
    required this.createdAt,
    this.cancelledAt,
    required this.syncedAt,
  });
  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    map['id'] = Variable<int>(id);
    map['code'] = Variable<String>(code);
    map['status'] = Variable<String>(status);
    if (!nullToAbsent || showtimeId != null) {
      map['showtime_id'] = Variable<int>(showtimeId);
    }
    map['movie_title'] = Variable<String>(movieTitle);
    map['room_name'] = Variable<String>(roomName);
    map['starts_at'] = Variable<DateTime>(startsAt);
    map['seat_labels'] = Variable<String>(seatLabels);
    map['total_amount'] = Variable<String>(totalAmount);
    if (!nullToAbsent || ticketPayload != null) {
      map['ticket_payload'] = Variable<String>(ticketPayload);
    }
    map['created_at'] = Variable<DateTime>(createdAt);
    if (!nullToAbsent || cancelledAt != null) {
      map['cancelled_at'] = Variable<DateTime>(cancelledAt);
    }
    map['synced_at'] = Variable<DateTime>(syncedAt);
    return map;
  }

  CachedBookingsCompanion toCompanion(bool nullToAbsent) {
    return CachedBookingsCompanion(
      id: Value(id),
      code: Value(code),
      status: Value(status),
      showtimeId: showtimeId == null && nullToAbsent
          ? const Value.absent()
          : Value(showtimeId),
      movieTitle: Value(movieTitle),
      roomName: Value(roomName),
      startsAt: Value(startsAt),
      seatLabels: Value(seatLabels),
      totalAmount: Value(totalAmount),
      ticketPayload: ticketPayload == null && nullToAbsent
          ? const Value.absent()
          : Value(ticketPayload),
      createdAt: Value(createdAt),
      cancelledAt: cancelledAt == null && nullToAbsent
          ? const Value.absent()
          : Value(cancelledAt),
      syncedAt: Value(syncedAt),
    );
  }

  factory CachedBooking.fromJson(
    Map<String, dynamic> json, {
    ValueSerializer? serializer,
  }) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return CachedBooking(
      id: serializer.fromJson<int>(json['id']),
      code: serializer.fromJson<String>(json['code']),
      status: serializer.fromJson<String>(json['status']),
      showtimeId: serializer.fromJson<int?>(json['showtimeId']),
      movieTitle: serializer.fromJson<String>(json['movieTitle']),
      roomName: serializer.fromJson<String>(json['roomName']),
      startsAt: serializer.fromJson<DateTime>(json['startsAt']),
      seatLabels: serializer.fromJson<String>(json['seatLabels']),
      totalAmount: serializer.fromJson<String>(json['totalAmount']),
      ticketPayload: serializer.fromJson<String?>(json['ticketPayload']),
      createdAt: serializer.fromJson<DateTime>(json['createdAt']),
      cancelledAt: serializer.fromJson<DateTime?>(json['cancelledAt']),
      syncedAt: serializer.fromJson<DateTime>(json['syncedAt']),
    );
  }
  @override
  Map<String, dynamic> toJson({ValueSerializer? serializer}) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return <String, dynamic>{
      'id': serializer.toJson<int>(id),
      'code': serializer.toJson<String>(code),
      'status': serializer.toJson<String>(status),
      'showtimeId': serializer.toJson<int?>(showtimeId),
      'movieTitle': serializer.toJson<String>(movieTitle),
      'roomName': serializer.toJson<String>(roomName),
      'startsAt': serializer.toJson<DateTime>(startsAt),
      'seatLabels': serializer.toJson<String>(seatLabels),
      'totalAmount': serializer.toJson<String>(totalAmount),
      'ticketPayload': serializer.toJson<String?>(ticketPayload),
      'createdAt': serializer.toJson<DateTime>(createdAt),
      'cancelledAt': serializer.toJson<DateTime?>(cancelledAt),
      'syncedAt': serializer.toJson<DateTime>(syncedAt),
    };
  }

  CachedBooking copyWith({
    int? id,
    String? code,
    String? status,
    Value<int?> showtimeId = const Value.absent(),
    String? movieTitle,
    String? roomName,
    DateTime? startsAt,
    String? seatLabels,
    String? totalAmount,
    Value<String?> ticketPayload = const Value.absent(),
    DateTime? createdAt,
    Value<DateTime?> cancelledAt = const Value.absent(),
    DateTime? syncedAt,
  }) => CachedBooking(
    id: id ?? this.id,
    code: code ?? this.code,
    status: status ?? this.status,
    showtimeId: showtimeId.present ? showtimeId.value : this.showtimeId,
    movieTitle: movieTitle ?? this.movieTitle,
    roomName: roomName ?? this.roomName,
    startsAt: startsAt ?? this.startsAt,
    seatLabels: seatLabels ?? this.seatLabels,
    totalAmount: totalAmount ?? this.totalAmount,
    ticketPayload: ticketPayload.present
        ? ticketPayload.value
        : this.ticketPayload,
    createdAt: createdAt ?? this.createdAt,
    cancelledAt: cancelledAt.present ? cancelledAt.value : this.cancelledAt,
    syncedAt: syncedAt ?? this.syncedAt,
  );
  CachedBooking copyWithCompanion(CachedBookingsCompanion data) {
    return CachedBooking(
      id: data.id.present ? data.id.value : this.id,
      code: data.code.present ? data.code.value : this.code,
      status: data.status.present ? data.status.value : this.status,
      showtimeId: data.showtimeId.present
          ? data.showtimeId.value
          : this.showtimeId,
      movieTitle: data.movieTitle.present
          ? data.movieTitle.value
          : this.movieTitle,
      roomName: data.roomName.present ? data.roomName.value : this.roomName,
      startsAt: data.startsAt.present ? data.startsAt.value : this.startsAt,
      seatLabels: data.seatLabels.present
          ? data.seatLabels.value
          : this.seatLabels,
      totalAmount: data.totalAmount.present
          ? data.totalAmount.value
          : this.totalAmount,
      ticketPayload: data.ticketPayload.present
          ? data.ticketPayload.value
          : this.ticketPayload,
      createdAt: data.createdAt.present ? data.createdAt.value : this.createdAt,
      cancelledAt: data.cancelledAt.present
          ? data.cancelledAt.value
          : this.cancelledAt,
      syncedAt: data.syncedAt.present ? data.syncedAt.value : this.syncedAt,
    );
  }

  @override
  String toString() {
    return (StringBuffer('CachedBooking(')
          ..write('id: $id, ')
          ..write('code: $code, ')
          ..write('status: $status, ')
          ..write('showtimeId: $showtimeId, ')
          ..write('movieTitle: $movieTitle, ')
          ..write('roomName: $roomName, ')
          ..write('startsAt: $startsAt, ')
          ..write('seatLabels: $seatLabels, ')
          ..write('totalAmount: $totalAmount, ')
          ..write('ticketPayload: $ticketPayload, ')
          ..write('createdAt: $createdAt, ')
          ..write('cancelledAt: $cancelledAt, ')
          ..write('syncedAt: $syncedAt')
          ..write(')'))
        .toString();
  }

  @override
  int get hashCode => Object.hash(
    id,
    code,
    status,
    showtimeId,
    movieTitle,
    roomName,
    startsAt,
    seatLabels,
    totalAmount,
    ticketPayload,
    createdAt,
    cancelledAt,
    syncedAt,
  );
  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      (other is CachedBooking &&
          other.id == this.id &&
          other.code == this.code &&
          other.status == this.status &&
          other.showtimeId == this.showtimeId &&
          other.movieTitle == this.movieTitle &&
          other.roomName == this.roomName &&
          other.startsAt == this.startsAt &&
          other.seatLabels == this.seatLabels &&
          other.totalAmount == this.totalAmount &&
          other.ticketPayload == this.ticketPayload &&
          other.createdAt == this.createdAt &&
          other.cancelledAt == this.cancelledAt &&
          other.syncedAt == this.syncedAt);
}

class CachedBookingsCompanion extends UpdateCompanion<CachedBooking> {
  final Value<int> id;
  final Value<String> code;
  final Value<String> status;
  final Value<int?> showtimeId;
  final Value<String> movieTitle;
  final Value<String> roomName;
  final Value<DateTime> startsAt;
  final Value<String> seatLabels;
  final Value<String> totalAmount;
  final Value<String?> ticketPayload;
  final Value<DateTime> createdAt;
  final Value<DateTime?> cancelledAt;
  final Value<DateTime> syncedAt;
  const CachedBookingsCompanion({
    this.id = const Value.absent(),
    this.code = const Value.absent(),
    this.status = const Value.absent(),
    this.showtimeId = const Value.absent(),
    this.movieTitle = const Value.absent(),
    this.roomName = const Value.absent(),
    this.startsAt = const Value.absent(),
    this.seatLabels = const Value.absent(),
    this.totalAmount = const Value.absent(),
    this.ticketPayload = const Value.absent(),
    this.createdAt = const Value.absent(),
    this.cancelledAt = const Value.absent(),
    this.syncedAt = const Value.absent(),
  });
  CachedBookingsCompanion.insert({
    this.id = const Value.absent(),
    required String code,
    required String status,
    this.showtimeId = const Value.absent(),
    required String movieTitle,
    required String roomName,
    required DateTime startsAt,
    required String seatLabels,
    required String totalAmount,
    this.ticketPayload = const Value.absent(),
    required DateTime createdAt,
    this.cancelledAt = const Value.absent(),
    required DateTime syncedAt,
  }) : code = Value(code),
       status = Value(status),
       movieTitle = Value(movieTitle),
       roomName = Value(roomName),
       startsAt = Value(startsAt),
       seatLabels = Value(seatLabels),
       totalAmount = Value(totalAmount),
       createdAt = Value(createdAt),
       syncedAt = Value(syncedAt);
  static Insertable<CachedBooking> custom({
    Expression<int>? id,
    Expression<String>? code,
    Expression<String>? status,
    Expression<int>? showtimeId,
    Expression<String>? movieTitle,
    Expression<String>? roomName,
    Expression<DateTime>? startsAt,
    Expression<String>? seatLabels,
    Expression<String>? totalAmount,
    Expression<String>? ticketPayload,
    Expression<DateTime>? createdAt,
    Expression<DateTime>? cancelledAt,
    Expression<DateTime>? syncedAt,
  }) {
    return RawValuesInsertable({
      if (id != null) 'id': id,
      if (code != null) 'code': code,
      if (status != null) 'status': status,
      if (showtimeId != null) 'showtime_id': showtimeId,
      if (movieTitle != null) 'movie_title': movieTitle,
      if (roomName != null) 'room_name': roomName,
      if (startsAt != null) 'starts_at': startsAt,
      if (seatLabels != null) 'seat_labels': seatLabels,
      if (totalAmount != null) 'total_amount': totalAmount,
      if (ticketPayload != null) 'ticket_payload': ticketPayload,
      if (createdAt != null) 'created_at': createdAt,
      if (cancelledAt != null) 'cancelled_at': cancelledAt,
      if (syncedAt != null) 'synced_at': syncedAt,
    });
  }

  CachedBookingsCompanion copyWith({
    Value<int>? id,
    Value<String>? code,
    Value<String>? status,
    Value<int?>? showtimeId,
    Value<String>? movieTitle,
    Value<String>? roomName,
    Value<DateTime>? startsAt,
    Value<String>? seatLabels,
    Value<String>? totalAmount,
    Value<String?>? ticketPayload,
    Value<DateTime>? createdAt,
    Value<DateTime?>? cancelledAt,
    Value<DateTime>? syncedAt,
  }) {
    return CachedBookingsCompanion(
      id: id ?? this.id,
      code: code ?? this.code,
      status: status ?? this.status,
      showtimeId: showtimeId ?? this.showtimeId,
      movieTitle: movieTitle ?? this.movieTitle,
      roomName: roomName ?? this.roomName,
      startsAt: startsAt ?? this.startsAt,
      seatLabels: seatLabels ?? this.seatLabels,
      totalAmount: totalAmount ?? this.totalAmount,
      ticketPayload: ticketPayload ?? this.ticketPayload,
      createdAt: createdAt ?? this.createdAt,
      cancelledAt: cancelledAt ?? this.cancelledAt,
      syncedAt: syncedAt ?? this.syncedAt,
    );
  }

  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    if (id.present) {
      map['id'] = Variable<int>(id.value);
    }
    if (code.present) {
      map['code'] = Variable<String>(code.value);
    }
    if (status.present) {
      map['status'] = Variable<String>(status.value);
    }
    if (showtimeId.present) {
      map['showtime_id'] = Variable<int>(showtimeId.value);
    }
    if (movieTitle.present) {
      map['movie_title'] = Variable<String>(movieTitle.value);
    }
    if (roomName.present) {
      map['room_name'] = Variable<String>(roomName.value);
    }
    if (startsAt.present) {
      map['starts_at'] = Variable<DateTime>(startsAt.value);
    }
    if (seatLabels.present) {
      map['seat_labels'] = Variable<String>(seatLabels.value);
    }
    if (totalAmount.present) {
      map['total_amount'] = Variable<String>(totalAmount.value);
    }
    if (ticketPayload.present) {
      map['ticket_payload'] = Variable<String>(ticketPayload.value);
    }
    if (createdAt.present) {
      map['created_at'] = Variable<DateTime>(createdAt.value);
    }
    if (cancelledAt.present) {
      map['cancelled_at'] = Variable<DateTime>(cancelledAt.value);
    }
    if (syncedAt.present) {
      map['synced_at'] = Variable<DateTime>(syncedAt.value);
    }
    return map;
  }

  @override
  String toString() {
    return (StringBuffer('CachedBookingsCompanion(')
          ..write('id: $id, ')
          ..write('code: $code, ')
          ..write('status: $status, ')
          ..write('showtimeId: $showtimeId, ')
          ..write('movieTitle: $movieTitle, ')
          ..write('roomName: $roomName, ')
          ..write('startsAt: $startsAt, ')
          ..write('seatLabels: $seatLabels, ')
          ..write('totalAmount: $totalAmount, ')
          ..write('ticketPayload: $ticketPayload, ')
          ..write('createdAt: $createdAt, ')
          ..write('cancelledAt: $cancelledAt, ')
          ..write('syncedAt: $syncedAt')
          ..write(')'))
        .toString();
  }
}

abstract class _$AppDatabase extends GeneratedDatabase {
  _$AppDatabase(QueryExecutor e) : super(e);
  $AppDatabaseManager get managers => $AppDatabaseManager(this);
  late final $CachedBookingsTable cachedBookings = $CachedBookingsTable(this);
  @override
  Iterable<TableInfo<Table, Object?>> get allTables =>
      allSchemaEntities.whereType<TableInfo<Table, Object?>>();
  @override
  List<DatabaseSchemaEntity> get allSchemaEntities => [cachedBookings];
}

typedef $$CachedBookingsTableCreateCompanionBuilder =
    CachedBookingsCompanion Function({
      Value<int> id,
      required String code,
      required String status,
      Value<int?> showtimeId,
      required String movieTitle,
      required String roomName,
      required DateTime startsAt,
      required String seatLabels,
      required String totalAmount,
      Value<String?> ticketPayload,
      required DateTime createdAt,
      Value<DateTime?> cancelledAt,
      required DateTime syncedAt,
    });
typedef $$CachedBookingsTableUpdateCompanionBuilder =
    CachedBookingsCompanion Function({
      Value<int> id,
      Value<String> code,
      Value<String> status,
      Value<int?> showtimeId,
      Value<String> movieTitle,
      Value<String> roomName,
      Value<DateTime> startsAt,
      Value<String> seatLabels,
      Value<String> totalAmount,
      Value<String?> ticketPayload,
      Value<DateTime> createdAt,
      Value<DateTime?> cancelledAt,
      Value<DateTime> syncedAt,
    });

class $$CachedBookingsTableFilterComposer
    extends Composer<_$AppDatabase, $CachedBookingsTable> {
  $$CachedBookingsTableFilterComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnFilters<int> get id => $composableBuilder(
    column: $table.id,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get code => $composableBuilder(
    column: $table.code,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get status => $composableBuilder(
    column: $table.status,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<int> get showtimeId => $composableBuilder(
    column: $table.showtimeId,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get movieTitle => $composableBuilder(
    column: $table.movieTitle,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get roomName => $composableBuilder(
    column: $table.roomName,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get startsAt => $composableBuilder(
    column: $table.startsAt,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get seatLabels => $composableBuilder(
    column: $table.seatLabels,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get totalAmount => $composableBuilder(
    column: $table.totalAmount,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get ticketPayload => $composableBuilder(
    column: $table.ticketPayload,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get createdAt => $composableBuilder(
    column: $table.createdAt,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get cancelledAt => $composableBuilder(
    column: $table.cancelledAt,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get syncedAt => $composableBuilder(
    column: $table.syncedAt,
    builder: (column) => ColumnFilters(column),
  );
}

class $$CachedBookingsTableOrderingComposer
    extends Composer<_$AppDatabase, $CachedBookingsTable> {
  $$CachedBookingsTableOrderingComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnOrderings<int> get id => $composableBuilder(
    column: $table.id,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get code => $composableBuilder(
    column: $table.code,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get status => $composableBuilder(
    column: $table.status,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<int> get showtimeId => $composableBuilder(
    column: $table.showtimeId,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get movieTitle => $composableBuilder(
    column: $table.movieTitle,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get roomName => $composableBuilder(
    column: $table.roomName,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get startsAt => $composableBuilder(
    column: $table.startsAt,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get seatLabels => $composableBuilder(
    column: $table.seatLabels,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get totalAmount => $composableBuilder(
    column: $table.totalAmount,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get ticketPayload => $composableBuilder(
    column: $table.ticketPayload,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get createdAt => $composableBuilder(
    column: $table.createdAt,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get cancelledAt => $composableBuilder(
    column: $table.cancelledAt,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get syncedAt => $composableBuilder(
    column: $table.syncedAt,
    builder: (column) => ColumnOrderings(column),
  );
}

class $$CachedBookingsTableAnnotationComposer
    extends Composer<_$AppDatabase, $CachedBookingsTable> {
  $$CachedBookingsTableAnnotationComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  GeneratedColumn<int> get id =>
      $composableBuilder(column: $table.id, builder: (column) => column);

  GeneratedColumn<String> get code =>
      $composableBuilder(column: $table.code, builder: (column) => column);

  GeneratedColumn<String> get status =>
      $composableBuilder(column: $table.status, builder: (column) => column);

  GeneratedColumn<int> get showtimeId => $composableBuilder(
    column: $table.showtimeId,
    builder: (column) => column,
  );

  GeneratedColumn<String> get movieTitle => $composableBuilder(
    column: $table.movieTitle,
    builder: (column) => column,
  );

  GeneratedColumn<String> get roomName =>
      $composableBuilder(column: $table.roomName, builder: (column) => column);

  GeneratedColumn<DateTime> get startsAt =>
      $composableBuilder(column: $table.startsAt, builder: (column) => column);

  GeneratedColumn<String> get seatLabels => $composableBuilder(
    column: $table.seatLabels,
    builder: (column) => column,
  );

  GeneratedColumn<String> get totalAmount => $composableBuilder(
    column: $table.totalAmount,
    builder: (column) => column,
  );

  GeneratedColumn<String> get ticketPayload => $composableBuilder(
    column: $table.ticketPayload,
    builder: (column) => column,
  );

  GeneratedColumn<DateTime> get createdAt =>
      $composableBuilder(column: $table.createdAt, builder: (column) => column);

  GeneratedColumn<DateTime> get cancelledAt => $composableBuilder(
    column: $table.cancelledAt,
    builder: (column) => column,
  );

  GeneratedColumn<DateTime> get syncedAt =>
      $composableBuilder(column: $table.syncedAt, builder: (column) => column);
}

class $$CachedBookingsTableTableManager
    extends
        RootTableManager<
          _$AppDatabase,
          $CachedBookingsTable,
          CachedBooking,
          $$CachedBookingsTableFilterComposer,
          $$CachedBookingsTableOrderingComposer,
          $$CachedBookingsTableAnnotationComposer,
          $$CachedBookingsTableCreateCompanionBuilder,
          $$CachedBookingsTableUpdateCompanionBuilder,
          (
            CachedBooking,
            BaseReferences<_$AppDatabase, $CachedBookingsTable, CachedBooking>,
          ),
          CachedBooking,
          PrefetchHooks Function()
        > {
  $$CachedBookingsTableTableManager(
    _$AppDatabase db,
    $CachedBookingsTable table,
  ) : super(
        TableManagerState(
          db: db,
          table: table,
          createFilteringComposer: () =>
              $$CachedBookingsTableFilterComposer($db: db, $table: table),
          createOrderingComposer: () =>
              $$CachedBookingsTableOrderingComposer($db: db, $table: table),
          createComputedFieldComposer: () =>
              $$CachedBookingsTableAnnotationComposer($db: db, $table: table),
          updateCompanionCallback:
              ({
                Value<int> id = const Value.absent(),
                Value<String> code = const Value.absent(),
                Value<String> status = const Value.absent(),
                Value<int?> showtimeId = const Value.absent(),
                Value<String> movieTitle = const Value.absent(),
                Value<String> roomName = const Value.absent(),
                Value<DateTime> startsAt = const Value.absent(),
                Value<String> seatLabels = const Value.absent(),
                Value<String> totalAmount = const Value.absent(),
                Value<String?> ticketPayload = const Value.absent(),
                Value<DateTime> createdAt = const Value.absent(),
                Value<DateTime?> cancelledAt = const Value.absent(),
                Value<DateTime> syncedAt = const Value.absent(),
              }) => CachedBookingsCompanion(
                id: id,
                code: code,
                status: status,
                showtimeId: showtimeId,
                movieTitle: movieTitle,
                roomName: roomName,
                startsAt: startsAt,
                seatLabels: seatLabels,
                totalAmount: totalAmount,
                ticketPayload: ticketPayload,
                createdAt: createdAt,
                cancelledAt: cancelledAt,
                syncedAt: syncedAt,
              ),
          createCompanionCallback:
              ({
                Value<int> id = const Value.absent(),
                required String code,
                required String status,
                Value<int?> showtimeId = const Value.absent(),
                required String movieTitle,
                required String roomName,
                required DateTime startsAt,
                required String seatLabels,
                required String totalAmount,
                Value<String?> ticketPayload = const Value.absent(),
                required DateTime createdAt,
                Value<DateTime?> cancelledAt = const Value.absent(),
                required DateTime syncedAt,
              }) => CachedBookingsCompanion.insert(
                id: id,
                code: code,
                status: status,
                showtimeId: showtimeId,
                movieTitle: movieTitle,
                roomName: roomName,
                startsAt: startsAt,
                seatLabels: seatLabels,
                totalAmount: totalAmount,
                ticketPayload: ticketPayload,
                createdAt: createdAt,
                cancelledAt: cancelledAt,
                syncedAt: syncedAt,
              ),
          withReferenceMapper: (p0) => p0
              .map((e) => (e.readTable(table), BaseReferences(db, table, e)))
              .toList(),
          prefetchHooksCallback: null,
        ),
      );
}

typedef $$CachedBookingsTableProcessedTableManager =
    ProcessedTableManager<
      _$AppDatabase,
      $CachedBookingsTable,
      CachedBooking,
      $$CachedBookingsTableFilterComposer,
      $$CachedBookingsTableOrderingComposer,
      $$CachedBookingsTableAnnotationComposer,
      $$CachedBookingsTableCreateCompanionBuilder,
      $$CachedBookingsTableUpdateCompanionBuilder,
      (
        CachedBooking,
        BaseReferences<_$AppDatabase, $CachedBookingsTable, CachedBooking>,
      ),
      CachedBooking,
      PrefetchHooks Function()
    >;

class $AppDatabaseManager {
  final _$AppDatabase _db;
  $AppDatabaseManager(this._db);
  $$CachedBookingsTableTableManager get cachedBookings =>
      $$CachedBookingsTableTableManager(_db, _db.cachedBookings);
}
