using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Application.Common.Options;
using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using Cinerama.Domain.Exceptions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cinerama.Application.Admin;

/// <summary>
/// Programación de funciones (CU-03, RF-SHOW-02, RF-SHOW-03). Crear una función genera, en la misma transacción, un asiento de función
/// <c>Available</c> por cada asiento activo de la sala (RN-15), y comprueba que no se solape con otra de la misma sala (RN-13). Dos
/// administradores que programen a la vez en la misma sala se serializan con un candado de aplicación sobre la sala: solo se confirma una.
/// </summary>
public sealed class ShowtimeAdminService(
    IAppDbContext db, IValidator<UpsertShowtimeRequest> validator, ShowtimeCanceller canceller,
    IOptions<CinemaOptions> cinema, TimeProvider time)
{
    public async Task<PagedResult<AdminShowtimeDto>> ListAsync(
        int? movieId, int? cinemaId, int? roomId, string? status, DateOnly? from, DateOnly? to, bool? upcomingOnly, int page, int pageSize, CancellationToken ct)
    {
        Paging.Validate(page, pageSize);
        var now = time.UtcNowMs();
        var query = db.Showtimes.AsNoTracking();

        if (movieId is not null) query = query.Where(s => s.MovieId == movieId);
        if (roomId is not null) query = query.Where(s => s.CinemaRoomId == roomId);
        if (cinemaId is not null) query = query.Where(s => s.CinemaRoom.CinemaId == cinemaId);
        if (upcomingOnly == true) query = query.Where(s => s.StartsAt > now);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ShowtimeStatus>(status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                throw new ValidationException([new ValidationFailure("status", "El estado debe ser scheduled o cancelled.")]);

            query = query.Where(s => s.Status == parsed);
        }

        // "desde" y "hasta" son fechas locales del cine (RN-38); "hasta" incluye todo ese día.
        if (from is not null)
            query = query.Where(s => s.StartsAt >= LocalStart(from.Value));
        if (to is not null)
            query = query.Where(s => s.StartsAt < LocalStart(to.Value.AddDays(1)));

        var total = await query.CountAsync(ct);
        var items = await Project(query.OrderByDescending(s => s.StartsAt).ThenByDescending(s => s.Id).Skip((page - 1) * pageSize).Take(pageSize), now).ToListAsync(ct);
        return new PagedResult<AdminShowtimeDto>(items, page, pageSize, total);
    }

    public async Task<AdminShowtimeDto> GetAsync(int id, CancellationToken ct) =>
        await Project(db.Showtimes.AsNoTracking().Where(s => s.Id == id), time.UtcNowMs()).FirstOrDefaultAsync(ct) ?? throw NotFound();

    public async Task<AdminShowtimeDto> CreateAsync(UpsertShowtimeRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var starts = Normalize(request.StartsAt);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.TryAcquireTransactionLockAsync(RoomAdminService.LockKey(request.CinemaRoomId), TimeSpan.FromSeconds(10), ct))
            throw new ConflictException("concurrency_conflict", "La sala está ocupada con otro cambio. Vuelve a intentarlo.");

        var (movie, room) = await LoadAndCheckAsync(request, starts, exceptShowtimeId: null, ct);
        var showtime = new Showtime
        {
            MovieId = movie.Id, CinemaRoomId = room.Id, StartsAt = starts, EndsAt = EndOf(starts, movie.DurationMinutes),
            Price = request.Price, Status = ShowtimeStatus.Scheduled, CreatedAt = time.UtcNowMs(),
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync(ct);
        await GenerateSeatsAsync(showtime.Id, room.Id, ct);
        await tx.CommitAsync(ct);

        return await GetAsync(showtime.Id, ct);
    }

    /// <summary>
    /// Cambia película, sala, hora o precio de una función que aún no empezó y no tiene ninguna reserva (confirmada o cancelada: las canceladas
    /// también cuelgan de sus asientos). Si cambia de sala se vuelven a generar sus asientos.
    /// </summary>
    public async Task<AdminShowtimeDto> UpdateAsync(int id, UpsertShowtimeRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var starts = Normalize(request.StartsAt);
        var current = await db.Showtimes.AsNoTracking().Where(s => s.Id == id).Select(s => new { s.CinemaRoomId }).FirstOrDefaultAsync(ct) ?? throw NotFound();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Se toma el candado de la sala nueva y, si cambia, también el de la anterior (siempre en el mismo orden para no provocar un interbloqueo).
        foreach (var roomId in new[] { current.CinemaRoomId, request.CinemaRoomId }.Distinct().Order())
            if (!await db.TryAcquireTransactionLockAsync(RoomAdminService.LockKey(roomId), TimeSpan.FromSeconds(10), ct))
                throw new ConflictException("concurrency_conflict", "La sala está ocupada con otro cambio. Vuelve a intentarlo.");

        var showtime = await db.Showtimes.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw NotFound();
        if (showtime.Status == ShowtimeStatus.Cancelled)
            throw new DomainRuleException("showtime_cancelled", "La función está cancelada y no se puede editar.");

        if (showtime.StartsAt <= time.UtcNowMs())
            throw new DomainRuleException("showtime_already_started", "La función ya empezó y no se puede editar.");

        if (await db.Bookings.AsNoTracking().AnyAsync(b => b.ShowtimeId == id, ct))
            throw new ConflictException("showtime_has_bookings", "La función tiene reservas: cancélala en lugar de editarla.");

        var (movie, room) = await LoadAndCheckAsync(request, starts, id, ct);
        var roomChanged = showtime.CinemaRoomId != room.Id;

        showtime.MovieId = movie.Id;
        showtime.CinemaRoomId = room.Id;
        showtime.StartsAt = starts;
        showtime.EndsAt = EndOf(starts, movie.DurationMinutes);
        showtime.Price = request.Price;
        await db.SaveChangesAsync(ct);

        if (roomChanged)
        {
            await db.ShowSeats.Where(ss => ss.ShowtimeId == id).ExecuteDeleteAsync(ct);
            await GenerateSeatsAsync(id, room.Id, ct);
        }

        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>RN-17: cancela la función antes de su inicio. Se cancelan sus reservas confirmadas, se liberan sus bloqueos y se avisa a los clientes.</summary>
    public async Task<AdminShowtimeDto> CancelAsync(int id, CancellationToken ct)
    {
        var showtime = await db.Showtimes.AsNoTracking().Where(s => s.Id == id).Select(s => new { s.Status, s.StartsAt }).FirstOrDefaultAsync(ct) ?? throw NotFound();
        if (showtime.Status == ShowtimeStatus.Cancelled)
            throw new DomainRuleException("showtime_cancelled", "La función ya estaba cancelada.");

        if (showtime.StartsAt <= time.UtcNowMs())
            throw new DomainRuleException("showtime_already_started", "La función ya empezó: no se puede cancelar.");

        await canceller.CancelAsync([id], ct);
        return await GetAsync(id, ct);
    }

    // ── Reglas compartidas ──────────────────────────────────────────────────

    private async Task<(Movie Movie, CinemaRoom Room)> LoadAndCheckAsync(UpsertShowtimeRequest request, DateTime starts, int? exceptShowtimeId, CancellationToken ct)
    {
        var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.MovieId, ct)
                    ?? throw new NotFoundException("movie_not_found", "La película no existe.");
        var room = await db.CinemaRooms.AsNoTracking().Include(r => r.Cinema).FirstOrDefaultAsync(r => r.Id == request.CinemaRoomId, ct)
                   ?? throw new NotFoundException("room_not_found", "La sala no existe.");

        if (!movie.IsActive)
            throw new DomainRuleException("movie_inactive", "La película está dada de baja: no admite funciones nuevas.");

        if (!room.IsActive || !room.Cinema.IsActive)
            throw new DomainRuleException("room_inactive", "La sala o su cine están desactivados.");

        if (starts <= time.UtcNowMs())
            throw new DomainRuleException("showtime_in_past", "La función debe programarse en el futuro.");

        // Una función no empieza antes del estreno de su película (la venta abre con la preventa o el estreno, RN-42).
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(starts, cinema.Value.ResolveTimeZone()));
        if (localDate < movie.ReleaseDate)
            throw new DomainRuleException("showtime_before_release", $"La película se estrena el {movie.ReleaseDate:yyyy-MM-dd}: no puede haber funciones antes.");

        if (!await db.Seats.AsNoTracking().AnyAsync(s => s.CinemaRoomId == room.Id && s.IsActive, ct))
            throw new DomainRuleException("room_has_no_seats", "La sala no tiene asientos: define su distribución primero.");

        var ends = EndOf(starts, movie.DurationMinutes);
        var overlaps = await db.Showtimes.AsNoTracking().AnyAsync(s =>
            s.CinemaRoomId == room.Id && s.Id != exceptShowtimeId && s.Status == ShowtimeStatus.Scheduled && s.StartsAt < ends && s.EndsAt > starts, ct);
        if (overlaps)
            throw new ConflictException("showtime_overlap", "La sala ya tiene otra función en ese horario (incluye la limpieza).");

        return (movie, room);
    }

    private Task<int> GenerateSeatsAsync(int showtimeId, int roomId, CancellationToken ct) => db.GenerateShowSeatsAsync(showtimeId, roomId, ct);

    private DateTime EndOf(DateTime starts, int movieMinutes) => starts.AddMinutes(movieMinutes + cinema.Value.CleaningMinutes);

    private static DateTime Normalize(DateTime value)
    {
        var utc = value.ToUniversalTime();
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    private DateTime LocalStart(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), cinema.Value.ResolveTimeZone());

    private IQueryable<AdminShowtimeDto> Project(IQueryable<Showtime> query, DateTime now) =>
        query.Select(s => new AdminShowtimeDto(
            s.Id, s.MovieId, s.Movie.Title, s.CinemaRoom.CinemaId, s.CinemaRoom.Cinema.Name, s.CinemaRoomId, s.CinemaRoom.Name,
            s.StartsAt, s.EndsAt, s.Price, s.Status.ToString(),
            s.ShowSeats.Count(),
            s.ShowSeats.Count(ss => ss.Status == ShowSeatStatus.Booked),
            db.Bookings.Count(b => b.ShowtimeId == s.Id && b.Status == BookingStatus.Confirmed),
            db.Bookings.Any(b => b.ShowtimeId == s.Id),
            s.StartsAt <= now));

    private static NotFoundException NotFound() => new("showtime_not_found", "La función no existe.");
}
