import 'package:cinerama_mobile/core/location/device_location.dart';
import 'package:cinerama_mobile/core/location/geo.dart';
import 'package:cinerama_mobile/core/util/formatters.dart';
import 'package:cinerama_mobile/features/movies/data/movie_models.dart';
import 'package:cinerama_mobile/features/movies/ui/cartelera_slide.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/device_location_fake.dart';
import '../helpers/movie_helpers.dart';

void main() {
  group('Geo.distanceKm (haversine)', () {
    test('un punto consigo mismo está a 0', () => expect(Geo.distanceKm(const GeoPoint(-9.07, -78.59), -9.07, -78.59), 0));

    test('un grado de latitud son unos 111,2 km', () => expect(Geo.distanceKm(const GeoPoint(0, 0), 1, 0), closeTo(111.19, .05)));

    test('un grado de longitud en el ecuador también', () => expect(Geo.distanceKm(const GeoPoint(0, 0), 0, 1), closeTo(111.19, .05)));

    test('es simétrica', () {
      const a = GeoPoint(-9.0745, -78.5936), b = GeoPoint(-12.0464, -77.0428);
      expect(Geo.distanceKm(a, b.latitude, b.longitude), closeTo(Geo.distanceKm(b, a.latitude, a.longitude), 1e-9));
    });

    test('Chimbote a Lima son unos 360 km', () => expect(Geo.distanceKm(const GeoPoint(-9.0745, -78.5936), -12.0464, -77.0428), closeTo(360, 15)));

    test('puntos antípodas no producen un error numérico (raíz de un valor mayor que 1)', () {
      expect(Geo.distanceKm(const GeoPoint(0, 0), 0, 180).isFinite, isTrue);
      expect(Geo.distanceKm(const GeoPoint(0, 0), 0, 180), closeTo(20015, 10));
    });
  });

  group('Formatters.distance', () {
    test('por debajo del kilómetro, en metros redondeados a 10', () {
      expect(Formatters.distance(0.0708), '70 m');
      expect(Formatters.distance(0.999), '1000 m');
      expect(Formatters.distance(0.004), '0 m');
    });
    test('hasta 100 km, con un decimal', () {
      expect(Formatters.distance(1.1), '1.1 km');
      expect(Formatters.distance(7.2), '7.2 km');
      expect(Formatters.distance(99.94), '99.9 km');
    });
    test('de 100 km en adelante, entero y con separador de miles', () {
      expect(Formatters.distance(360.4), '360 km');
      expect(Formatters.distance(9812.3), '9 812 km');
      expect(Formatters.distance(12345.6), '12 346 km');
    });
  });

  group('NearbyCinemas', () {
    final cinerama = cinema(id: 1, name: 'Cinerama - Chimbote');
    const star = CinemaRef(id: 2, name: 'Cine Star - Chimbote', address: 'Referencial', city: 'Chimbote', latitude: -9.1239, longitude: -78.5207);
    const lima = CinemaRef(id: 3, name: 'Cine Lima', address: 'Calle 1', city: 'Lima', latitude: -12.0464, longitude: -77.0428);

    test('sin ubicación: por nombre, y el detalle es la dirección', () {
      final nearby = NearbyCinemas.of(movieSummary(cinemas: [star, cinerama]), city: 'Chimbote');

      expect(nearby.cinemas.map((c) => c.name), ['Cine Star - Chimbote', 'Cinerama - Chimbote']); // orden alfabético
      expect(nearby.detailOf(cinerama), 'Av. José Pardo 123');
      expect(nearby.kmTo(cinerama), isNull);
    });

    test('con ubicación: del más cercano al más lejano, con "cerca de ti" solo hasta 25 km', () {
      final nearby = NearbyCinemas.of(movieSummary(cinemas: [lima, star, cinerama]), position: nearChimbote);

      expect(nearby.cinemas.map((c) => c.name), ['Cinerama - Chimbote', 'Cine Star - Chimbote', 'Cine Lima']);
      expect(nearby.detailOf(cinerama), 'A 70 m cerca de ti');
      expect(nearby.detailOf(star), matches(r'^A \d\.\d km cerca de ti$'));
      expect(nearby.detailOf(lima), matches(r'^A \d{3} km$'), reason: 'a 360 km ya no es "cerca de ti"');
    });

    test('el límite de "cerca" es exactamente kNearbyKm', () {
      final nearby = NearbyCinemas.of(movieSummary(cinemas: [cinerama]), position: nearChimbote);
      expect(kNearbyKm, 25);
      expect(nearby.detailOf(cinerama), endsWith('cerca de ti'));
    });

    test('filtra por la ciudad elegida y, si ninguna coincide, muestra todos (no deja la tarjeta sin cines)', () {
      expect(NearbyCinemas.of(movieSummary(cinemas: [cinerama, lima]), city: 'Lima').cinemas.map((c) => c.id), [3]);
      expect(NearbyCinemas.of(movieSummary(cinemas: [cinerama, lima]), city: 'Trujillo').cinemas, hasLength(2));
      expect(NearbyCinemas.of(movieSummary(cinemas: [cinerama, lima])).cinemas, hasLength(2));
    });

    test('una película sin cines no tiene nada que mostrar', () {
      expect(NearbyCinemas.of(movieSummary(cinemas: const []), city: 'Chimbote', position: nearChimbote).cinemas, isEmpty);
    });
  });
}
