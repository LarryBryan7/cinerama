import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/api/api_exception.dart';
import '../../../core/location/device_location.dart';
import '../data/catalog_repository.dart';
import '../data/movie_models.dart';
import 'movies_cubit.dart';

class LocationState extends Equatable {
  const LocationState({
    this.status = LoadStatus.initial,
    this.cities = const [],
    this.selected,
    this.access = LocationAccess.unknown,
    this.position,
    this.locating = false,
  });

  final LoadStatus status;
  final List<City> cities;

  /// Ciudad elegida para filtrar la cartelera. `null` si el servidor no tiene ciudades todavía.
  final City? selected;

  /// Si la persona concedió la ubicación del dispositivo, y la posición aproximada (solo vive en la app).
  final LocationAccess access;
  final GeoPoint? position;
  final bool locating;

  LocationState copyWith({LoadStatus? status, List<City>? cities, City? selected, LocationAccess? access, GeoPoint? position, bool? locating, bool clearPosition = false}) =>
      LocationState(
        status: status ?? this.status,
        cities: cities ?? this.cities,
        selected: selected ?? this.selected,
        access: access ?? this.access,
        position: clearPosition ? null : (position ?? this.position),
        locating: locating ?? this.locating,
      );

  @override
  List<Object?> get props => [status, cities, selected, access, position, locating];
}

/// Ciudad que se muestra en la cabecera de Inicio y con la que se filtra la cartelera, más la ubicación aproximada del
/// dispositivo para calcular distancias a los cines. Las ciudades salen de los cines reales del servidor (RN-49).
/// La ubicación **nunca se envía al servidor**. La elección de ciudad vive solo mientras la app está abierta.
class LocationCubit extends Cubit<LocationState> {
  LocationCubit(this._repository, [this._device]) : super(const LocationState());

  final CatalogRepository _repository;
  final DeviceLocation? _device;

  /// Carga las ciudades y, después, pide la ubicación del dispositivo (muestra el permiso del sistema si hace falta).
  Future<void> load() async {
    emit(state.copyWith(status: LoadStatus.loading));
    try {
      final cities = await _repository.cities();
      // Se conserva la elección anterior si sigue existiendo; si no, la primera ciudad.
      final selected = cities.where((c) => c.name == state.selected?.name).firstOrNull ?? cities.firstOrNull;
      emit(LocationState(status: LoadStatus.ready, cities: cities, selected: selected, access: state.access, position: state.position));
    } on ApiException catch (e) {
      emit(state.copyWith(status: LoadStatus.failure));
      addError(e);
    }
    if (state.access == LocationAccess.unknown) await locate();
  }

  /// Pide la ubicación. Si el permiso quedó bloqueado, solo los ajustes del dispositivo lo pueden cambiar.
  Future<void> locate({bool askPermission = true}) async {
    final device = _device;
    if (device == null || state.locating) return;
    emit(state.copyWith(locating: true));
    final result = await device.locate(askPermission: askPermission);
    emit(LocationState(
      status: state.status,
      cities: state.cities,
      selected: state.selected,
      access: result.access,
      position: result.point ?? state.position,
    ));
  }

  Future<void> openSettings() async => _device?.openSettings();

  void select(City city) {
    if (city == state.selected) return;
    emit(state.copyWith(selected: city));
  }
}
