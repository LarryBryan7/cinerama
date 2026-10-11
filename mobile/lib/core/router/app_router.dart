import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../features/auth/bloc/auth_bloc.dart';
import '../../features/auth/ui/login_page.dart';
import '../../features/auth/ui/register_page.dart';
import '../../features/bookings/bloc/bookings_cubit.dart';
import '../../features/bookings/bloc/ticket_cubit.dart';
import '../../features/bookings/ui/bookings_page.dart';
import '../../features/bookings/ui/ticket_page.dart';
import '../../features/checkout/bloc/checkout_cubit.dart';
import '../../features/checkout/ui/checkout_page.dart';
import '../../features/community/ui/community_page.dart';
import '../../features/movies/bloc/location_cubit.dart';
import '../../features/movies/bloc/movie_detail_cubit.dart';
import '../../features/movies/bloc/movies_cubit.dart';
import '../../features/movies/bloc/reviews_cubit.dart';
import '../../features/movies/bloc/trailer_comments_cubit.dart';
import '../../features/movies/ui/movie_detail_page.dart';
import '../../features/movies/ui/movies_page.dart';
import '../../features/movies/ui/reviews_page.dart';
import '../../features/profile/ui/profile_page.dart';
import '../../features/seats/bloc/seat_selection_bloc.dart';
import '../../features/seats/ui/seat_map_page.dart';
import '../../features/seats/ui/showtime_info.dart';
import '../../features/shell/home_shell.dart';
import '../di/app_dependencies.dart';

/// Hace que el router vuelva a evaluar sus redirecciones cada vez que cambia el estado de la sesión.
class _AuthRefresh extends ChangeNotifier {
  _AuthRefresh(Stream<AuthState> stream) {
    _subscription = stream.listen((_) => notifyListeners());
  }

  late final StreamSubscription<AuthState> _subscription;

  @override
  void dispose() {
    unawaited(_subscription.cancel());
    super.dispose();
  }
}

class AppRouter {
  const AppRouter._();

  /// Pantallas que se pueden ver sin iniciar sesión: la cartelera y el detalle de cada película (la API los expone
  /// públicos), la comunidad y las pantallas de acceso. Todo lo demás (asientos, pago, entradas, perfil) exige sesión.
  static bool isPublic(String path) =>
      path == '/login' || path == '/register' || path == '/community' || path == '/movies' || path.startsWith('/movies/');

  /// Solo se acepta como destino de retorno una ruta interna de la app (nunca una URL externa ni las pantallas de acceso).
  static String? _safeReturnTo(String? from) {
    if (from == null || !from.startsWith('/') || from.startsWith('//')) return null;
    final path = Uri.tryParse(from)?.path;
    if (path == null || path == '/login' || path == '/register' || path == '/splash') return null;
    return from;
  }

  /// Reglas de acceso: quien no ha iniciado sesión entra directo a la cartelera; el login solo aparece cuando una
  /// acción lo necesita (elegir asientos, ver entradas, perfil) y, al terminar, devuelve a donde iba.
  @visibleForTesting
  static String? redirectFor(AuthStatus status, Uri uri) {
    final path = uri.path;
    final onAuthPage = path == '/login' || path == '/register';

    if (status == AuthStatus.unknown) return path == '/splash' ? null : '/splash';
    if (status == AuthStatus.unauthenticated) {
      if (path == '/splash') return '/movies';
      if (isPublic(path)) return null;
      return Uri(path: '/login', queryParameters: {'from': uri.toString()}).toString();
    }
    // Con sesión: no tiene sentido quedarse en el login ni en la pantalla de carga.
    if (onAuthPage || path == '/splash') return _safeReturnTo(uri.queryParameters['from']) ?? '/movies';
    return null;
  }

