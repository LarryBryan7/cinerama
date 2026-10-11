using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Cinerama.Application.Common.Options;

namespace Cinerama.Application.Seating;

public sealed class SeatMapService(
    IAppDbContext db,
    SeatRecommendationService recommender,
    IOptions<SeatingOptions> seating,
    TimeProvider time)
{
    private sealed record SeatRow(int SeatId, char Row, byte Number, ShowSeatStatus Status, int? LockedByUserId, DateTime? LockedUntil);

    /// <summary>Estado de cada asiento de la función, en una sola consulta (sin N+1).</summary>
    public async Task<SeatMapDto> GetAsync(int showtimeId, int userId, CancellationToken ct)
    {
        var now = time.UtcNowMs();
        var showtime = await LoadShowtimeAsync(showtimeId, ct);
        var rows = await LoadRowsAsync(showtimeId, ct);

        return new SeatMapDto(showtimeId, showtime.Status.ToString(), showtime.StartsAt, showtime.Price,
            rows.Select(r => ToState(r, userId, now)).ToList());
    }

    /// <summary>RF-SEAT-04: hasta 3 grupos de <paramref name="count"/> asientos contiguos y céntricos que no se solapan entre sí.</summary>
    public async Task<IReadOnlyList<SeatRecommendationDto>> RecommendAsync(int showtimeId, int userId, int count, CancellationToken ct)
    {
        var max = seating.Value.MaxSeatsPerUser;
        if (count < 1 || count > max)
            throw new ValidationException([new ValidationFailure("count", $"La cantidad debe estar entre 1 y {max}.")]);

        var now = time.UtcNowMs();
        await LoadShowtimeAsync(showtimeId, ct);
        var rows = await LoadRowsAsync(showtimeId, ct);

        // Los asientos que ya tiene bloqueados el propio usuario no cuentan como libres para una nueva sugerencia.
        var slots = rows.Select(r => new SeatSlot(r.SeatId, r.Row, r.Number,
            ShowSeat.EffectiveStatus(r.Status, r.LockedUntil, now) == ShowSeatStatus.Available));

        return recommender.Recommend(slots, count)
            .Select(s => new SeatRecommendationDto(s.Score, s.Seats.Select(x => new RecommendedSeatDto(x.SeatId, x.Label)).ToList()))
            .ToList();
    }

    /// <summary>Estado de un asiento ya cargado como entidad (lo usa el resultado del bloqueo).</summary>
    internal static SeatStateDto ToState(ShowSeat ss, int userId, DateTime now) =>
        ToState(new SeatRow(ss.SeatId, ss.Seat.RowLabel, ss.Seat.Number, ss.Status, ss.LockedByUserId, ss.LockedUntil), userId, now);

    private static SeatStateDto ToState(SeatRow r, int userId, DateTime now)
    {
        var status = ShowSeat.EffectiveStatus(r.Status, r.LockedUntil, now);
        var mine = status == ShowSeatStatus.Locked && r.LockedByUserId == userId;
        return new SeatStateDto(r.SeatId, r.Row.ToString(), r.Number, $"{r.Row}{r.Number}", status.ToString(), mine, mine ? r.LockedUntil : null);
    }

    private async Task<(ShowtimeStatus Status, DateTime StartsAt, decimal Price)> LoadShowtimeAsync(int showtimeId, CancellationToken ct)
    {
        var s = await db.Showtimes.AsNoTracking().Where(x => x.Id == showtimeId)
            .Select(x => new { x.Status, x.StartsAt, x.Price }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("showtime_not_found", "La función no existe.");
        return (s.Status, s.StartsAt, s.Price);
    }

    private Task<List<SeatRow>> LoadRowsAsync(int showtimeId, CancellationToken ct) =>
        db.ShowSeats.AsNoTracking()
            .Where(ss => ss.ShowtimeId == showtimeId)
            .OrderBy(ss => ss.Seat.RowLabel).ThenBy(ss => ss.Seat.Number)
            .Select(ss => new SeatRow(ss.SeatId, ss.Seat.RowLabel, ss.Seat.Number, ss.Status, ss.LockedByUserId, ss.LockedUntil))
            .ToListAsync(ct);
}
