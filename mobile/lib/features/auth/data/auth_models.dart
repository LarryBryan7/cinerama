import 'package:equatable/equatable.dart';

class AppUser extends Equatable {
  const AppUser({required this.id, required this.fullName, required this.email, required this.role});

  final int id;
  final String fullName;
  final String email;
  final String role;

  bool get isAdmin => role == 'Admin';

  factory AppUser.fromJson(Map<String, dynamic> json) => AppUser(
        id: json['id'] as int,
        fullName: json['fullName'] as String,
        email: json['email'] as String,
        role: json['role'] as String,
      );

  Map<String, dynamic> toJson() => {'id': id, 'fullName': fullName, 'email': email, 'role': role};

  AppUser copyWith({String? fullName}) => AppUser(id: id, fullName: fullName ?? this.fullName, email: email, role: role);

  @override
  List<Object?> get props => [id, fullName, email, role];
}

/// Respuesta de login / registro / refresh. En la app móvil el refresh token siempre viaja en el cuerpo
/// (el panel web, en cambio, lo recibe en una cookie HttpOnly; ADR-006).
class AuthSession {
  const AuthSession({
    required this.accessToken,
    required this.accessTokenExpiresAt,
    required this.refreshToken,
    required this.user,
  });

  final String accessToken;
  final DateTime accessTokenExpiresAt;
  final String refreshToken;
  final AppUser user;

  factory AuthSession.fromJson(Map<String, dynamic> json) => AuthSession(
        accessToken: json['accessToken'] as String,
        accessTokenExpiresAt: DateTime.parse(json['accessTokenExpiresAt'] as String).toUtc(),
        refreshToken: json['refreshToken'] as String,
        user: AppUser.fromJson(json['user'] as Map<String, dynamic>),
      );
}
