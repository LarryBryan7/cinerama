import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../core/location/device_location.dart';
import '../../../core/theme/cine_tokens.dart';
import '../../../core/theme/figma_icons.dart';
import '../../../core/widgets/common_widgets.dart';
import '../../../core/widgets/design_canvas.dart';
import '../bloc/location_cubit.dart';
import '../bloc/movies_cubit.dart';
import '../data/movie_models.dart';
import 'cartelera_slide.dart';

/// Pantalla "Inicio" (Figma: "Cartelera - dark - 1"): un carrusel de tarjetas con las vecinas asomando a los lados.
/// Deslizar horizontalmente mueve las tarjetas; tocar la activa o el botón abre el detalle con sus funciones, y tocar una
/// vecina la centra.
class MoviesPage extends StatefulWidget {
  const MoviesPage({super.key});

  @override
  State<MoviesPage> createState() => _MoviesPageState();
}

class _MoviesPageState extends State<MoviesPage> {
  @override
  void initState() {
    super.initState();
    unawaited(context.read<MoviesCubit>().load());
    // Carga las ciudades y pide la ubicación del dispositivo (aparece el permiso del sistema). Al conocer la ciudad, la
    // cartelera se vuelve a pedir filtrada.
    unawaited(context.read<LocationCubit>().load());
  }

  void _onTab(MovieStatus tab) => unawaited(context.read<MoviesCubit>().setTab(tab));

  void _onCity(City city) {
    context.read<LocationCubit>().select(city);
  }

  /// Qué decirle a la persona cuando todavía no hay ubicación.
  static String _locationHint(LocationAccess access) => switch (access) {
        LocationAccess.deniedForever => 'El permiso está bloqueado: actívalo en los ajustes de la app.',
        LocationAccess.serviceOff => 'Enciende la ubicación del dispositivo para ver qué tan cerca está cada cine.',
        LocationAccess.denied => 'Para ver qué tan cerca está cada cine. No se envía a ningún servidor.',
        _ => 'Para ver qué tan cerca está cada cine. No se envía a ningún servidor.',
      };

