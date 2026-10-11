import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../core/util/formatters.dart';
import '../../../core/widgets/common_widgets.dart';
import '../../seats/ui/showtime_info.dart';
import '../bloc/movie_detail_cubit.dart';
import '../bloc/movies_cubit.dart';
import '../data/movie_models.dart';
import 'movie_preview_video.dart';

class MovieDetailPage extends StatefulWidget {
  const MovieDetailPage({super.key});

  @override
  State<MovieDetailPage> createState() => _MovieDetailPageState();
}

class _MovieDetailPageState extends State<MovieDetailPage> {
  @override
  void initState() {
    super.initState();
    unawaited(context.read<MovieDetailCubit>().load());
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Película')),
      body: BlocBuilder<MovieDetailCubit, MovieDetailState>(
        builder: (context, state) {
          if (state.status == LoadStatus.failure) {
            return ErrorState(message: messageForCode(state.errorCode), onRetry: () => context.read<MovieDetailCubit>().load());
          }
          final movie = state.movie;
          if (movie == null) return const Center(child: CircularProgressIndicator());

          final theme = Theme.of(context);
          final byDay = <String, List<ShowtimeSummary>>{};
          for (final s in movie.showtimes) {
            byDay.putIfAbsent(Formatters.day(s.startsAt), () => []).add(s);
          }

          return ListView(padding: const EdgeInsets.all(16), children: [
            // El video corto va arriba. Cada película tiene el suyo (la key evita reutilizar el reproductor de otra).
            if (movie.previewVideoUrl != null) ...[
              MoviePreviewVideo(key: ValueKey('preview-${movie.id}'), videoUrl: movie.previewVideoUrl!, posterUrl: movie.posterUrl),
              const SizedBox(height: 16),
            ],
            Text(movie.title, style: theme.textTheme.headlineSmall),
            const SizedBox(height: 8),
            Wrap(spacing: 8, runSpacing: 4, children: [
              Chip(label: Text(movie.classification)),
              Chip(label: Text(Formatters.duration(movie.durationMinutes))),
              for (final g in movie.genres) Chip(label: Text(g)),
            ]),
            if (movie.synopsis != null) ...[
              const SizedBox(height: 12),
              Text(movie.synopsis!, style: theme.textTheme.bodyLarge),
            ],
            const SizedBox(height: 12),
            _RatingRow(movie: movie),
            const SizedBox(height: 24),
            Text('Funciones', style: theme.textTheme.titleLarge),
            const SizedBox(height: 8),
            if (movie.status == MovieStatus.preSale && movie.releaseDate != null)
              Card(
                key: const Key('presale-note'),
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Text('Preventa abierta: ya puedes reservar tus asientos. Se estrena el ${Formatters.shortDate(movie.releaseDate!)}.'),
                ),
              ),
            if (movie.status == MovieStatus.comingSoon)
              Text(
                movie.releaseDate == null ? 'Próximamente. Aún no hay funciones a la venta.' : 'Se estrena el ${Formatters.shortDate(movie.releaseDate!)}. Aún no hay funciones a la venta.',
                key: const Key('coming-soon-note'),
              )
            else if (movie.showtimes.isEmpty)
              const Text('No hay funciones programadas por ahora.'),
            for (final entry in byDay.entries) ...[
              Padding(
                padding: const EdgeInsets.only(top: 12, bottom: 4),
                child: Text(entry.key, style: theme.textTheme.titleSmall?.copyWith(color: theme.colorScheme.primary)),
              ),
              for (final s in entry.value) _ShowtimeTile(movie: movie, showtime: s),
            ],
          ]);
        },
      ),
    );
  }
}

class _ShowtimeTile extends StatelessWidget {
  const _ShowtimeTile({required this.movie, required this.showtime});

  final MovieDetail movie;
  final ShowtimeSummary showtime;

  @override
  Widget build(BuildContext context) {
    final soldOut = showtime.availableSeats == 0;
    return Card(
      child: ListTile(
        key: Key('showtime-${showtime.id}'),
        enabled: !soldOut,
        leading: Text(Formatters.time(showtime.startsAt), style: Theme.of(context).textTheme.titleLarge),
        title: Text(
          [if (showtime.cinemaName.isNotEmpty) showtime.cinemaName, showtime.roomName, Formatters.money(showtime.price)].join(' · '),
        ),
        subtitle: Text(soldOut ? 'Agotada' : '${showtime.availableSeats} asientos libres'),
        trailing: const Icon(Icons.chevron_right),
        onTap: soldOut
            ? null
            : () => context.push(
                  '/showtimes/${showtime.id}/seats',
                  extra: ShowtimeInfo(movieTitle: movie.title, roomName: showtime.roomName, startsAt: showtime.startsAt),
                ),
      ),
    );
  }
}

/// Calificación promedio de la app y acceso a los comentarios. Son dos cosas de fuentes distintas y se muestran por separado: la
/// calificación sale de las opiniones de la app (sin opiniones no se muestra un "0": se invita a opinar si ya se estrenó); los
/// comentarios son los del tráiler en YouTube (YouTube pide no mezclar sus resultados con los de otras fuentes).
class _RatingRow extends StatelessWidget {
  const _RatingRow({required this.movie});

  final MovieDetail movie;

  @override
  Widget build(BuildContext context) {
    final rating = movie.averageRating;
    final label = rating == null
        ? (movie.status == MovieStatus.nowShowing ? 'Sé el primero en opinar' : 'Aún sin opiniones')
        : '${rating.toStringAsFixed(1)} · ${movie.reviewCount} ${movie.reviewCount == 1 ? 'opinión' : 'opiniones'} en la app';
    final theme = Theme.of(context);
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Row(children: [
          Icon(rating == null ? Icons.star_border : Icons.star, color: theme.colorScheme.primary),
          const SizedBox(width: 8),
          Expanded(child: Text(label, key: const Key('detail-rating'), style: theme.textTheme.bodyLarge)),
        ]),
      ),
      InkWell(
        key: const Key('detail-reviews'),
        onTap: () => context.push('/movies/${movie.id}/reviews'),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 8),
          child: Row(children: [
            Icon(Icons.chat_bubble_outline, color: theme.colorScheme.primary),
            const SizedBox(width: 8),
            Expanded(child: Text('Comentarios del tráiler en YouTube', style: theme.textTheme.bodyLarge)),
            const Icon(Icons.chevron_right),
          ]),
        ),
      ),
    ]);
  }
}
