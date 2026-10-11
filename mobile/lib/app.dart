import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:go_router/go_router.dart';

import 'core/api/server_clock.dart';
import 'core/connectivity/connectivity_cubit.dart';
import 'core/di/app_dependencies.dart';
import 'core/router/app_router.dart';
import 'core/theme/app_theme.dart';
import 'core/widgets/common_widgets.dart';
import 'features/auth/bloc/auth_bloc.dart';
import 'features/auth/data/auth_repository.dart';

class CineramaApp extends StatefulWidget {
  const CineramaApp({super.key, required this.dependencies});

  final AppDependencies dependencies;

  @override
  State<CineramaApp> createState() => _CineramaAppState();
}

class _CineramaAppState extends State<CineramaApp> {
  late final AuthBloc _auth;
  late final ConnectivityCubit _connectivity;
  late final GoRouter _router;

  @override
  void initState() {
    super.initState();
    final deps = widget.dependencies;
    _auth = AuthBloc(repository: deps.auth, sessions: deps.sessions, sessionLost: deps.sessionLost)..add(const AuthStarted());
    _connectivity = ConnectivityCubit(deps.connectivity);
    _router = AppRouter.create(authBloc: _auth, deps: deps);
  }

  @override
  void dispose() {
    _router.dispose();
    unawaited(_auth.close());
    unawaited(_connectivity.close());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final deps = widget.dependencies;
    return MultiRepositoryProvider(
      providers: [
        RepositoryProvider.value(value: deps),
        RepositoryProvider<ServerClock>.value(value: deps.clock),
        RepositoryProvider<AuthRepository>.value(value: deps.auth),
      ],
      child: MultiBlocProvider(
        providers: [
          BlocProvider.value(value: _auth),
          BlocProvider.value(value: _connectivity),
        ],
        child: MaterialApp.router(
          title: 'Cinerama',
          theme: AppTheme.dark(),
          routerConfig: _router,
          locale: const Locale('es'),
          supportedLocales: const [Locale('es')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          builder: (context, child) => BlocBuilder<ConnectivityCubit, bool>(
            builder: (context, online) => Column(children: [
              if (!online) const SafeArea(bottom: false, child: OfflineBanner()),
              // Con el aviso visible, el área segura superior ya la ocupa el aviso: se evita duplicarla en la pantalla de abajo.
              Expanded(child: MediaQuery.removePadding(context: context, removeTop: !online, child: child ?? const SizedBox.shrink())),
            ]),
          ),
        ),
      ),
    );
  }
}