  Future<void> _pickCity() async {
    final location = context.read<LocationCubit>();
    if (location.state.cities.isEmpty) {
      unawaited(location.load());
      return;
    }
    final picked = await showModalBottomSheet<City>(
      context: context,
      backgroundColor: CineColors.surface,
      builder: (context) => SafeArea(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Padding(padding: const EdgeInsets.all(16), child: Text('Elige tu ciudad', style: cineText(16, FontWeight.w600, CineColors.textTitle))),
          if (location.state.position == null)
            ListTile(
              key: const Key('enable-location'),
              leading: const Icon(Icons.my_location, color: CineColors.lime),
              title: Text('Usar mi ubicación', style: cineText(14, FontWeight.w500, CineColors.white)),
              subtitle: Text(_locationHint(location.state.access), style: cineText(12, FontWeight.w300, CineColors.textTab)),
              onTap: () {
                Navigator.pop(context);
                unawaited(location.state.access == LocationAccess.deniedForever ? location.openSettings() : location.locate());
              },
            ),
          for (final city in location.state.cities)
            ListTile(
              key: Key('city-${city.name}'),
              title: Text(city.label, style: cineText(14, FontWeight.w400, CineColors.white)),
              subtitle: Text('${city.cinemaCount} ${city.cinemaCount == 1 ? 'cine' : 'cines'}', style: cineText(12, FontWeight.w300, CineColors.textTab)),
              trailing: city == location.state.selected ? const Icon(Icons.check, color: CineColors.lime) : null,
              onTap: () => Navigator.pop(context, city),
            ),
        ]),
      ),
    );
    if (picked != null) _onCity(picked);
  }

  @override
  Widget build(BuildContext context) {
    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: SystemUiOverlayStyle.light,
      child: Scaffold(
        backgroundColor: CineColors.background,
        body: BlocListener<LocationCubit, LocationState>(
          // Al cambiar de ciudad (o al conocerla), la lista se pide filtrada y se vuelve a la primera película.
          listenWhen: (previous, current) => previous.selected?.name != current.selected?.name,
          listener: (context, state) => unawaited(context.read<MoviesCubit>().setCity(state.selected?.name)),
          child: Column(children: [
            _CineHeader(onPickCity: _pickCity),
            Expanded(
              child: BlocBuilder<MoviesCubit, MoviesState>(
                builder: (context, state) {
                  final tabs = CarteleraTabs(selected: state.tab, onSelected: _onTab);

                  // Las pestañas siempre se ven: si una está vacía o falla, hay que poder volver a otra.
                  Widget withTabs(Widget content) => Column(children: [
                        DesignCanvas(height: 60, children: [Positioned.fill(child: tabs)]),
                        Expanded(child: content),
                      ]);

                  if (state.status == LoadStatus.failure && state.items.isEmpty) {
                    return withTabs(ErrorState(message: messageForCode(state.errorCode), onRetry: () => context.read<MoviesCubit>().load()));
                  }
                  if (state.status != LoadStatus.ready && state.items.isEmpty) {
                    return withTabs(const Center(child: CircularProgressIndicator(color: CineColors.lime)));
                  }
                  if (state.items.isEmpty) {
                    return withTabs(Center(child: Text(_emptyMessage(state.tab), style: cineText(14, FontWeight.w300, CineColors.textTab))));
                  }

                  final items = state.items;
                  return RefreshIndicator(
                    color: CineColors.lime,
                    backgroundColor: CineColors.surface,
                    onRefresh: () => context.read<MoviesCubit>().load(),
                    child: SingleChildScrollView(
                      physics: const AlwaysScrollableScrollPhysics(),
                      child: DesignCanvas(height: kCarteleraContentHeight, children: [
                        Positioned.fill(child: tabs),
                        Positioned.fill(
                          child: BlocBuilder<LocationCubit, LocationState>(
                            buildWhen: (a, b) => a.position != b.position || a.selected != b.selected,
                            builder: (context, location) => CarteleraCarousel(
                              // Otra pestaña, ciudad o búsqueda: el carrusel vuelve a la primera tarjeta.
                              key: ValueKey('${state.tab.name}|${state.city}|${state.query}'),
                              movies: items,
                              cityName: location.selected?.name,
                              position: location.position,
                              onOpen: (movie) => context.push('/movies/${movie.id}'),
                              onOpenReviews: (movie) => context.push('/movies/${movie.id}/reviews'),
                              onNearEnd: () => unawaited(context.read<MoviesCubit>().loadMore()),
                            ),
                          ),
                        ),
                      ]),
                    ),
                  );
                },
              ),
            ),
          ]),
        ),
      ),
    );
  }

  static String _emptyMessage(MovieStatus tab) => switch (tab) {
        MovieStatus.nowShowing => 'No hay películas en cartelera.',
        MovieStatus.preSale => 'No hay películas en preventa.',
        MovieStatus.comingSoon => 'No hay estrenos próximos.',
      };
}

/// Cabecera de 75 px: marca y ubicación. Se extiende bajo la barra de estado del dispositivo.
/// La ciudad sale de los cines del servidor; tocarla abre el selector.
class _CineHeader extends StatelessWidget {
  const _CineHeader({required this.onPickCity});

  final VoidCallback onPickCity;

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      color: CineColors.surface,
      child: Padding(
        padding: EdgeInsets.only(top: MediaQuery.paddingOf(context).top),
        child: DesignCanvas(height: 75, children: [
          Positioned(left: 138, top: 15, width: 121, child: Text('CineFlash⚡', style: cineText(20, FontWeight.w700, CineColors.textTitle), softWrap: false)),
          Positioned(left: 120, top: 44, child: FigmaIcon(FigmaIconData.mapPin)),
          Positioned(
            left: 139,
            top: 44,
            width: 122,
            child: BlocBuilder<LocationCubit, LocationState>(
              builder: (context, state) => Text(
                state.selected?.name ?? 'Elige tu ciudad',
                key: const Key('header-city'),
                style: cineText(14, FontWeight.w300, CineColors.textTab),
                softWrap: false,
                overflow: TextOverflow.ellipsis,
              ),
            ),
          ),
          // El SVG del chevron incluye 0.75 px de margen por el trazo: el vector de Figma está en (263.25, 51.5).
          Positioned(left: 262.5, top: 50.75, child: FigmaIcon(FigmaIconData.chevronDown)),
          // Toda la fila de ubicación es tocable.
          Positioned(
            left: 110,
            top: 36,
            width: 170,
            height: 36,
            child: Semantics(
              button: true,
              label: 'Cambiar de ciudad',
              child: GestureDetector(key: const Key('city-picker'), behavior: HitTestBehavior.opaque, onTap: onPickCity, child: const SizedBox.expand()),
            ),
          ),
        ]),
      ),
    );
  }
}
