using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Application.Common.Options;
using Cinerama.Application.Seating;
using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using Cinerama.Domain.Exceptions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cinerama.Application.Bookings;

/// <summary>
/// Confirmación, consulta y cancelación de reservas. SQL Server es la fuente de verdad (ADR-004): un asiento no
/// puede quedar en dos reservas activas por tres barreras independientes: <c>RowVersion</c> de <c>ShowSeats</c>,
/// la transacción y el índice único filtrado de <c>BookingSeats</c>.
/// </summary>
public sealed class BookingService(
    IAppDbContext db,
    IValidator<CreateBookingRequest> validator,
    ITicketSigner ticketSigner,
    ISeatNotifier notifier,
    IOptions<SeatingOptions> seating,
    IOptions<CinemaOptions> cinema,
    TimeProvider time,
    ILogger<BookingService> logger)
{
    private const string ActiveBookingSeatIndex = "UX_BookingSeats_ShowSeat_Active";
    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24); // RN-27

    // ── Confirmar ───────────────────────────────────────────────────────────

    /// <summary>
    /// Confirma los asientos que el usuario tiene bloqueados (RN-24). Con <paramref name="idempotencyKey"/>, repetir la
    /// solicitud devuelve la reserva original y no crea otra (RN-27): el cliente puede reintentar sin miedo si perdió
    /// la respuesta o tocó el botón dos veces.
    /// </summary>
    public async Task<BookingResult> ConfirmAsync(int userId, CreateBookingRequest request, string? idempotencyKey, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key is { Length: > CreateBookingRequestValidator.MaxIdempotencyKeyLength })
            throw new ValidationException([new ValidationFailure("Idempotency-Key",
                $"La clave de idempotencia admite hasta {CreateBookingRequestValidator.MaxIdempotencyKeyLength} caracteres.")]);

        var seatIds = request.SeatIds.Distinct().Order().ToArray();
        if (seatIds.Length > seating.Value.MaxSeatsPerUser)
            throw new DomainRuleException("seat_limit_exceeded", $"Una reserva admite hasta {seating.Value.MaxSeatsPerUser} asientos.");

        if (key is not null && await FindByKeyAsync(userId, key, ct) is { } existing)
            return Replay(existing, request.ShowtimeId, seatIds);

        try
        {
            return await CreateAsync(userId, request.ShowtimeId, seatIds, key, ct);
        }
        catch (Exception ex) when (key is not null && ex is ConflictException or DbUpdateException)
        {
            // Dos solicitudes con la misma clave compitieron (doble toque): una creó la reserva y la otra perdió la
            // carrera por los asientos o por el índice de la clave. La perdedora devuelve la reserva de la ganadora.
            db.ChangeTracker.Clear();
            if (await FindByKeyAsync(userId, key, ct) is { } winner)
                return Replay(winner, request.ShowtimeId, seatIds);
            throw;
        }
    }

    private async Task<BookingResult> CreateAsync(int userId, int showtimeId, int[] seatIds, string? key, CancellationToken ct)
    {
        var now = Now();
        var showtime = await ShowtimeSalesGuard.EnsureOpenAsync(db, showtimeId, now, cinema.Value.ResolveTimeZone(), ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Mismo orden fijo que el bloqueo: sin interbloqueos entre solicitudes con asientos en común.
        var showSeats = await db.ShowSeats.Include(ss => ss.Seat)
            .Where(ss => ss.ShowtimeId == showtimeId && seatIds.Contains(ss.SeatId))
            .OrderBy(ss => ss.SeatId)
            .ToListAsync(ct);

        if (showSeats.Count != seatIds.Length)
            throw ShowtimeSalesGuard.MissingSeats(seatIds, showSeats.Select(ss => ss.SeatId));

        // RN-24: todos los asientos deben estar bloqueados por este usuario y con el bloqueo vigente.
        var notHeld = showSeats.Where(ss => !ss.TryConfirm(userId, now)).ToList();
        if (notHeld.Count > 0)
            throw ShowtimeSalesGuard.Conflict("lock_expired", "Tu selección expiró o ya no es tuya. Vuelve a elegir tus asientos.",
                notHeld.Select(ss => (ss.SeatId, ss.Seat.Label)));

        var booking = new Booking
        {
            Code = await NewUniqueCodeAsync(ct),
            UserId = userId,
            ShowtimeId = showtimeId,
            Status = BookingStatus.Confirmed,
            IdempotencyKey = key,
            CreatedAt = now,
            TotalAmount = showtime.Price * showSeats.Count, // RN-25: el precio vigente se copia a cada asiento
            Seats = showSeats.Select(ss => new BookingSeat { ShowSeat = ss, Price = showtime.Price }).ToList(),
        };
        db.Bookings.Add(booking);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Primera barrera: otra solicitud modificó estos asientos (RowVersion) entre nuestra lectura y nuestra escritura.
            await tx.RollbackAsync(ct);
            throw await RaceLostAsync(showtimeId, userId, seatIds, ct);
        }
        catch (UniqueConstraintViolationException ex) when (ex.Involves(ActiveBookingSeatIndex))
        {
            // Tercera barrera: el índice único filtrado impidió una segunda reserva activa del mismo asiento.
            await tx.RollbackAsync(ct);
            throw await RaceLostAsync(showtimeId, userId, seatIds, ct);
        }

        await NotifyAsync(showtimeId, showSeats.Select(ss => new SeatChange(ss.SeatId, ShowSeatStatus.Booked)), ct);

        var dto = await LoadAsync(b => b.Id == booking.Id, ct)
                  ?? throw new InvalidOperationException("La reserva recién creada no se encontró.");
        return new BookingResult(dto, Created: true);
    }

    private async Task<ConflictException> RaceLostAsync(int showtimeId, int userId, int[] seatIds, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var now = Now();

        var current = await db.ShowSeats.AsNoTracking().Include(ss => ss.Seat)
            .Where(ss => ss.ShowtimeId == showtimeId && seatIds.Contains(ss.SeatId))
            .ToListAsync(ct);

        var conflicts = current.Where(ss => !ss.IsHeldBy(userId, now)).ToList();
        return ShowtimeSalesGuard.Conflict("seat_unavailable",
            "Esos asientos cambiaron mientras confirmabas. Revisa tu selección.",
            (conflicts.Count > 0 ? conflicts : current).Select(ss => (ss.SeatId, ss.Seat.Label)));
    }

    private async Task<BookingDto?> FindByKeyAsync(int userId, string key, CancellationToken ct) =>
        await LoadAsync(b => b.UserId == userId && b.IdempotencyKey == key, ct);

    /// <summary>Devuelve la reserva original si la repetición es la misma solicitud; si no, la clave se reutilizó con otro contenido.</summary>
    private BookingResult Replay(BookingDto existing, int showtimeId, int[] seatIds)
    {
        var sameRequest = existing.Showtime.Id == showtimeId
                          && existing.Seats.Select(s => s.SeatId).Order().SequenceEqual(seatIds);
        if (!sameRequest)
            throw new DomainRuleException("idempotency_key_reused", "Esa clave de idempotencia ya se usó con una solicitud distinta.");

        if (Now() - existing.CreatedAt > IdempotencyWindow)
            throw new ConflictException("idempotency_key_expired", "Esa clave de idempotencia ya venció. Usa una clave nueva.");

        return new BookingResult(existing, Created: false);
    }

    private async Task<string> NewUniqueCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = BookingCodeGenerator.Generate();
            if (!await db.Bookings.AnyAsync(b => b.Code == code, ct))
                return code;
        }

        throw new InvalidOperationException("No se pudo generar un código de reserva único.");
    }

    // ── Consultar ───────────────────────────────────────────────────────────

    /// <summary>
    /// RN-32: el cliente solo ve sus reservas. Una reserva ajena responde igual que una inexistente (404), así no se
    /// revela que existe. El administrador puede consultar cualquiera.
    /// </summary>
    public async Task<BookingDto> GetAsync(int bookingId, int userId, bool isAdmin, CancellationToken ct) =>
        await LoadAsync(b => b.Id == bookingId && (isAdmin || b.UserId == userId), ct)
        ?? throw BookingNotFound();

    public async Task<PagedResult<BookingListItemDto>> ListAsync(int userId, string? status, int page, int pageSize, CancellationToken ct)
    {
        Paging.Validate(page, pageSize);

        BookingStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<BookingStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                throw new ValidationException([new ValidationFailure("status", "El estado debe ser Confirmed o Cancelled.")]);
            statusFilter = parsed;
        }

        // El filtro por dueño va en la propia consulta (RN-32, RN-34): no existe ningún parámetro para listar las de otra persona.
        var query = db.Bookings.AsNoTracking().Where(b => b.UserId == userId);
        if (statusFilter is not null)
            query = query.Where(b => b.Status == statusFilter);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new
            {
                b.Id, b.Code, b.Status, b.TotalAmount, b.CreatedAt,
                MovieTitle = b.Showtime.Movie.Title,
                Room = b.Showtime.CinemaRoom.Name,
                b.Showtime.StartsAt,
                Seats = b.Seats.Select(bs => new { Row = bs.ShowSeat.Seat.RowLabel, bs.ShowSeat.Seat.Number }).ToList(),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new BookingListItemDto(
            r.Id, r.Code, r.Status.ToString(), r.MovieTitle, r.Room, r.StartsAt,
            r.Seats.OrderBy(s => s.Row).ThenBy(s => s.Number).Select(s => $"{s.Row}{s.Number}").ToList(),
            r.TotalAmount, r.CreatedAt)).ToList();

        return new PagedResult<BookingListItemDto>(items, page, pageSize, total);
    }

    // ── Cancelar ────────────────────────────────────────────────────────────

    /// <summary>
    /// Cancela la reserva y devuelve sus asientos a Available (RN-30). El cliente cancela hasta 2 horas antes; el
    /// administrador, hasta el inicio. Si dos cancelaciones llegan a la vez, la <c>RowVersion</c> de los asientos hace
    /// que solo una prospere y la otra responda que ya estaba cancelada.
    /// </summary>
    public async Task<BookingDto> CancelAsync(int bookingId, int userId, bool isAdmin, CancellationToken ct)
    {
        var booking = await db.Bookings
            .Include(b => b.Showtime)
            .Include(b => b.Seats).ThenInclude(bs => bs.ShowSeat).ThenInclude(ss => ss.Seat)
            .FirstOrDefaultAsync(b => b.Id == bookingId && (isAdmin || b.UserId == userId), ct)
            ?? throw BookingNotFound();

        if (booking.Status == BookingStatus.Cancelled)
            throw AlreadyCancelled();

        var now = Now();
        booking.Cancel(now, booking.Showtime.StartsAt, isAdmin, TimeSpan.FromHours(cinema.Value.CancellationHours));
        foreach (var bookingSeat in booking.Seats)
            bookingSeat.ShowSeat.ReleaseFromBooking();

        var changes = booking.Seats.Select(bs => new SeatChange(bs.ShowSeat.SeatId, ShowSeatStatus.Available)).ToList();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            if (await db.Bookings.AsNoTracking().AnyAsync(b => b.Id == bookingId && b.Status == BookingStatus.Cancelled, ct))
                throw AlreadyCancelled();
            throw;
        }

        await NotifyAsync(booking.ShowtimeId, changes, ct);

        return await LoadAsync(b => b.Id == bookingId, ct) ?? throw BookingNotFound();
    }

    // ── Compartido ──────────────────────────────────────────────────────────

    private async Task<BookingDto?> LoadAsync(System.Linq.Expressions.Expression<Func<Booking, bool>> predicate, CancellationToken ct)
    {
        var row = await db.Bookings.AsNoTracking()
            .Where(predicate)
            .Select(b => new
            {
                b.Id, b.Code, b.Status, b.TotalAmount, b.CreatedAt, b.CancelledAt,
                b.ShowtimeId,
                b.Showtime.MovieId,
                MovieTitle = b.Showtime.Movie.Title,
                Room = b.Showtime.CinemaRoom.Name,
                b.Showtime.StartsAt,
                Seats = b.Seats.Select(bs => new
                {
                    bs.ShowSeat.SeatId, Row = bs.ShowSeat.Seat.RowLabel, bs.ShowSeat.Seat.Number, bs.Price,
                }).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return null;

        var seats = row.Seats.OrderBy(s => s.Row).ThenBy(s => s.Number)
            .Select(s => new BookingSeatDto(s.SeatId, $"{s.Row}{s.Number}", s.Price)).ToList();

        return new BookingDto(
            row.Id, row.Code, row.Status.ToString(),
            new BookingShowtimeDto(row.ShowtimeId, row.MovieId, row.MovieTitle, row.Room, row.StartsAt),
            seats, row.TotalAmount, ticketSigner.Sign(row.Code), row.CreatedAt, row.CancelledAt);
    }

    private async Task NotifyAsync(int showtimeId, IEnumerable<SeatChange> changes, CancellationToken ct)
    {
        var list = changes.ToList();
        if (list.Count == 0)
            return;

        try
        {
            await notifier.NotifyAsync(showtimeId, list, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo notificar el cambio de asientos de la función {ShowtimeId}.", showtimeId);
        }
    }

    private DateTime Now() => time.UtcNowMs();

    private static NotFoundException BookingNotFound() => new("booking_not_found", "La reserva no existe.");

    private static ConflictException AlreadyCancelled() => new("booking_already_cancelled", "La reserva ya está cancelada.");
}
