import 'dart:async';
import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/cine_tokens.dart';
import '../../../core/theme/figma_icons.dart';
import '../../../core/util/formatters.dart';
import '../../../core/widgets/cine_avatar.dart';
import '../../../core/widgets/common_widgets.dart';
import '../../../core/widgets/design_canvas.dart';
import '../../auth/bloc/auth_bloc.dart';
import '../bloc/reviews_cubit.dart';
import '../bloc/trailer_comments_cubit.dart';

/// Comentarios de una película (Figma: "BottonSheet - Comment"): una hoja desde abajo sobre la pantalla anterior, que se
/// ve borrosa y oscurecida detrás. Es una ruta transparente (ver `AppRouter`), así que conserva el enlace
/// `/movies/:id/reviews`, el botón atrás y el regreso del login.
///
/// Muestra los comentarios (las opiniones que traen texto), no las puntuaciones. Leer es público. La barra de abajo lleva a
/// escribir: sin sesión, al login (y al terminar vuelve aquí); con sesión, al formulario de opinión (puntuación y
/// comentario). Mientras el servidor no tenga comentarios independientes de la puntuación, escribir exige puntuar.
class ReviewsPage extends StatefulWidget {
  const ReviewsPage({super.key, this.now});

  /// Hora local para etiquetar la antigüedad de cada comentario ("2d", "1 sem"). Se inyecta en las pruebas.
  final DateTime? now;

  @override
  State<ReviewsPage> createState() => _ReviewsPageState();
}

class _ReviewsPageState extends State<ReviewsPage> {
  /// Medidas de Figma: la hoja mide 401 px de alto y 390 de ancho; sus coordenadas son relativas a su borde superior (y=335).
  static const _sheetHeight = 401.0;
  static const _listTop = 53.0;
  static const _listHeight = 292.0; // hasta la línea de y=345, donde empieza la barra de escribir

  @override
  void initState() {
    super.initState();
    unawaited(context.read<ReviewsCubit>().load());
    unawaited(context.read<TrailerCommentsCubit>().load());
  }

  Future<void> _write() async {
    final authenticated = context.read<AuthBloc>().state.status == AuthStatus.authenticated;
    if (!authenticated) {
      // Sin cuenta no se puede opinar: se pide iniciar sesión y se vuelve a esta pantalla.
      unawaited(context.push(Uri(path: '/login', queryParameters: {'from': GoRouterState.of(context).uri.toString()}).toString()));
      return;
    }
    final cubit = context.read<ReviewsCubit>();
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: CineColors.surface,
      builder: (_) => BlocProvider.value(value: cubit, child: const _ReviewSheet()),
    );
  }

  void _close() => Navigator.of(context).maybePop();

  @override
  Widget build(BuildContext context) {
    final now = widget.now ?? DateTime.now();
    final animation = ModalRoute.of(context)?.animation ?? kAlwaysCompleteAnimation;
    final slide = CurvedAnimation(parent: animation, curve: Curves.easeOutCubic, reverseCurve: Curves.easeInCubic);
    final userName = context.select((AuthBloc bloc) => bloc.state.user?.fullName) ?? '';

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Stack(children: [
        // Fondo: 70 % de #0E0E10 y desenfoque de 30 (sigma 15), que aparecen con la hoja.
        Positioned.fill(
          child: GestureDetector(
            key: const Key('comments-scrim'),
            behavior: HitTestBehavior.opaque,
            onTap: _close,
            child: AnimatedBuilder(
              animation: animation,
              builder: (context, _) {
                final t = animation.value.clamp(0.0, 1.0);
                final scrim = ColoredBox(color: CineColors.sheetScrim.withValues(alpha: CineColors.sheetScrim.a * t));
                return t < .01 ? scrim : BackdropFilter(filter: ImageFilter.blur(sigmaX: 15 * t, sigmaY: 15 * t), child: scrim);
              },
            ),
          ),
        ),
        Align(
          alignment: Alignment.bottomCenter,
          child: SlideTransition(
            position: Tween(begin: const Offset(0, 1), end: Offset.zero).animate(slide),
            child: DecoratedBox(
              decoration: const BoxDecoration(color: CineColors.surface, borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
              child: Padding(
                padding: EdgeInsets.only(bottom: MediaQuery.paddingOf(context).bottom),
                child: DesignCanvas(height: _sheetHeight, children: [
                  Positioned(left: 176, top: 11, width: 35, height: 3, child: DecoratedBox(decoration: BoxDecoration(color: CineColors.sheetHandle, borderRadius: BorderRadius.circular(30)))),
                  Positioned(left: 155, top: 25, width: 80, child: Text('Comentarios', style: cineText(13, FontWeight.w400, CineColors.white), softWrap: false)),
                  const Positioned(left: 5, top: 52, width: 378, height: .5, child: ColoredBox(color: CineColors.divider)),
                  Positioned(left: 0, top: _listTop, width: 390, height: _listHeight, child: _CommentList(now: now)),
                  const Positioned(left: -1, top: 345, width: 391, height: 1, child: ColoredBox(color: CineColors.divider)),

                  // Barra de escribir: avatar propio, campo y enviar.
                  Positioned(left: 11, top: 358, child: CineAvatar(name: userName, size: 30, strokeWidth: .5)),
                  Positioned(
                    left: 47,
                    top: 354,
                    width: 289,
                    height: 35,
                    child: Semantics(
                      button: true,
                      label: 'Escribir un comentario',
                      child: GestureDetector(
                        key: const Key('reviews-write'),
                        behavior: HitTestBehavior.opaque,
                        onTap: _write,
                        child: DecoratedBox(
                          decoration: BoxDecoration(
                            color: CineColors.surface,
                            borderRadius: BorderRadius.circular(15),
                            border: Border.all(color: CineColors.inputStroke, width: .6),
                          ),
                          child: Stack(children: [
                            Positioned(left: 11, top: 10, width: 208, child: ExcludeSemantics(child: Text('¿Qué te pareció esta película?', style: cineText(12, FontWeight.w300, CineColors.soft80), softWrap: false))),
                          ]),
                        ),
                      ),
                    ),
                  ),
                  Positioned(
                    left: 343,
                    top: 351,
                    width: 36,
                    height: 35,
                    child: Semantics(
                      button: true,
                      label: 'Enviar',
                      child: GestureDetector(
                        key: const Key('reviews-send'),
                        behavior: HitTestBehavior.opaque,
                        onTap: _write,
                        child: Stack(clipBehavior: Clip.none, children: [
                          Positioned.fill(
                            child: DecoratedBox(
                              decoration: BoxDecoration(
                                color: CineColors.send,
                                borderRadius: BorderRadius.circular(20),
                                boxShadow: const [BoxShadow(color: CineColors.sendShadow, offset: Offset(0, 4), blurRadius: 4)],
                              ),
                            ),
                          ),
                          Positioned(left: 11, top: 8, child: FigmaIcon(FigmaIconData.paperPlane)),
                        ]),
                      ),
                    ),
                  ),

                  // Arrastrar hacia abajo desde la cabecera cierra la hoja.
                  Positioned(
                    left: 0,
                    top: 0,
                    width: 390,
                    height: _listTop,
                    child: GestureDetector(
                      key: const Key('comments-header'),
                      behavior: HitTestBehavior.opaque,
                      onVerticalDragEnd: (details) {
                        if ((details.primaryVelocity ?? 0) > 300) _close();
                      },
                    ),
                  ),
                ]),
              ),
            ),
          ),
        ),
      ]),
    );
  }
}

