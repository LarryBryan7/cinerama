using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Application.Common.Options;
using Cinerama.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cinerama.Application.Catalog;

public sealed class ShowtimeService(IAppDbContext db, IOptions<CinemaOptions> cinema, TimeProvider time)
{
    /// <summary>
    /// Funciones programadas y futuras (público). <paramref name="date"/> es una fecha local del cine: se
    /// traduce al rango UTC correspondiente (RN-38).
    /// </summary>
    public async Task<PagedResult<ShowtimeDto>> ListAsync(
        int? movieId, DateOnly? date, int? cinemaId, int page, int pageSize, CancellationToken ct)
    {
        Paging.Validate(page, pageSize);
        var now = time.GetUtcNow().UtcDateTime;

        var query = db.Showtimes.AsNoTracking()
            .Where(s => s.Status == ShowtimeStatus.Scheduled && s.StartsAt > now && s.CinemaRoom.Cinema.IsActive);

        if (movieId is not null)
            query = query.Where(s => s.MovieId == movieId);

        if (cinemaId is not null)
            query = query.Where(s => s.CinemaRoom.CinemaId == cinemaId);

        if (date is not null)
        {
            var (fromUtc, toUtc) = LocalDayToUtcRange(date.Value);
            query = query.Where(s => s.StartsAt >= fromUtc && s.StartsAt < toUtc);
        }

        var total = await query.CountAsync(ct);

        // Se ordena y se pagina sobre las entidades y recién después se proyecta: EF no puede traducir un
        // OrderBy sobre un DTO que ya incluye subconsultas (los Count de asientos).
        var items = await Project(
                query.OrderBy(s => s.StartsAt).ThenBy(s => s.Id).Skip((page - 1) * pageSize).Take(pageSize), now)
            .ToListAsync(ct);

        return new PagedResult<ShowtimeDto>(items, page, pageSize, total);
    }

    public async Task<ShowtimeDto> GetAsync(int id, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var showtime = await Project(db.Showtimes.AsNoTracking().Where(s => s.Id == id), now).FirstOrDefaultAsync(ct);
        return showtime ?? throw new NotFoundException("showtime_not_found", "La función no existe.");
    }

    private (DateTime FromUtc, DateTime ToUtc) LocalDayToUtcRange(DateOnly date)
    {
        var zone = cinema.Value.ResolveTimeZone();
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var from = TimeZoneInfo.ConvertTimeToUtc(start, zone);
        var to = TimeZoneInfo.ConvertTimeToUtc(start.AddDays(1), zone);
        return (from, to);
    }

    // Un asiento con bloqueo vencido cuenta como disponible aunque el proceso de limpieza aún no lo haya liberado (RN-23).
    private static IQueryable<ShowtimeDto> Project(IQueryable<Domain.Entities.Showtime> query, DateTime now) =>
        query.Select(s => new ShowtimeDto(
            s.Id, s.MovieId, s.Movie.Title, s.CinemaRoomId, s.CinemaRoom.Name,
            s.StartsAt, s.EndsAt, s.Price, s.Status.ToString(),
            s.ShowSeats.Count(),
            s.ShowSeats.Count(ss => ss.Status == ShowSeatStatus.Available
                || (ss.Status == ShowSeatStatus.Locked && ss.LockedUntil < now)),
            s.CinemaRoom.CinemaId, s.CinemaRoom.Cinema.Name));
}
