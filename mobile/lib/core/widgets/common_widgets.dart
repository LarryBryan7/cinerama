import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../connectivity/connectivity_cubit.dart';

/// Aviso permanente cuando no hay conexión REAL con la API (no solo "sin Wi-Fi"). Se anuncia a los lectores de pantalla.
class OfflineBanner extends StatelessWidget {
  const OfflineBanner({super.key});

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<ConnectivityCubit, bool>(
      builder: (context, online) {
        if (online) return const SizedBox.shrink();
        final scheme = Theme.of(context).colorScheme;
        return Semantics(
          liveRegion: true,
          container: true,
          label: 'Sin conexión con el servidor. Puedes ver tus entradas guardadas.',
          child: Material(
            color: scheme.errorContainer,
            child: ExcludeSemantics(
              child: InkWell(
                onTap: () => context.read<ConnectivityCubit>().recheck(),
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                  child: Row(children: [
                    Icon(Icons.cloud_off, size: 18, color: scheme.onErrorContainer),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        'Sin conexión. Puedes ver tus entradas guardadas; reservar necesita conexión.',
                        style: TextStyle(color: scheme.onErrorContainer, fontSize: 13),
                      ),
                    ),
                    Text('Reintentar', style: TextStyle(color: scheme.onErrorContainer, fontWeight: FontWeight.w600, fontSize: 13)),
                  ]),
                ),
              ),
            ),
          ),
        );
      },
    );
  }
}

class ErrorState extends StatelessWidget {
  const ErrorState({super.key, required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            const Icon(Icons.error_outline, size: 40),
            const SizedBox(height: 12),
            Text(message, textAlign: TextAlign.center),
            const SizedBox(height: 16),
            FilledButton.tonal(onPressed: onRetry, child: const Text('Reintentar')),
          ]),
        ),
      );
}

String messageForCode(String? code) => switch (code) {
      'network_error' || 'offline' => 'No hay conexión con el servidor. Revisa tu conexión e inténtalo de nuevo.',
      'movie_not_found' || 'showtime_not_found' => 'No encontramos lo que buscas.',
      'movie_not_released' => 'Solo puedes opinar sobre una película que ya se estrenó.',
      'rate_limited' => 'Hiciste demasiados cambios seguidos. Espera un momento e inténtalo de nuevo.',
      'validation_failed' => 'Revisa tu opinión: la puntuación va de 1 a 5 y el comentario admite hasta 500 caracteres.',
      'review_not_found' => 'No tienes una opinión sobre esta película.',
      'sales_not_open' => 'La venta de entradas para esta película todavía no está habilitada.',
      'cinema_closed' => 'El cine de esta función no está disponible.',
      _ => 'Ocurrió un error. Inténtalo de nuevo.',
    };
