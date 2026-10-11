using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using Cinerama.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.Application.Seating;

public sealed record OpenShowtime(decimal Price, DateTime StartsAt);

internal static class ShowtimeSalesGuard
{
    /// <summary>
    /// RN-16: la venta (bloquear y confirmar) cierra al inicio de la función. Una función cancelada no vende.
    /// RN-42: tampoco vende una película cuya preventa (o estreno) todavía no abrió.
    /// </summary>
    public static async Task<OpenShowtime> EnsureOpenAsync(IAppDbContext db, int showtimeId, DateTime nowUtc, TimeZoneInfo cinemaZone, CancellationToken ct)
    {
        var showtime = await db.Showtimes.AsNoTracking()
            .Where(s => s.Id == showtimeId)
            .Select(s => new { s.Status, s.StartsAt, s.Price, s.Movie.ReleaseDate, s.Movie.PreSaleStartsOn, CinemaActive = s.CinemaRoom.Cinema.IsActive })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("showtime_not_found", "La función no existe.");

        if (showtime.Status == ShowtimeStatus.Cancelled)
            throw new DomainRuleException("showtime_cancelled", "La función fue cancelada.");

        if (!showtime.CinemaActive)
            throw new DomainRuleException("cinema_closed", "El cine de esta función no está disponible.");

        if (showtime.StartsAt <= nowUtc)
            throw new DomainRuleException("sales_closed", "La venta para esta función ya cerró.");

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, cinemaZone));
        if (!Movie.IsSalesOpen(showtime.ReleaseDate, showtime.PreSaleStartsOn, today))
            throw new DomainRuleException("sales_not_open", "La venta de entradas para esta película todavía no está habilitada.");

        return new OpenShowtime(showtime.Price, showtime.StartsAt);
    }

    /// <summary>Los asientos pedidos deben pertenecer a la función. Se informan los que no (404, sin revelar otros datos).</summary>
    public static NotFoundException MissingSeats(IEnumerable<int> requested, IEnumerable<int> found) =>
        new("seat_not_found", $"Estos asientos no existen en la función: {string.Join(", ", requested.Except(found))}.");

    public static ConflictException Conflict(string code, string message, IEnumerable<(int SeatId, string Label)> seats) =>
        new(code, message, new Dictionary<string, object?>
        {
            ["conflicts"] = seats.Select(s => new { seatId = s.SeatId, label = s.Label }).ToList(),
        });
}
