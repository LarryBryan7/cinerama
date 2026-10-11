import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:qr_flutter/qr_flutter.dart';

import '../../../core/connectivity/connectivity_cubit.dart';
import '../../../core/util/formatters.dart';
import '../bloc/ticket_cubit.dart';
import '../data/booking_models.dart';

class TicketPage extends StatefulWidget {
  const TicketPage({super.key});

  @override
  State<TicketPage> createState() => _TicketPageState();
}

class _TicketPageState extends State<TicketPage> {
  @override
  void initState() {
    super.initState();
    unawaited(context.read<TicketCubit>().load());
  }

  Future<void> _confirmCancel(BuildContext context) async {
    final cubit = context.read<TicketCubit>();
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('¿Cancelar la reserva?'),
        content: const Text('Tus asientos quedarán libres para otras personas. Solo puedes cancelar hasta 2 horas antes de la función.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Volver')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Cancelar reserva')),
        ],
      ),
    );
    if (ok == true) await cubit.cancel();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Tu ticket')),
      body: BlocBuilder<TicketCubit, TicketState>(
        builder: (context, state) {
          if (state.status == TicketStatus.loading) return const Center(child: CircularProgressIndicator());
          final booking = state.booking;
          if (state.status == TicketStatus.notFound || booking == null) {
            return const Center(child: Padding(padding: EdgeInsets.all(24), child: Text('No encontramos esa reserva.')));
          }
          final online = context.watch<ConnectivityCubit>().state;
          return _TicketView(
            booking: booking,
            state: state,
            online: online,
            onCancel: () => unawaited(_confirmCancel(context)),
          );
        },
      ),
    );
  }
}

class _TicketView extends StatelessWidget {
  const _TicketView({required this.booking, required this.state, required this.online, required this.onCancel});

  final Booking booking;
  final TicketState state;
  final bool online;
  final VoidCallback onCancel;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final offlineNote = state.fromCache && !booking.isCancelled;

    return ListView(padding: const EdgeInsets.all(16), children: [
      if (booking.isCancelled)
        Card(
          color: theme.colorScheme.errorContainer,
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Text('Reserva cancelada', key: const Key('ticket-cancelled'), style: theme.textTheme.titleMedium?.copyWith(color: theme.colorScheme.onErrorContainer)),
          ),
        ),
      if (offlineNote)
        Semantics(
          liveRegion: true,
          child: Card(
            key: const Key('ticket-offline-note'),
            child: Padding(
              padding: const EdgeInsets.all(12),
              child: Row(children: [
                const Icon(Icons.cloud_off, size: 20),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    'Mostrando tu ticket guardado'
                    '${booking.syncedAt == null ? '' : ' (sincronizado ${Formatters.ago(booking.syncedAt!, DateTime.now().toUtc())})'}. '
                    'El cine confirma su validez al escanearlo.',
                    style: theme.textTheme.bodySmall,
                  ),
                ),
              ]),
            ),
          ),
        ),
      const SizedBox(height: 8),
      Text(booking.movieTitle, style: theme.textTheme.headlineSmall),
      const SizedBox(height: 4),
      Text('${Formatters.dateTime(booking.startsAt)} · ${booking.roomName}', style: theme.textTheme.bodyLarge),
      const SizedBox(height: 16),
      if (booking.hasTicket)
        Center(child: TicketQr(key: const Key('ticket-qr'), code: booking.code, data: booking.ticketPayload!))
      else if (!booking.isCancelled)
        const Card(child: Padding(padding: EdgeInsets.all(16), child: Text('Conéctate para descargar el código QR de esta reserva.'))),
      const SizedBox(height: 16),
      Center(child: SelectableText(booking.code, key: const Key('ticket-code'), style: theme.textTheme.headlineMedium?.copyWith(letterSpacing: 4))),
      const SizedBox(height: 16),
      Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(children: [
            _Row('Asientos', booking.seatLabels.join(', ')),
            _Row('Total', Formatters.money(double.parse(booking.totalAmount))),
            _Row('Estado', booking.isCancelled ? 'Cancelada' : 'Confirmada'),
          ]),
        ),
      ),
      if (state.errorCode != null && state.errorCode != 'network_error') ...[
        const SizedBox(height: 8),
        Semantics(liveRegion: true, child: Text(_errorText(state.errorCode!), key: const Key('ticket-error'), style: TextStyle(color: theme.colorScheme.error))),
      ],
      if (!booking.isCancelled) ...[
        const SizedBox(height: 16),
        OutlinedButton.icon(
          key: const Key('cancel-booking'),
          onPressed: (!online || state.cancelling) ? null : onCancel,
          icon: const Icon(Icons.cancel_outlined),
          label: Text(online ? 'Cancelar reserva' : 'Cancelar (necesita conexión)'),
        ),
      ],
    ]);
  }

  static String _errorText(String code) => switch (code) {
        'cancellation_window_closed' => 'Ya no puedes cancelar: faltan menos de 2 horas para la función.',
        'offline' => 'Necesitas conexión para cancelar la reserva.',
        'booking_already_cancelled' => 'Esta reserva ya estaba cancelada.',
        _ => 'No se pudo completar la operación.',
      };
}

class _Row extends StatelessWidget {
  const _Row(this.label, this.value);

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 4),
        child: Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [Text(label), Flexible(child: Text(value, textAlign: TextAlign.end))]),
      );
}

/// El QR de la entrada. Codifica EXACTAMENTE el contenido firmado por el servidor; la app no agrega ni interpreta nada.
class TicketQr extends StatelessWidget {
  const TicketQr({required this.code, required this.data, super.key});

  final String code;
  final String data;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: 'Código QR de tu entrada. Código de reserva ${code.split('').join(' ')}',
      image: true,
      child: Container(
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12)), // fondo blanco: contraste para escanear
        child: QrImageView(data: data, size: 240, backgroundColor: Colors.white),
      ),
    );
  }
}
