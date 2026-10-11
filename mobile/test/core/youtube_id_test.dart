import 'package:cinerama_mobile/core/util/formatters.dart';
import 'package:cinerama_mobile/core/youtube/youtube_id.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('YouTubeId.parse (misma regla que el servidor)', () {
    const id = '1McLG8FgNaI';

    for (final input in [
      id,
      'https://www.youtube.com/watch?v=$id',
      'https://www.youtube.com/watch?v=$id&t=30s',
      'https://www.youtube.com/watch?feature=share&v=$id',
      'https://youtu.be/$id',
      'https://youtu.be/$id?si=abc',
      'https://m.youtube.com/watch?v=$id',
      'https://www.youtube.com/embed/$id',
      'https://www.youtube.com/shorts/$id',
      'https://www.youtube.com/live/$id',
      'https://www.youtube-nocookie.com/embed/$id',
      '  https://youtu.be/$id  ',
    ]) {
      test('extrae el código de "$input"', () => expect(YouTubeId.parse(input), id));
    }

    for (final input in [
      null,
      '',
      '   ',
      'e', // basura que publica la fuente
      'dsaf',
      '1McLG8FgNaIX',
      '1McLG8FgNa',
      'https://www.youtube.com/watch?v=corto',
      'https://www.youtube.com/watch?v=${id}extra',
      'https://vimeo.com/watch?v=$id',
      'https://evil.example/youtu.be/$id',
      'https://youtube.com.evil.example/watch?v=$id',
      'javascript:alert(1)//watch?v=$id',
      'ftp://www.youtube.com/watch?v=$id',
      'https://www.youtube.com/',
      'https://youtu.be/',
      'https://youtu.be/corto',
    ]) {
      test('rechaza ${input == null ? 'null' : '"$input"'}', () => expect(YouTubeId.parse(input), isNull));
    }
  });

  group('Formatters.compactCount ("me gusta")', () {
    test('menos de mil, tal cual', () {
      expect(Formatters.compactCount(0), '0');
      expect(Formatters.compactCount(7), '7');
      expect(Formatters.compactCount(999), '999');
    });
    test('miles con un decimal si hace falta', () {
      expect(Formatters.compactCount(1000), '1 mil');
      expect(Formatters.compactCount(1234), '1,2 mil');
      expect(Formatters.compactCount(9999), '10 mil');
      expect(Formatters.compactCount(15400), '15 mil');
      expect(Formatters.compactCount(999999), '999 mil');
    });
    test('millones', () {
      expect(Formatters.compactCount(1000000), '1 M');
      expect(Formatters.compactCount(2400000), '2,4 M');
    });
  });
}
