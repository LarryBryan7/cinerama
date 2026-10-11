import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../core/util/formatters.dart';
import '../bloc/bookings_cubit.dart';
import '../data/booking_models.dart';

class BookingsPage extends StatefulWidget {
  const BookingsPage({super.key});

  @override
  State<BookingsPage> createState() => _BookingsPageState();
}

class _BookingsPageState extends State<BookingsPage> {
  @override
  void initState() {
    super.initState();
    unawaited(context.read<BookingsCubit>().load());
  }

  Future<void> _open(BuildContext context, Booking booking) async {
    final cubit = context.read<BookingsCubit>();
    await context.push('/bookings/${booking.id}');
    unawaited(cubit.load()); // al volver pudo haberse cancelado
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Mis reservas')),
      body: BlocBuilder<BookingsCubit, BookingsState>(
        builder: (context, state) {
          if (state.items.isEmpty && state.loading) return const Center(child: CircularProgressIndicator());

          return Column(children: [
            if (state.fromCache && !state.loading && state.lastSync != null)
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                child: Text(
                  'Mostrando lo guardado en tu dispositivo (sincronizado ${Formatters.ago(state.lastSync!, DateTime.now().toUtc())}).',
                  key: const Key('bookings-cache-note'),
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ),
            Expanded(
              child: state.items.isEmpty
                  ? const Center(child: Padding(padding: EdgeInsets.all(24), child: Text('Todavía no tienes reservas.', textAlign: TextAlign.center)))
                  : RefreshIndicator(
                      onRefresh: () => context.read<BookingsCubit>().load(),
                      child: ListView.builder(
                        itemCount: state.items.length,
                        itemBuilder: (context, i) => _BookingTile(booking: state.items[i], onTap: () => unawaited(_open(context, state.items[i]))),
                      ),
                    ),
            ),
          ]);
        },
      ),
    );
  }
}

class _BookingTile extends StatelessWidget {
  const _BookingTile({required this.booking, required this.onTap});

  final Booking booking;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: ListTile(
        key: Key('booking-${booking.id}'),
        onTap: onTap,
        leading: Icon(booking.isCancelled ? Icons.cancel_outlined : Icons.confirmation_number_outlined),
        title: Text(booking.movieTitle),
        subtitle: Text('${Formatters.dateTime(booking.startsAt)}\n${booking.roomName} · ${booking.seatLabels.join(', ')}'),
        isThreeLine: true,
        trailing: Column(mainAxisAlignment: MainAxisAlignment.center, crossAxisAlignment: CrossAxisAlignment.end, children: [
          Text(booking.code, style: theme.textTheme.labelLarge),
          Text(booking.isCancelled ? 'Cancelada' : 'Confirmada', style: theme.textTheme.bodySmall?.copyWith(color: booking.isCancelled ? theme.colorScheme.error : null)),
        ]),
      ),
    );
  }
}
