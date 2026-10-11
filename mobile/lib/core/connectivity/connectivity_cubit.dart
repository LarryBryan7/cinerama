import 'dart:async';

import 'package:flutter_bloc/flutter_bloc.dart';

import 'connectivity_service.dart';

/// Expone al árbol de widgets si hay conexión real con la API (para el aviso "Sin conexión" y para deshabilitar acciones).
class ConnectivityCubit extends Cubit<bool> {
  ConnectivityCubit(this._service) : super(_service.isOnline) {
    _subscription = _service.changes.listen(emit);
  }

  final ConnectivityService _service;
  StreamSubscription<bool>? _subscription;

  Future<void> recheck() async => emit(await _service.check());

  @override
  Future<void> close() async {
    await _subscription?.cancel();
    return super.close();
  }
}
