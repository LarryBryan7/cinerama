using Cinerama.Application.Auth;
using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.Application.Users;

public sealed class UserService(
    IAppDbContext db,
    IValidator<UpdateProfileRequest> updateValidator,
    TimeProvider time)
{
    public async Task<UserDto> GetProfileAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new UserDto(u.Id, u.FullName, u.Email, u.Role.Name))
            .FirstOrDefaultAsync(ct);

        return user ?? throw new NotFoundException("user_not_found", "El usuario no existe.");
    }

    public async Task<UserDto> UpdateProfileAsync(int userId, UpdateProfileRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);

        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
                   ?? throw new NotFoundException("user_not_found", "El usuario no existe.");

        user.FullName = request.FullName.Trim();
        await db.SaveChangesAsync(ct);

        return new UserDto(user.Id, user.FullName, user.Email, user.Role.Name);
    }

    public async Task<PagedResult<AdminUserDto>> ListAsync(string? search, string? role, bool? isActive, int page, int pageSize, CancellationToken ct)
    {
        Paging.Validate(page, pageSize);

        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u => u.Email.Contains(term) || u.FullName.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var wanted = role.Trim();
            if (!string.Equals(wanted, "Admin", StringComparison.OrdinalIgnoreCase) && !string.Equals(wanted, "Customer", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException([new FluentValidation.Results.ValidationFailure("role", "El rol debe ser Admin o Customer.")]);

            query = query.Where(u => u.Role.Name == (string.Equals(wanted, "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "Customer"));
        }

        if (isActive is not null)
            query = query.Where(u => u.IsActive == isActive);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new AdminUserDto(u.Id, u.FullName, u.Email, u.Role.Name, u.IsActive, u.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<AdminUserDto>(items, page, pageSize, total);
    }

    /// <summary>
    /// Activa o desactiva una cuenta. Al desactivar se revocan todos sus refresh tokens (RN-03): el access token
    /// vigente sigue siendo válido hasta que expire (15 minutos), pero no podrá renovarse.
    /// </summary>
    public async Task<AdminUserDto> SetActiveAsync(int adminId, int userId, bool isActive, CancellationToken ct)
    {
        if (!isActive && adminId == userId)
            throw new DomainRuleException("cannot_deactivate_self", "No puedes desactivar tu propia cuenta.");

        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new NotFoundException("user_not_found", "El usuario no existe.");

        user.IsActive = isActive;
        await db.SaveChangesAsync(ct);

        if (!isActive)
        {
            var now = time.UtcNowMs();
            await db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        }

        return new AdminUserDto(user.Id, user.FullName, user.Email, user.Role.Name, user.IsActive, user.CreatedAt);
    }
}
