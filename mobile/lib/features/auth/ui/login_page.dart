import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../bloc/auth_bloc.dart';

class LoginPage extends StatefulWidget {
  const LoginPage({super.key});

  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final _form = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _password = TextEditingController();

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  void _submit() {
    if (_form.currentState!.validate()) {
      context.read<AuthBloc>().add(AuthLoginRequested(_email.text, _password.text));
    }
  }

  /// Conserva a dónde iba la persona (`from`) al pasar por el registro.
  String _registerLocation(BuildContext context) {
    final from = GoRouterState.of(context).uri.queryParameters['from'];
    return from == null ? '/register' : Uri(path: '/register', queryParameters: {'from': from}).toString();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: BlocBuilder<AuthBloc, AuthState>(
                builder: (context, state) => Form(
                  key: _form,
                  child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                    const Icon(Icons.local_movies, size: 56),
                    const SizedBox(height: 8),
                    Text('Cinerama', textAlign: TextAlign.center, style: Theme.of(context).textTheme.headlineLarge),
                    const SizedBox(height: 4),
                    Text('Reserva tus entradas', textAlign: TextAlign.center, style: Theme.of(context).textTheme.bodyLarge),
                    const SizedBox(height: 32),
                    TextFormField(
                      key: const Key('login-email'),
                      controller: _email,
                      keyboardType: TextInputType.emailAddress,
                      autofillHints: const [AutofillHints.email],
                      textInputAction: TextInputAction.next,
                      decoration: InputDecoration(labelText: 'Email', errorText: state.fieldErrors['email']?.first),
                      validator: (v) => (v == null || !v.contains('@')) ? 'Ingresa un email válido' : null,
                    ),
                    const SizedBox(height: 16),
                    TextFormField(
                      key: const Key('login-password'),
                      controller: _password,
                      obscureText: true,
                      autofillHints: const [AutofillHints.password],
                      textInputAction: TextInputAction.done,
                      onFieldSubmitted: (_) => _submit(),
                      decoration: const InputDecoration(labelText: 'Contraseña'),
                      validator: (v) => (v == null || v.isEmpty) ? 'Ingresa tu contraseña' : null,
                    ),
                    if (state.message != null) ...[
                      const SizedBox(height: 16),
                      Semantics(
                        liveRegion: true,
                        child: Text(state.message!, key: const Key('auth-message'), style: TextStyle(color: Theme.of(context).colorScheme.error)),
                      ),
                    ],
                    const SizedBox(height: 24),
                    FilledButton(
                      key: const Key('login-submit'),
                      onPressed: state.busy ? null : _submit,
                      child: state.busy
                          ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2))
                          : const Text('Iniciar sesión'),
                    ),
                    const SizedBox(height: 8),
                    TextButton(onPressed: state.busy ? null : () => context.push(_registerLocation(context)), child: const Text('Crear una cuenta')),
                    TextButton(key: const Key('login-browse'), onPressed: state.busy ? null : () => context.go('/movies'), child: const Text('Seguir viendo la cartelera')),
                  ]),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
