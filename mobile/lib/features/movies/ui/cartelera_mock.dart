/// IMÁGENES DE RESPALDO y VALORES DE MAQUETA de la pantalla Cartelera.
///
/// Regla del proyecto (ver docs/ui-figma.md): lo que Figma muestra y el backend todavía no expone se dibuja con valores de
/// muestra. Desde la fase 3.5 el backend expone todo lo que muestra la tarjeta (sinopsis, calificación, opinión destacada,
/// cines, estado de estreno, ubicación). Lo que queda aquí es: las imágenes de respaldo cuando una película no trae
/// póster o la imagen no se puede descargar, y los dos valores de maqueta de abajo. Nada de esto es información real.
library;

class CarteleraMock {
  const CarteleraMock._();

  /// Pósters de muestra para las películas que no traen `posterUrl` o cuya imagen falla. Se reparten por posición.
  static const posters = [
    'assets/mock/poster_hero.jpg',
    'assets/mock/poster_prev.jpg',
    'assets/mock/poster_next.jpg',
  ];

  static String posterFor(int index) => posters[index % posters.length];

  /// MAQUETA (decisión del autor, 2026-10-08: "solo diseña, lo veré luego"). Contador de duración que Figma dibuja sobre la
  /// imagen. Todavía no hay dato real (el servidor no guarda la duración del video ni del tráiler). Se muestra solo en las
  /// películas que tienen video o tráiler.
  static const videoLength = '1:37';

  /// MAQUETA (misma decisión). Cantidad de "me gusta" que Figma dibuja junto al corazón de cada comentario. Los comentarios
  /// todavía no tienen "me gusta" en el servidor.
  static const commentLikes = 2;
}