/// Lista de comentarios: **solo los del tráiler en YouTube** (hasta 20; si el video tiene menos, los que haya). Los comentarios
/// que la base de datos trae de demostración (generados por el seed) no se muestran: no son de nadie. Cada fila mide 55 px como
/// mínimo y las separan 8 px. Sin comentarios de YouTube (sin clave, sin tráiler, comentarios cerrados o sin red) se dice que aún
/// no hay, sin errores.
class _CommentList extends StatelessWidget {
  const _CommentList({required this.now});

  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final trailer = context.watch<TrailerCommentsCubit>().state;
    // Se espera a saber si hay comentarios antes de decir que no hay ninguno (evita un parpadeo del aviso).
    if (trailer.isLoading) {
      return const Center(child: CircularProgressIndicator(color: CineColors.lime));
    }
    if (!trailer.hasComments) {
      return Center(child: Text('Aún no hay comentarios.', key: const Key('reviews-empty'), style: cineText(13, FontWeight.w300, CineColors.textTab)));
    }
    return RefreshIndicator(
      color: CineColors.lime,
      backgroundColor: CineColors.surface,
      onRefresh: () => context.read<TrailerCommentsCubit>().load(),
      child: ListView(
        padding: const EdgeInsets.fromLTRB(9, 7, 16, 8),
        children: [
          // YouTube pide dejar claro que es la fuente de lo que se muestra.
          Padding(
            key: const Key('trailer-comments-header'),
            padding: const EdgeInsets.fromLTRB(8, 2, 0, 8),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text('Comentarios del tráiler en YouTube', style: cineText(11, FontWeight.w500, CineColors.textSoft)),
              Text('Fuente: YouTube. Se muestran tal como los publicaron.', key: const Key('youtube-attribution'), style: cineText(10, FontWeight.w300, CineColors.textSoft)),
            ]),
          ),
          for (final (i, c) in trailer.comments.items.indexed) ...[
            // Los "me gusta" son reales: son los que tiene el comentario en YouTube.
            _CommentTile(
              tileKey: Key('trailer-comment-$i'), authorName: c.authorName, avatarUrl: c.authorAvatarUrl,
              text: c.text, at: c.publishedAt, likes: c.likeCount, now: now,
            ),
            const SizedBox(height: 8),
          ],
        ],
      ),
    );
  }
}

