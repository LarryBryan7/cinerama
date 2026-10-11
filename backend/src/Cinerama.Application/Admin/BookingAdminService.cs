using Cinerama.Application.Common;
using Cinerama.Application.Common.Options;
using Cinerama.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cinerama.Application.Admin;

/// <summary>
/// Listado general de reservas para el administrador (RF-BOOK-05, RN-33). Los clientes siguen viendo solo las suyas (RN-32); este listado
/// vive bajo <c>/admin</c>, exige el rol y no comparte código de acceso con el de los clientes. Cancelar usa el endpoint de siempre
/// (<c>POST /bookings/{id}/cancel</c>), donde el administrador cancela hasta el inicio de la función (RN-30).
/// </summary>
public sealed class BookingAdminService(IAppDbContext db, IOptions<CinemaOptions> cinema)
{
    /// <param name="from">Fecha local del cine (inclusive) de la **función** (cuándo se proyecta), no de cuándo se compró.</param>
    /// <param name="to">Fecha local del cine (inclusive) de la función.</param>
    /// <param name="search">Código de la reserva, nombre o email del cliente.</param>
    public async Task<PagedResult<AdminBookingListItemDto>> ListAsync(
        string? status, int? movieId, int? cinemaId, DateOnly? from, DateOnly? to, string? search, int page, int pageSize, CancellationToken ct)
    {
        Paging.Validate(page, pageSize);
        var query = db.Bookings.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<BookingStatus>(status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                throw new ValidationException([new ValidationFailure("status", "El estado debe ser confirmed o cancelled.")]);

            query = query.Where(b => b.Status == parsed);
        }

        if (movieId is not null) query = query.Where(b => b.Showtime.MovieId == movieId);
        if (cinemaId is not null) query = query.Where(b => b.Showtime.CinemaRoom.CinemaId == cinemaId);

        var zone = cinema.Value.ResolveTimeZone();
        if (from is not null)
        {
            var fromUtc = TimeZoneInfo.ConvertTimeToUtc(from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
            query = query.Where(b => b.Showtime.StartsAt >= fromUtc);
        }

        if (to is not null)
        {
            var toUtc = TimeZoneInfo.ConvertTimeToUtc(to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
            query = query.Where(b => b.Showtime.StartsAt < toUtc);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(b => b.Code.Contains(term) || b.User.Email.Contains(term) || b.User.FullName.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new
            {
                b.Id, b.Code, b.Status, b.UserId, Customer = b.User.FullName, b.User.Email,
                b.ShowtimeId, b.Showtime.MovieId, MovieTitle = b.Showtime.Movie.Title,
                CinemaName = b.Showtime.CinemaRoom.Cinema.Name, Room = b.Showtime.CinemaRoom.Name, b.Showtime.StartsAt,
                Seats = b.Seats.Select(bs => new { Row = bs.ShowSeat.Seat.RowLabel, bs.ShowSeat.Seat.Number }).ToList(),
                b.TotalAmount, b.CreatedAt, b.CancelledAt,
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new AdminBookingListItemDto(
            r.Id, r.Code, r.Status.ToString(), r.UserId, r.Customer, r.Email, r.ShowtimeId, r.MovieId, r.MovieTitle, r.CinemaName, r.Room, r.StartsAt,
            r.Seats.OrderBy(s => s.Row).ThenBy(s => s.Number).Select(s => $"{s.Row}{s.Number}").ToList(), r.TotalAmount, r.CreatedAt, r.CancelledAt)).ToList();

        return new PagedResult<AdminBookingListItemDto>(items, page, pageSize, total);
    }
}
