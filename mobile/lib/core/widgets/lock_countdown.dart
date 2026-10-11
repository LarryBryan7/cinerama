import 'dart:async';

import 'package:flutter/material.dart';

import '../util/formatters.dart';

/// Cuenta regresiva hasta el vencimiento de un bloqueo, en hora del servidor.
class LockCountdown extends StatefulWidget {
  const LockCountdown({super.key, required this.until, required this.now, this.prefix = 'Tu selección vence en '});

  final DateTime until;
  final DateTime Function() now;
  final String prefix;

  @override
  State<LockCountdown> createState() => _LockCountdownState();
}

class _LockCountdownState extends State<LockCountdown> {
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) => setState(() {}));
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final remaining = widget.until.difference(widget.now());
    final expired = remaining <= Duration.zero;
    return Text(
      expired ? 'Tu selección venció' : '${widget.prefix}${Formatters.countdown(remaining)}',
      key: const Key('checkout-countdown'),
      style: TextStyle(color: expired ? Theme.of(context).colorScheme.error : null),
    );
  }
}
