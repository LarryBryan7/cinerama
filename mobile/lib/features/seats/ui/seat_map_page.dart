import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../core/realtime/seat_realtime.dart';
import '../../../core/util/formatters.dart';
import '../../../core/widgets/common_widgets.dart';
import '../bloc/seat_selection_bloc.dart';
import '../data/seat_models.dart';
import 'seat_grid.dart';
import 'seat_tile.dart';
import 'showtime_info.dart';

class SeatMapPage extends StatefulWidget {
  const SeatMapPage({super.key, required this.showtimeId, this.info});

  final int showtimeId;
  final ShowtimeInfo? info;

  @override
  State<SeatMapPage> createState() => _SeatMapPageState();
}

class _SeatMapPageState extends State<SeatMapPage> {
  @override
  void initState() {
    super.initState();
    context.read<SeatSelectionBloc>().add(SeatMapStarted(widget.showtimeId));
  }

  Future<void> _continue(BuildContext context) async {
    await context.push('/showtimes/${widget.showtimeId}/checkout', extra: widget.info);
    // Al volver (por ejemplo, el bloqueo venció en el checkout) se pide el mapa completo.
    if (context.mounted) context.read<SeatSelectionBloc>().add(const SeatMapRefreshRequested());
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.info?.movieTitle ?? 'Elige tus asientos'),
        bottom: widget.info == null
            ? null
            : PreferredSize(
                preferredSize: const Size.fromHeight(24),
                child: Padding(
                  padding: const EdgeInsets.only(left: 16, right: 16, bottom: 8),
                  child: Align(alignment: Alignment.centerLeft, child: Text('${widget.info!.roomName} · ${Formatters.dateTime(widget.info!.startsAt)}')),
                ),
              ),
      ),
      body: BlocConsumer<SeatSelectionBloc, SeatSelectionState>(
        listenWhen: (a, b) => b.notice != null && a.notice != b.notice,
        listener: (context, state) {
          final notice = state.notice!;
          ScaffoldMessenger.of(context)
            ..hideCurrentSnackBar()
            ..showSnackBar(SnackBar(
              content: Text(notice.message),
              backgroundColor: notice.kind == NoticeKind.error ? Theme.of(context).colorScheme.errorContainer : null,
            ));
        },
        builder: (context, state) {
          if (state.status == SeatMapStatus.failure) {
            return ErrorState(
              message: messageForCode(state.errorCode),
              onRetry: () => context.read<SeatSelectionBloc>().add(SeatMapStarted(widget.showtimeId)),
            );
          }
          if (state.status == SeatMapStatus.loading) return const Center(child: CircularProgressIndicator());

          return Column(children: [
            _RealtimeChip(state.realtime),
            Expanded(
              child: SeatGrid(
                seats: state.seats,
                busy: state.busy,
                onTap: (id) => context.read<SeatSelectionBloc>().add(SeatTapped(id)),
              ),
            ),
            const Padding(padding: EdgeInsets.symmetric(vertical: 8), child: SeatLegend()),
            _SummaryBar(state: state, onContinue: () => unawaited(_continue(context))),
          ]);
        },
      ),
    );
  }
}

class _RealtimeChip extends StatelessWidget {
  const _RealtimeChip(this.state);

  final RealtimeState state;

  @override
  Widget build(BuildContext context) {
    final (icon, text) = switch (state) {
      RealtimeState.connected => (Icons.bolt, 'En vivo'),
      RealtimeState.connecting || RealtimeState.reconnecting => (Icons.sync, 'Reconectando… (se actualiza cada pocos segundos)'),
      RealtimeState.idle => (Icons.sync_disabled, 'Sin conexión en vivo (se actualiza cada pocos segundos)'),
    };
    return Semantics(
      container: true,
      label: text,
      child: ExcludeSemantics(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
          child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
            Icon(icon, size: 14, color: state == RealtimeState.connected ? Colors.greenAccent : Colors.orangeAccent),
            const SizedBox(width: 6),
            Flexible(child: Text(text, key: const Key('realtime-status'), style: Theme.of(context).textTheme.bodySmall)),
          ]),
        ),
      ),
    );
  }
}

