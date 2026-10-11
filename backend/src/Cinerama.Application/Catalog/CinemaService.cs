using Cinerama.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.Application.Catalog;

public sealed class CinemaService(IAppDbContext db, IMediaUrls media)
{
    /// <summary>Ciudades con al menos un cine activo (para el selector de ubicación de la app).</summary>
    public async Task<IReadOnlyList<CityDto>> ListCitiesAsync(CancellationToken ct) =>
        (await db.Cinemas.AsNoTracking()
            .Where(c => c.IsActive)
            .GroupBy(c => new { c.City, c.Region })
            .Select(g => new { g.Key.City, g.Key.Region, Count = g.Count() })
            .OrderBy(c => c.City)  // se ordena antes de crear el DTO: EF no puede ordenar sobre un constructor
            .ToListAsync(ct))
        .Select(c => new CityDto(c.City, c.Region, c.Count))
        .ToList();

    public async Task<IReadOnlyList<CinemaDto>> ListAsync(string? city, CancellationToken ct)
    {
        var query = db.Cinemas.AsNoTracking().Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(city))
        {
            var cityName = city.Trim();
            query = query.Where(c => c.City == cityName);
        }

        var rows = await query.OrderBy(c => c.Name).ToListAsync(ct);
        return rows.Select(c => new CinemaDto(c.Id, c.Name, c.Address, c.City, c.Region, c.Latitude, c.Longitude, media.Absolute(c.LogoUrl))).ToList();
    }
}
