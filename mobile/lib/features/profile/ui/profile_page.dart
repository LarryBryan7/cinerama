import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../auth/bloc/auth_bloc.dart';
import '../../auth/data/auth_repository.dart';
import '../../../core/api/api_exception.dart';
import '../../../core/connectivity/connectivity_cubit.dart';

class ProfilePage extends StatelessWidget {
  const ProfilePage({super.key});

  Future<void> _editName(BuildContext context, String current) async {
    final controller = TextEditingController(text: current);
    final repository = context.read<AuthRepository>();
    final bloc = context.read<AuthBloc>();
    final messenger = ScaffoldMessenger.of(context);

    final name = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Editar nombre'),
        content: TextField(controller: controller, autofocus: true, decoration: const InputDecoration(labelText: 'Nombre')),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancelar')),
          FilledButton(onPressed: () => Navigator.pop(context, controller.text.trim()), child: const Text('Guardar')),
        ],
      ),
    );
    controller.dispose();
    if (name == null || name.isEmpty || name == current) return;

    try {
      bloc.add(AuthUserUpdated(await repository.updateName(name)));
    } on ApiException {
      messenger.showSnackBar(const SnackBar(content: Text('No se pudo guardar el nombre. Revisa tu conexión.')));
    }
  }

  Future<void> _logout(BuildContext context) async {
    final bloc = context.read<AuthBloc>();
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('¿Cerrar sesión?'),
        content: const Text('Se borrarán de este dispositivo tus reservas guardadas y tus tickets. Podrás volver a descargarlos al iniciar sesión.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Volver')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Cerrar sesión')),
        ],
      ),
    );
    if (ok != true) return;
    bloc.add(const AuthLogoutRequested());
    // Al salir, de vuelta a la cartelera (no a la pantalla de login).
    if (context.mounted) context.go('/movies');
  }

  @override
  Widget build(BuildContext context) {
    final user = context.watch<AuthBloc>().state.user;
    final online = context.watch<ConnectivityCubit>().state;
    if (user == null) return const SizedBox.shrink();

    return Scaffold(
      appBar: AppBar(title: const Text('Perfil')),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        ListTile(leading: const Icon(Icons.person_outline), title: Text(user.fullName, key: const Key('profile-name')), subtitle: Text(user.email)),
        const Divider(),
        ListTile(
          key: const Key('edit-name'),
          leading: const Icon(Icons.edit_outlined),
          title: const Text('Editar nombre'),
          subtitle: online ? null : const Text('Necesita conexión'),
          enabled: online,
          onTap: () => unawaited(_editName(context, user.fullName)),
        ),
        ListTile(
          key: const Key('logout'),
          leading: const Icon(Icons.logout),
          title: const Text('Cerrar sesión'),
          onTap: () => unawaited(_logout(context)),
        ),
      ]),
    );
  }
}