  static GoRouter create({required AuthBloc authBloc, required AppDependencies deps}) {
    return GoRouter(
      initialLocation: '/splash',
      refreshListenable: _AuthRefresh(authBloc.stream),
      redirect: (context, state) => redirectFor(authBloc.state.status, state.uri),
      routes: [
        GoRoute(path: '/splash', builder: (_, _) => const Scaffold(body: Center(child: CircularProgressIndicator()))),
        GoRoute(path: '/login', builder: (_, _) => const LoginPage()),
        GoRoute(path: '/register', builder: (_, _) => const RegisterPage()),
        StatefulShellRoute.indexedStack(
          builder: (context, state, shell) => HomeShell(shell: shell),
          branches: [
            StatefulShellBranch(routes: [
              GoRoute(
                path: '/movies',
                builder: (context, state) => MultiBlocProvider(
                  providers: [
                    BlocProvider(create: (_) => MoviesCubit(deps.catalog)),
                    BlocProvider(create: (_) => LocationCubit(deps.catalog, deps.location)),
                  ],
                  child: const MoviesPage(),
                ),
                routes: [
                  GoRoute(
                    path: ':id',
                    builder: (context, state) => BlocProvider(
                      create: (_) => MovieDetailCubit(deps.catalog, int.parse(state.pathParameters['id']!)),
                      child: const MovieDetailPage(),
                    ),
                  ),
                ],
              ),
            ]),
            StatefulShellBranch(routes: [
              GoRoute(
                path: '/bookings',
                builder: (context, state) => BlocProvider(create: (_) => BookingsCubit(deps.sessions.require().bookings), child: const BookingsPage()),
                routes: [
                  GoRoute(
                    path: ':id',
                    builder: (context, state) => BlocProvider(
                      create: (_) => TicketCubit(deps.sessions.require().bookings, int.parse(state.pathParameters['id']!)),
                      child: const TicketPage(),
                    ),
                  ),
                ],
              ),
            ]),
            StatefulShellBranch(routes: [GoRoute(path: '/community', builder: (_, _) => const CommunityPage())]),
            StatefulShellBranch(routes: [GoRoute(path: '/profile', builder: (_, _) => const ProfilePage())]),
          ],
        ),
        // Los comentarios son una hoja que sube sobre la pantalla anterior (se ve borrosa detrás), incluida la barra inferior:
        // por eso es una ruta transparente fuera del shell. Se leen sin cuenta; escribir exige sesión (la pantalla lo resuelve).
        GoRoute(
          path: '/movies/:id/reviews',
          pageBuilder: (context, state) => CustomTransitionPage<void>(
            key: state.pageKey,
            opaque: false,
            transitionDuration: const Duration(milliseconds: 300),
            reverseTransitionDuration: const Duration(milliseconds: 250),
            // La hoja y su fondo se animan dentro de la propia pantalla (necesitan la misma animación de la ruta).
            transitionsBuilder: (context, animation, secondaryAnimation, child) => child,
            child: MultiBlocProvider(
              providers: [
                BlocProvider(create: (_) => ReviewsCubit(deps.catalog, int.parse(state.pathParameters['id']!))),
                // Comentarios del tráiler en YouTube: se muestran junto a los de la comunidad.
                BlocProvider(create: (_) => TrailerCommentsCubit(deps.catalog, int.parse(state.pathParameters['id']!))),
              ],
              child: const ReviewsPage(),
            ),
          ),
        ),
        // Pantallas completas, sin la barra inferior.
        GoRoute(
          path: '/showtimes/:id/seats',
          builder: (context, state) => BlocProvider(
            create: (_) => SeatSelectionBloc(repository: deps.seats, realtime: deps.realtimeFactory(), now: deps.clock.now),
            child: SeatMapPage(showtimeId: int.parse(state.pathParameters['id']!), info: state.extra as ShowtimeInfo?),
          ),
        ),
        GoRoute(
          path: '/showtimes/:id/checkout',
          builder: (context, state) => BlocProvider(
            create: (_) => CheckoutCubit(
              showtimeId: int.parse(state.pathParameters['id']!),
              seats: deps.seats,
              bookings: deps.sessions.require().bookings,
            ),
            child: CheckoutPage(info: state.extra as ShowtimeInfo?),
          ),
        ),
      ],
    );
  }
}
