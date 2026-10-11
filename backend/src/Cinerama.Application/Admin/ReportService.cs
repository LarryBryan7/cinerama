using Cinerama.Application.Common;
using Cinerama.Application.Common.Options;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cinerama.Application.Admin;

/// <summary>
/// Reportes del dashboard (RF-REP-01 a RF-REP-04). La definición de "vendida", "ingreso" y "ocupación" vive en la vista
/// <c>vw_ShowtimeSales</c> y los KPIs salen de <c>sp_GetSalesSummary</c>; este servicio solo traduce fechas y agrupa. Las canceladas no suman
/// (RN-37) y una función cancelada no cuenta en la ocupación. Las fechas son del calendario local del cine y "hasta" las incluye (RN-38).
/// </summary>
public sealed class ReportService(IAppDbContext db, IOptions<CinemaOptions> cinema)
{
    public const int MaxDays = 366;

    private TimeZoneInfo Zone => cinema.Value.ResolveTimeZone();

    public async Task<SalesSummaryDto> SummaryAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (start, end, fromUtc, toUtc) = Range(from, to);
        var row = await db.GetSalesSummaryAsync(fromUtc, toUtc, ct);
        return new SalesSummaryDto(start, end, row.Revenue, row.Bookings, row.TicketsSold, row.AverageTicket, row.AverageOccupancy, row.Showtimes);
    }

    /// <summary>Un registro por cada día del rango (también los días sin funciones, con ceros): así el gráfico no salta fechas.</summary>
    public async Task<IReadOnlyList<DailySalesDto>> DailyAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (start, end, fromUtc, toUtc) = Range(from, to);
        var rows = await InPeriod(fromUtc, toUtc)
            .Select(s => new { s.StartsAt, s.Revenue, s.Bookings, s.SeatsSold, s.Capacity })
            .ToListAsync(ct);

        var zone = Zone;
        var byDay = rows.GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.StartsAt, DateTimeKind.Utc), zone))).ToDictionary(g => g.Key);

        var result = new List<DailySalesDto>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (!byDay.TryGetValue(day, out var g))
            {
                result.Add(new DailySalesDto(day, 0, 0, 0, 0, 0));
                continue;
            }

            var capacity = g.Sum(r => r.Capacity);
            result.Add(new DailySalesDto(day, g.Sum(r => r.Revenue), g.Sum(r => r.Bookings), g.Sum(r => r.SeatsSold), g.Count(),
                capacity == 0 ? 0 : Math.Round((decimal)g.Sum(r => r.SeatsSold) / capacity, 6)));
        }

        return result;
    }

    public async Task<IReadOnlyList<MovieSalesDto>> ByMovieAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (_, _, fromUtc, toUtc) = Range(from, to);
        var rows = await InPeriod(fromUtc, toUtc)
            .GroupBy(s => new { s.MovieId, s.MovieTitle })
            .Select(g => new
            {
                g.Key.MovieId, g.Key.MovieTitle,
                Revenue = g.Sum(s => s.Revenue), Bookings = g.Sum(s => s.Bookings), Sold = g.Sum(s => s.SeatsSold),
                Capacity = g.Sum(s => s.Capacity), Showtimes = g.Count(),
            })
            .ToListAsync(ct);

        return rows.OrderByDescending(r => r.Revenue).ThenBy(r => r.MovieTitle)
            .Select(r => new MovieSalesDto(r.MovieId, r.MovieTitle, r.Revenue, r.Bookings, r.Sold, r.Showtimes, Ratio(r.Sold, r.Capacity))).ToList();
    }

    /// <param name="groupBy"><c>showtime</c> (una fila por función, de menos a más ocupada) o <c>room</c> (una por sala, ponderada por capacidad).</param>
    public async Task<PagedResult<OccupancyRowDto>> OccupancyAsync(DateOnly? from, DateOnly? to, string? groupBy, int page, int pageSize, CancellationToken ct)
    {
        Paging.Validate(page, pageSize);
        var (_, _, fromUtc, toUtc) = Range(from, to);
        var mode = (groupBy ?? "showtime").Trim().ToLowerInvariant();
        if (mode is not ("showtime" or "room"))
            throw new ValidationException([new ValidationFailure("groupBy", "groupBy debe ser showtime o room.")]);

        var query = InPeriod(fromUtc, toUtc);
        if (mode == "showtime")
        {
            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(s => s.Occupancy).ThenBy(s => s.StartsAt).ThenBy(s => s.ShowtimeId)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(s => new OccupancyRowDto(s.ShowtimeId, s.MovieTitle, s.CinemaName + " · " + s.RoomName, s.StartsAt, s.Capacity, s.SeatsSold, s.Occupancy, 1))
                .ToListAsync(ct);
            return new PagedResult<OccupancyRowDto>(items.Select(i => i with { StartsAt = DateTime.SpecifyKind(i.StartsAt!.Value, DateTimeKind.Utc) }).ToList(), page, pageSize, total);
        }

        var rooms = await query.GroupBy(s => new { s.CinemaRoomId, s.CinemaName, s.RoomName })
            .Select(g => new { g.Key.CinemaRoomId, g.Key.CinemaName, g.Key.RoomName, Capacity = g.Sum(s => s.Capacity), Sold = g.Sum(s => s.SeatsSold), Showtimes = g.Count() })
            .ToListAsync(ct);
        var ordered = rooms.Select(r => new OccupancyRowDto(r.CinemaRoomId, r.CinemaName + " · " + r.RoomName, null, null, r.Capacity, r.Sold, Ratio(r.Sold, r.Capacity), r.Showtimes))
            .OrderBy(r => r.Occupancy).ThenBy(r => r.Title).ToList();
        return new PagedResult<OccupancyRowDto>(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, ordered.Count);
    }

    private IQueryable<Domain.Entities.ShowtimeSales> InPeriod(DateTime fromUtc, DateTime toUtc) =>
        db.ShowtimeSales.AsNoTracking().Where(s => s.Status == "Scheduled" && s.StartsAt >= fromUtc && s.StartsAt < toUtc);

    private static decimal Ratio(int sold, int capacity) => capacity == 0 ? 0 : Math.Round((decimal)sold / capacity, 6);

    private (DateOnly From, DateOnly To, DateTime FromUtc, DateTime ToUtc) Range(DateOnly? from, DateOnly? to)
    {
        var failures = new List<ValidationFailure>();
        if (from is null) failures.Add(new ValidationFailure("from", "Indica la fecha de inicio (yyyy-MM-dd)."));
        if (to is null) failures.Add(new ValidationFailure("to", "Indica la fecha de fin (yyyy-MM-dd)."));
        if (failures.Count == 0 && from > to) failures.Add(new ValidationFailure("from", "La fecha de inicio no puede ser posterior a la de fin."));
        if (failures.Count == 0 && to!.Value.DayNumber - from!.Value.DayNumber + 1 > MaxDays)
            failures.Add(new ValidationFailure("to", $"El rango admite hasta {MaxDays} días."));
        if (failures.Count > 0)
            throw new ValidationException(failures);

        var zone = Zone;
        DateTime Start(DateOnly d) => TimeZoneInfo.ConvertTimeToUtc(d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
        return (from!.Value, to!.Value, Start(from.Value), Start(to.Value.AddDays(1)));
    }
}
