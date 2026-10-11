/// Código de un video de YouTube (11 caracteres). Misma regla que el servidor: se extrae de una URL de YouTube
/// (watch, youtu.be, embed, shorts, live) o se acepta tal cual, y cualquier otra cosa es inválida.
class YouTubeId {
  const YouTubeId._();

  static final _id = RegExp(r'^[A-Za-z0-9_-]{11}$');
  static final _inUrl = RegExp(r'(?:[?&]v=|/embed/|/shorts/|/live/)([A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])');
  static const _hosts = {'youtu.be', 'youtube.com', 'www.youtube.com', 'm.youtube.com', 'youtube-nocookie.com', 'www.youtube-nocookie.com'};

  static String? parse(String? value) {
    final text = value?.trim();
    if (text == null || text.isEmpty) return null;
    if (_id.hasMatch(text)) return text;

    final uri = Uri.tryParse(text);
    if (uri == null || !(uri.scheme == 'http' || uri.scheme == 'https') || !_hosts.contains(uri.host.toLowerCase())) return null;

    if (uri.host.toLowerCase() == 'youtu.be') {
      final first = uri.pathSegments.where((s) => s.isNotEmpty).firstOrNull;
      return first != null && _id.hasMatch(first) ? first : null;
    }

    final path = uri.hasQuery ? '${uri.path}?${uri.query}' : uri.path;
    return _inUrl.firstMatch(path)?.group(1);
  }
}
