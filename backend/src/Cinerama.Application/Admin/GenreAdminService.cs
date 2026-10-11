using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.Application.Admin;

/// <summary>Géneros del catálogo (RF-CAT-04). El nombre es único sin distinguir mayúsculas; un género con películas no se borra.</summary>
public sealed class GenreAdminService(IAppDbContext db, IValidator<GenreRequest> validator)
{
    private const string UniqueName = "UX_Genres_Name";

    public async Task<IReadOnlyList<AdminGenreDto>> ListAsync(CancellationToken ct) =>
        await db.Genres.AsNoTracking().OrderBy(g => g.Name).Select(g => new AdminGenreDto(g.Id, g.Name, g.Movies.Count)).ToListAsync(ct);

    public async Task<AdminGenreDto> CreateAsync(GenreRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var name = request.Name.Trim();
        await EnsureNameFreeAsync(name, exceptId: null, ct);

        var genre = new Genre { Name = name };
        db.Genres.Add(genre);
        await SaveAsync(ct);
        return new AdminGenreDto(genre.Id, genre.Name, 0);
    }

    public async Task<AdminGenreDto> UpdateAsync(int id, GenreRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var genre = await db.Genres.FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw NotFound();
        var name = request.Name.Trim();
        await EnsureNameFreeAsync(name, id, ct);

        genre.Name = name;
        await SaveAsync(ct);
        return new AdminGenreDto(genre.Id, genre.Name, await db.Genres.Where(g => g.Id == id).Select(g => g.Movies.Count).SingleAsync(ct));
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var genre = await db.Genres.FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw NotFound();
        var inUse = await db.Movies.AnyAsync(m => m.Genres.Any(g => g.Id == id), ct);
        if (inUse)
            throw new ConflictException("genre_in_use", "El género tiene películas: quítalo de ellas antes de borrarlo.");

        db.Genres.Remove(genre);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureNameFreeAsync(string name, int? exceptId, CancellationToken ct)
    {
        if (await db.Genres.AsNoTracking().AnyAsync(g => g.Name == name && g.Id != exceptId, ct))
            throw new ConflictException("genre_name_taken", "Ya existe un género con ese nombre.");
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException e) when (e.Involves(UniqueName))
        {
            throw new ConflictException("genre_name_taken", "Ya existe un género con ese nombre.");
        }
    }

    private static NotFoundException NotFound() => new("genre_not_found", "El género no existe.");
}
