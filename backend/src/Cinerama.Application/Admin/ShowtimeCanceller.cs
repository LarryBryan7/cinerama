using Cinerama.Application.Common;
using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Cinerama.Application.Common.Options;

namespace Cinerama.Application.Admin;

/// <summary>
/// Cancela funciones y deja todo coherente (RN-17): la función queda <c>Cancelled</c>, sus reservas confirmadas se cancelan, sus bloqueos
/// y asientos vendidos vuelven a <c>Available</c> y se avisa a los clientes conectados. La usan la baja de una película, la desactivación de
/// un cine o de una sala y la cancelación directa de una función, para que ninguna deje una función viva sin sentido.
/// </summary>
public sealed class ShowtimeCanceller(
    IAppDbContext db, ISeatNotifier notifier, IOptions<CinemaOptions> cinema, TimeProvider time, ILogger<ShowtimeCanceller> logger)
{
    /// <summary>Cancela las funciones indicadas que sigan programadas y no hayan empezado. Devuelve cuántas canceló.</summary>
    public async Task<int> CancelAsync(IReadOnlyCollection<int> showtimeIds, CancellationToken ct)
    {
        if (showtimeIds.Count == 0)
            return 0;

        var now = time.UtcNowMs();
        var window = TimeSpan.FromHours(cinema.Value.CancellationHours);
        var showtimes = await db.Showtimes
            .Where(s => showtimeIds.Contains(s.Id) && s.Status == ShowtimeStatus.Scheduled && s.StartsAt > now)
            .Include(s => s.ShowSeats.Where(ss => ss.Status != ShowSeatStatus.Available))
            .ToListAsync(ct);
        if (showtimes.Count == 0)
            return 0;

        var bookings = await db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed && showtimeIds.Contains(b.ShowtimeId))
            .Include(b => b.Showtime)
            .Include(b => b.Seats)
            .ToListAsync(ct);

        foreach (var booking in bookings)
            booking.Cancel(now, booking.Showtime.StartsAt, byAdmin: true, window); // los asientos vendidos se liberan abajo, junto con los bloqueos

        var changes = new Dictionary<int, List<SeatChange>>();
        foreach (var showtime in showtimes)
        {
            showtime.Status = ShowtimeStatus.Cancelled;
            var list = changes[showtime.Id] = [];
            foreach (var seat in showtime.ShowSeats)
            {
                seat.ReleaseFromBooking(); // Available, sin dueño ni vencimiento
                list.Add(new SeatChange(seat.SeatId, ShowSeatStatus.Available));
            }
        }

        await db.SaveChangesAsync(ct);

        foreach (var (showtimeId, seatChanges) in changes)
        {
            if (seatChanges.Count == 0)
                continue;

            try
            {
                await notifier.NotifyAsync(showtimeId, seatChanges, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "No se pudo notificar la cancelación de la función {ShowtimeId}.", showtimeId);
            }
        }

        return showtimes.Count;
    }

    /// <summary>Ids de las funciones futuras y programadas que cumplen <paramref name="filter"/>.</summary>
    public async Task<List<int>> FutureIdsAsync(System.Linq.Expressions.Expression<Func<Showtime, bool>> filter, CancellationToken ct)
    {
        var now = time.UtcNowMs();
        return await db.Showtimes.AsNoTracking()
            .Where(s => s.Status == ShowtimeStatus.Scheduled && s.StartsAt > now)
            .Where(filter)
            .Select(s => s.Id)
            .ToListAsync(ct);
    }

    /// <summary>¿Alguna de esas funciones futuras tiene reservas confirmadas?</summary>
    public async Task<bool> HasConfirmedBookingsAsync(System.Linq.Expressions.Expression<Func<Showtime, bool>> filter, CancellationToken ct)
    {
        var now = time.UtcNowMs();
        return await db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Confirmed && b.Showtime.Status == ShowtimeStatus.Scheduled && b.Showtime.StartsAt > now)
            .AnyAsync(b => db.Showtimes.Where(filter).Select(s => s.Id).Contains(b.ShowtimeId), ct);
    }
}
