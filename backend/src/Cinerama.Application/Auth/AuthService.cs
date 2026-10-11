using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.Application.Auth;

/// <summary>
/// Registro, login, renovación con rotación y cierre de sesión (ADR-007). La identidad se toma siempre de
/// quien llama (token o credenciales), nunca de parámetros del request (RN-34).
/// </summary>
public sealed class AuthService(
    IAppDbContext db,
    ITokenService tokens,
    IPasswordHasher<User> hasher,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    TimeProvider time)
{
    // Se verifica contra este hash cuando el email no existe, para que la respuesta tarde lo mismo y no
    // revele qué emails están registrados.
    private static readonly Lazy<string> DummyHash =
        new(() => new PasswordHasher<User>().HashPassword(new User(), "dummy-password-1"));

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        await registerValidator.ValidateAndThrowAsync(request, ct);

        var email = User.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("email_already_registered", "Ya existe una cuenta con ese email.");

        // RN-04: el registro público siempre crea un Customer. El rol no es una entrada del request.
        var user = new User
        {
            Email = email,
            FullName = request.FullName.Trim(),
            RoleId = Roles.CustomerId,
            IsActive = true,
            CreatedAt = Now(),
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);

        // Si dos registros con el mismo email compiten, el índice único gana: el perdedor recibe el mismo 409.
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException ex) when (ex.Involves("UX_Users_Email"))
        {
            throw new ConflictException("email_already_registered", "Ya existe una cuenta con ese email.");
        }

        return await IssueTokensAsync(user, Roles.Customer, Guid.NewGuid(), ct);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        await loginValidator.ValidateAndThrowAsync(request, ct);

        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Email == email, ct);

        var verification = hasher.VerifyHashedPassword(user ?? new User(), user?.PasswordHash ?? DummyHash.Value, request.Password);

        // Mismo error para email inexistente, contraseña incorrecta y cuenta inactiva (RN-03): no se revela cuál fue.
        if (user is null || verification == PasswordVerificationResult.Failed || !user.IsActive)
            throw new AuthenticationFailedException("invalid_credentials", "Email o contraseña incorrectos.");

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(ct);
        }

        return await IssueTokensAsync(user, user.Role.Name, Guid.NewGuid(), ct);
    }

    public async Task<AuthResult> RefreshAsync(string? presentedToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(presentedToken))
            throw InvalidRefreshToken();

        var hash = tokens.HashRefreshToken(presentedToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u.Role)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null)
            throw InvalidRefreshToken();

        var now = Now();

        if (stored.RevokedAt is not null)
        {
            // Revocado por rotación → alguien presenta una copia vieja: se asume robo y se revoca toda la sesión.
            // Revocado por logout o por desactivación (sin ReplacedById) → simplemente ya no sirve.
            if (stored.ReplacedById is not null)
            {
                await RevokeFamilyAsync(stored.FamilyId, now, ct);
                throw new AuthenticationFailedException("refresh_token_reused",
                    "Por seguridad cerramos tu sesión. Vuelve a iniciar sesión.");
            }
            throw InvalidRefreshToken();
        }

        if (stored.ExpiresAt <= now)
            throw InvalidRefreshToken();

        if (!stored.User.IsActive)
            throw new AuthenticationFailedException("account_disabled", "La cuenta está desactivada.");

        // La rotación es atómica: se inserta el token nuevo y se "reclama" el viejo con un UPDATE condicional
        // (RevokedAt IS NULL). Si dos solicitudes presentan el mismo token a la vez, solo una reclama la fila;
        // la otra obtiene 0 filas y se trata como reutilización. Los clientes lo evitan con un único refresh en curso.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var generated = tokens.CreateRefreshToken();
        var next = new RefreshToken
        {
            UserId = stored.UserId,
            TokenHash = generated.Hash,
            FamilyId = stored.FamilyId,
            CreatedAt = now,
            ExpiresAt = generated.ExpiresAt,
        };
        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync(ct);

        var claimed = await db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.ReplacedById, next.Id), ct);

        if (claimed == 0)
        {
            await tx.RollbackAsync(ct);
            await RevokeFamilyAsync(stored.FamilyId, now, ct);
            throw new AuthenticationFailedException("refresh_token_reused",
                "Por seguridad cerramos tu sesión. Vuelve a iniciar sesión.");
        }

        await tx.CommitAsync(ct);

        var access = tokens.CreateAccessToken(stored.User.Id, stored.User.Email, stored.User.Role.Name);
        return new AuthResult(access.Value, access.ExpiresAt, generated.Value, generated.ExpiresAt, ToDto(stored.User, stored.User.Role.Name));
    }

    /// <summary>
    /// Con token: revoca esa sesión (toda su familia), pero solo si el token es del usuario autenticado; el token de
    /// otra persona se ignora sin revelarlo. Sin token: revoca todas las sesiones del usuario, que es lo más seguro.
    /// </summary>
    public async Task LogoutAsync(int userId, string? presentedToken, CancellationToken ct)
    {
        var now = Now();

        if (!string.IsNullOrWhiteSpace(presentedToken))
        {
            var hash = tokens.HashRefreshToken(presentedToken);
            var familyId = await db.RefreshTokens
                .Where(t => t.TokenHash == hash && t.UserId == userId)
                .Select(t => (Guid?)t.FamilyId)
                .FirstOrDefaultAsync(ct);

            if (familyId is not null)
                await RevokeFamilyAsync(familyId.Value, now, ct);
            return;
        }

        await RevokeAllForUserAsync(userId, now, ct);
    }

    private async Task<AuthResult> IssueTokensAsync(User user, string roleName, Guid familyId, CancellationToken ct)
    {
        var access = tokens.CreateAccessToken(user.Id, user.Email, roleName);
        var generated = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = generated.Hash,
            FamilyId = familyId,
            CreatedAt = Now(),
            ExpiresAt = generated.ExpiresAt,
        });
        await db.SaveChangesAsync(ct);

        return new AuthResult(access.Value, access.ExpiresAt, generated.Value, generated.ExpiresAt, ToDto(user, roleName));
    }

    private Task<int> RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

    private Task<int> RevokeAllForUserAsync(int userId, DateTime now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

    private DateTime Now() => time.UtcNowMs();

    private static AuthenticationFailedException InvalidRefreshToken() =>
        new("invalid_refresh_token", "La sesión no es válida. Inicia sesión nuevamente.");

    private static UserDto ToDto(User user, string roleName) => new(user.Id, user.FullName, user.Email, roleName);
}
