import 'package:cinerama_mobile/core/router/app_router.dart';
import 'package:cinerama_mobile/features/auth/bloc/auth_bloc.dart';
import 'package:flutter_test/flutter_test.dart';

String? redirect(AuthStatus status, String location) => AppRouter.redirectFor(status, Uri.parse(location));

void main() {
  group('sin sesión: la cartelera es lo primero que se ve', () {
    test('al terminar de cargar, la pantalla de carga lleva a la cartelera (no al login)', () {
      expect(redirect(AuthStatus.unauthenticated, '/splash'), '/movies');
    });

    test('cartelera, detalle de película y comunidad se pueden ver sin cuenta', () {
      for (final path in ['/movies', '/movies/5', '/community', '/login', '/register']) {
        expect(redirect(AuthStatus.unauthenticated, path), isNull, reason: path);
      }
    });

    test('elegir asientos, pagar, ver entradas o el perfil piden iniciar sesión y recuerdan a dónde iba', () {
      for (final path in ['/showtimes/98/seats', '/showtimes/98/checkout', '/bookings', '/bookings/7', '/profile']) {
        final target = Uri.parse(redirect(AuthStatus.unauthenticated, path)!);
        expect(target.path, '/login', reason: path);
        expect(target.queryParameters['from'], path, reason: path);
      }
    });
  });

  group('con sesión', () {
    test('al iniciar sesión vuelve a donde iba', () {
      expect(redirect(AuthStatus.authenticated, '/login?from=%2Fshowtimes%2F98%2Fseats'), '/showtimes/98/seats');
      expect(redirect(AuthStatus.authenticated, '/register?from=%2Fbookings'), '/bookings');
    });

    test('sin destino previo, entra a la cartelera', () {
      expect(redirect(AuthStatus.authenticated, '/login'), '/movies');
      expect(redirect(AuthStatus.authenticated, '/splash'), '/movies');
    });

    test('solo acepta destinos internos: ni URLs externas ni las pantallas de acceso', () {
      for (final from in ['https://evil.example/x', '//evil.example', 'javascript:alert(1)', '/login', '/register', '/splash']) {
        expect(redirect(AuthStatus.authenticated, '/login?from=${Uri.encodeComponent(from)}'), '/movies', reason: from);
      }
    });

    test('las pantallas normales no se redirigen', () {
      for (final path in ['/movies', '/bookings', '/profile', '/showtimes/98/seats']) {
        expect(redirect(AuthStatus.authenticated, path), isNull, reason: path);
      }
    });
  });

  test('mientras no se sabe si hay sesión, todo espera en la pantalla de carga', () {
    expect(redirect(AuthStatus.unknown, '/movies'), '/splash');
    expect(redirect(AuthStatus.unknown, '/splash'), isNull);
  });
}
