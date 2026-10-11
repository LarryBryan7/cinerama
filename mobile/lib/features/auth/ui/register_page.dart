import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../bloc/auth_bloc.dart';

class RegisterPage extends StatefulWidget {
  const RegisterPage({super.key});

  @override
  State<RegisterPage> createState() => _RegisterPageState();
}

class _RegisterPageState extends State<RegisterPage> {
  final _form = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();

  @override
  void dispose() {
    _name.dispose();
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  void _submit() {
    if (_form.currentState!.validate()) {
      context.read<AuthBloc>().add(AuthRegisterRequested(_name.text, _email.text, _password.text));
    }
  }

  // RN-02: al menos 8 caracteres, con una letra y un número. El servidor valida igual; esto solo evita un viaje de ida y vuelta.
  static String? _passwordError(String? v) {
    if (v == null || v.length < 8) return 'Usa al menos 8 caracteres';
    if (!v.contains(RegExp(r'[A-Za-zÁ-ú]'))) return 'Incluye al menos una letra';
    if (!v.contains(RegExp(r'\d'))) return 'Incluye al menos un número';
    return null;
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Crear cuenta')),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: BlocBuilder<AuthBloc, AuthState>(
            builder: (context, state) => Form(
              key: _form,
              child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                TextFormField(
                  controller: _name,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(labelText: 'Nombre', errorText: state.fieldErrors['fullName']?.first),
                  validator: (v) => (v == null || v.trim().isEmpty) ? 'Ingresa tu nombre' : null,
                ),
                const SizedBox(height: 16),
                TextFormField(
                  controller: _email,
                  keyboardType: TextInputType.emailAddress,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(labelText: 'Email', errorText: state.fieldErrors['email']?.first),
                  validator: (v) => (v == null || !v.contains('@')) ? 'Ingresa un email válido' : null,
                ),
                const SizedBox(height: 16),
                TextFormField(
                  controller: _password,
                  obscureText: true,
                  decoration: InputDecoration(
                    labelText: 'Contraseña',
                    helperText: 'Al menos 8 caracteres, con una letra y un número',
                    errorText: state.fieldErrors['password']?.first,
                  ),
                  validator: _passwordError,
                ),
                if (state.message != null) ...[
                  const SizedBox(height: 16),
                  Semantics(liveRegion: true, child: Text(state.message!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
                ],
                const SizedBox(height: 24),
                FilledButton(
                  onPressed: state.busy ? null : _submit,
                  child: state.busy ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2)) : const Text('Crear cuenta'),
                ),
              ]),
            ),
          ),
        ),
      ),
    );
  }
}
