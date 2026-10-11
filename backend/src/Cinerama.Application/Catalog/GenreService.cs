using Cinerama.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.Application.Catalog;

public sealed class GenreService(IAppDbContext db)
{
    public async Task<IReadOnlyList<GenreDto>> ListAsync(CancellationToken ct) =>
        await db.Genres.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GenreDto(g.Id, g.Name))
            .ToListAsync(ct);
}
