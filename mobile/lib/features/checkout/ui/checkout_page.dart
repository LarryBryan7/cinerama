import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../core/api/server_clock.dart';
import '../../../core/connectivity/connectivity_cubit.dart';
import '../../../core/util/formatters.dart';
import '../../../core/widgets/lock_countdown.dart';
import '../../seats/ui/showtime_info.dart';
import '../bloc/checkout_cubit.dart';

class CheckoutPage extends StatefulWidget {
  const CheckoutPage({super.key, this.info});

  final ShowtimeInfo? info;

  @override
  State<CheckoutPage> createState() => _CheckoutPageState();
}

class _CheckoutPageState extends State<CheckoutPage> {
  @override
  void initState() {
    super.initState();
    unawaited(context.read<CheckoutCubit>().load());
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Confirmar reserva')),
      body: BlocConsumer<CheckoutCubit, CheckoutState>(
        listener: (context, state) {
          if (state.status == CheckoutStatus.confirmed) {
            context.go('/bookings/${state.booking!.id}'); // reemplaza la pila: no se vuelve a un checkout ya confirmado
          } else if (state.status == CheckoutStatus.seatsLost) {
            ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Tu selección venció o cambió. Elige tus asientos de nuevo.')));
            context.pop();
          }
        },
        builder: (context, state) {
          if (state.status == CheckoutStatus.loading || state.status == CheckoutStatus.seatsLost || state.status == CheckoutStatus.confirmed) {
            return const Center(child: CircularProgressIndicator());
          }
          final online = context.watch<ConnectivityCubit>().state;
          final confirming = state.status == CheckoutStatus.confirming;

          return ListView(padding: const EdgeInsets.all(16), children: [
            if (widget.info != null) ...[
              Text(widget.info!.movieTitle, style: theme.textTheme.titleLarge),
              Text('${widget.info!.roomName} · ${Formatters.dateTime(widget.info!.startsAt)}'),
              const SizedBox(height: 16),
            ],
            Card(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text('Tus asientos', style: theme.textTheme.titleMedium),
                  const SizedBox(height: 8),
                  Wrap(spacing: 8, children: [for (final s in state.seats) Chip(label: Text(s.label))]),
                  const Divider(height: 24),
                  Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                    Text('${state.seats.length} × ${Formatters.money(state.price)}'),
                    Text(Formatters.money(state.total), key: const Key('checkout-total'), style: theme.textTheme.titleLarge),
                  ]),
                ]),
              ),
            ),
            if (state.lockedUntil != null) ...[
              const SizedBox(height: 12),
              LockCountdown(until: state.lockedUntil!, now: context.read<ServerClock>().now),
            ],
            const SizedBox(height: 16),
            Text(
              'Pago simulado: no se cobra nada ni se guardan datos de tarjetas.',
              style: theme.textTheme.bodySmall,
            ),
            if (state.status == CheckoutStatus.failure) ...[
              const SizedBox(height: 12),
              Semantics(
                liveRegion: true,
                child: Text(_failureMessage(state.errorCode), key: const Key('checkout-error'), style: TextStyle(color: theme.colorScheme.error)),
              ),
            ],
            if (!online) ...[
              const SizedBox(height: 12),
              const Text('Necesitas conexión para confirmar la reserva.'),
            ],
            const SizedBox(height: 24),
            FilledButton(
              key: const Key('confirm-button'),
              onPressed: (confirming || !online) ? null : () => unawaited(context.read<CheckoutCubit>().confirm()),
              child: confirming ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2)) : const Text('Confirmar reserva (pago simulado)'),
            ),
          ]);
        },
      ),
    );
  }

  static String _failureMessage(String? code) => switch (code) {
        'offline' || 'network_error' => 'Sin conexión con el servidor. Tu selección sigue guardada: vuelve a intentarlo.',
        'sales_closed' || 'showtime_cancelled' => 'La venta de esta función ya cerró.',
        _ => 'No se pudo confirmar la reserva. Inténtalo de nuevo.',
      };
}