class _SummaryBar extends StatelessWidget {
  const _SummaryBar({required this.state, required this.onContinue});

  final SeatSelectionState state;
  final VoidCallback onContinue;

  Future<void> _suggest(BuildContext context) async {
    final bloc = context.read<SeatSelectionBloc>();
    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      builder: (_) => BlocProvider.value(value: bloc, child: const _SuggestionsSheet()),
    );
  }

  @override
  Widget build(BuildContext context) {
    final mine = state.mine;
    final theme = Theme.of(context);
    return Material(
      elevation: 8,
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
          child: Row(children: [
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
                Text(
                  mine.isEmpty ? 'Toca un asiento para elegirlo' : '${mine.map((s) => s.label).join(', ')}  ·  ${Formatters.money(state.total)}',
                  key: const Key('selection-summary'),
                  style: theme.textTheme.titleSmall,
                ),
                if (state.remaining != null)
                  Text('Tu selección vence en ${Formatters.countdown(state.remaining!)}', key: const Key('lock-countdown'), style: theme.textTheme.bodySmall),
              ]),
            ),
            TextButton.icon(key: const Key('suggest-button'), onPressed: () => unawaited(_suggest(context)), icon: const Icon(Icons.auto_awesome), label: const Text('Sugerir')),
            const SizedBox(width: 4),
            FilledButton(key: const Key('continue-button'), onPressed: state.canContinue ? onContinue : null, child: const Text('Continuar')),
          ]),
        ),
      ),
    );
  }
}

class _SuggestionsSheet extends StatefulWidget {
  const _SuggestionsSheet();

  @override
  State<_SuggestionsSheet> createState() => _SuggestionsSheetState();
}

class _SuggestionsSheetState extends State<_SuggestionsSheet> {
  int _count = 2;
  late Future<List<SeatSuggestion>> _future;

  @override
  void initState() {
    super.initState();
    _future = context.read<SeatSelectionBloc>().suggest(_count);
  }

  void _setCount(int count) => setState(() {
        _count = count;
        _future = context.read<SeatSelectionBloc>().suggest(count);
      });

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
      child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text('Asientos juntos', style: Theme.of(context).textTheme.titleLarge),
        const SizedBox(height: 8),
        Row(children: [
          const Text('¿Cuántas personas?'),
          const Spacer(),
          IconButton(tooltip: 'Menos personas', onPressed: _count > 1 ? () => _setCount(_count - 1) : null, icon: const Icon(Icons.remove_circle_outline)),
          Text('$_count', style: Theme.of(context).textTheme.titleMedium),
          IconButton(tooltip: 'Más personas', onPressed: _count < 10 ? () => _setCount(_count + 1) : null, icon: const Icon(Icons.add_circle_outline)),
        ]),
        FutureBuilder<List<SeatSuggestion>>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.hasError) return const Padding(padding: EdgeInsets.all(16), child: Text('No se pudieron obtener sugerencias.'));
            if (!snapshot.hasData) return const Padding(padding: EdgeInsets.all(16), child: Center(child: CircularProgressIndicator()));
            final options = snapshot.data!;
            if (options.isEmpty) return const Padding(padding: EdgeInsets.all(16), child: Text('No hay lugares juntos para ese grupo. Prueba con menos personas.'));
            return Column(children: [
              for (final option in options)
                Card(
                  child: ListTile(
                    key: Key('suggestion-${option.labels.first}'),
                    leading: const Icon(Icons.event_seat),
                    title: Text(option.labels.join(', ')),
                    subtitle: Text('Puntaje ${option.score.toStringAsFixed(0)} de 100 · cerca del centro'),
                    onTap: () {
                      context.read<SeatSelectionBloc>().add(SeatsChosen(option.seatIds));
                      Navigator.of(context).pop();
                    },
                  ),
                ),
            ]);
          },
        ),
      ]),
    );
  }
}
