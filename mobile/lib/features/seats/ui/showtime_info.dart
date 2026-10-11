/// Datos de la función que se arrastran entre pantallas (película, sala y hora) para no volver a pedirlos.
class ShowtimeInfo {
  const ShowtimeInfo({required this.movieTitle, required this.roomName, required this.startsAt});

  final String movieTitle;
  final String roomName;
  final DateTime startsAt;
}