/// Un comentario (Figma: "filter-chip" de 365×55): avatar de 30 px, nombre, antigüedad, texto y "me gusta".
class _CommentTile extends StatelessWidget {
  const _CommentTile({required this.tileKey, required this.authorName, required this.text, required this.at, required this.likes, required this.now, this.avatarUrl});

  final Key tileKey;
  final String authorName;
  final String? avatarUrl;
  final String text;
  final DateTime at;
  final int likes;
  final DateTime now;

  @override
  Widget build(BuildContext context) => ConstrainedBox(
        key: tileKey,
        constraints: const BoxConstraints(minHeight: 55),
        child: Stack(clipBehavior: Clip.none, children: [
          // El texto ocupa x=45..296 de la fila (251 px) y baja según su largo.
          Padding(
            padding: const EdgeInsets.fromLTRB(45, 10, 69, 12),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
              SizedBox(
                height: 17,
                child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Flexible(child: Text(authorName, maxLines: 1, overflow: TextOverflow.ellipsis, style: cineText(13, FontWeight.w500, CineColors.commentText))),
                  const SizedBox(width: 6),
                  Padding(padding: const EdgeInsets.only(top: 2), child: Text(Formatters.commentAge(at, now), style: cineText(10, FontWeight.w300, CineColors.textSoft))),
                ]),
              ),
              Text(text, style: cineText(13, FontWeight.w300, CineColors.commentText)),
            ]),
          ),
          Positioned(left: 9, top: 10, child: CineAvatar(name: authorName, url: avatarUrl, size: 30, strokeWidth: .5)),
          Positioned(left: 339, top: 10, child: FigmaIcon(FigmaIconData.heart)),
          Positioned(left: 344, top: 27, child: Text(Formatters.compactCount(likes), softWrap: false, style: cineText(10, FontWeight.w400, CineColors.textSoft))),
        ]),
      );
}

/// Formulario de la opinión propia: puntuación de 1 a 5 y comentario opcional. Lo escrito no se pierde si el guardado falla.
class _ReviewSheet extends StatefulWidget {
  const _ReviewSheet();

  @override
  State<_ReviewSheet> createState() => _ReviewSheetState();
}

class _ReviewSheetState extends State<_ReviewSheet> {
  late final TextEditingController _comment;
  late int _rating;

  @override
  void initState() {
    super.initState();
    final mine = context.read<ReviewsCubit>().state.mine;
    _rating = mine?.rating ?? 0;
    _comment = TextEditingController(text: mine?.comment ?? '');
  }

  @override
  void dispose() {
    _comment.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_rating == 0) return;
    final ok = await context.read<ReviewsCubit>().save(rating: _rating, comment: _comment.text);
    if (ok && mounted) Navigator.pop(context);
  }

  Future<void> _delete() async {
    final ok = await context.read<ReviewsCubit>().deleteMine();
    if (ok && mounted) Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.fromLTRB(16, 16, 16, 16 + MediaQuery.viewInsetsOf(context).bottom),
      child: BlocBuilder<ReviewsCubit, ReviewsState>(
        builder: (context, state) => SingleChildScrollView(
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Text('Tu opinión', style: cineText(16, FontWeight.w600, CineColors.textTitle)),
            const SizedBox(height: 12),
            Row(mainAxisAlignment: MainAxisAlignment.center, children: [
              for (var i = 1; i <= 5; i++)
                IconButton(
                  key: Key('star-$i'),
                  tooltip: '$i ${i == 1 ? 'estrella' : 'estrellas'}',
                  iconSize: 36,
                  constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
                  onPressed: state.submitting ? null : () => setState(() => _rating = i),
                  icon: Icon(i <= _rating ? Icons.star : Icons.star_border, color: i <= _rating ? CineColors.lime : CineColors.textTab),
                ),
            ]),
            const SizedBox(height: 8),
            TextField(
              key: const Key('review-comment'),
              controller: _comment,
              maxLength: 500,
              maxLines: 4,
              minLines: 2,
              decoration: const InputDecoration(labelText: 'Comentario (opcional)'),
            ),
            if (state.saveErrorCode != null) ...[
              const SizedBox(height: 8),
              Semantics(
                liveRegion: true,
                child: Text(messageForCode(state.saveErrorCode), key: const Key('review-error'), style: TextStyle(color: Theme.of(context).colorScheme.error)),
              ),
            ],
            const SizedBox(height: 12),
            FilledButton(
              key: const Key('review-save'),
              style: FilledButton.styleFrom(backgroundColor: CineColors.lime, foregroundColor: CineColors.textOnLime),
              // Sin puntuación no se puede guardar: la puntuación es lo único obligatorio.
              onPressed: state.submitting || _rating == 0 ? null : _save,
              child: state.submitting ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2)) : const Text('Guardar'),
            ),
            if (state.mine != null)
              TextButton(
                key: const Key('review-delete'),
                onPressed: state.submitting ? null : _delete,
                child: const Text('Borrar mi opinión'),
              ),
          ]),
        ),
      ),
    );
  }
}
