using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Application.Common.Options;
using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using Cinerama.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cinerama.Application.Seating;

/// <summary>
/// Bloqueo y liberación de asientos. SQL Server es la fuente de verdad (ADR-004): las reglas se validan en el
/// dominio y la integridad la garantizan <c>RowVersion</c> y la transacción. Nunca se usa un lock distribuido.
/// </summary>
public sealed class SeatLockService(
    IAppDbContext db,
    IValidator<SeatSelectionRequest> validator,
    ISeatNotifier notifier,
    IOptions<SeatingOptions> seating,
    IOptions<CinemaOptions> cinema,
    TimeProvider time,
    ILogger<SeatLockService> logger)
{
    /// <summary>Un reintento como máximo: lo justifica un único caso, descrito en <see cref="TryLockAsync"/>.</summary>
    private const int MaxLockAttempts = 2;

    /// <summary>
    /// Bloquea los asientos, todo o nada (RN-21). Si dos personas piden el mismo asiento a la vez, una recibe 200 y la
    /// otra 409: ambas leen el asiento con la misma <c>RowVersion</c> y SQL Server solo deja actualizarlo a la primera.
    /// </summary>
    public async Task<LockResultDto> LockAsync(int showtimeId, int userId, SeatSelectionRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var options = seating.Value;
        var seatIds = request.SeatIds.Distinct().Order().ToArray();
        if (seatIds.Length > options.MaxSeatsPerUser)
            throw LimitExceeded(options.MaxSeatsPerUser);

        await ShowtimeSalesGuard.EnsureOpenAsync(db, showtimeId, Now(), cinema.Value.ResolveTimeZone(), ct);

        for (var attempt = 1; ; attempt++)
        {
            var result = await TryLockAsync(showtimeId, userId, seatIds, canRetry: attempt < MaxLockAttempts, ct);
            if (result is not null)
                return result;
        }
    }

    /// <returns>El resultado, o <c>null</c> si hay que reintentar (solo cuando <paramref name="canRetry"/>).</returns>
    /// <remarks>
    /// El reintento existe por un caso concreto: el proceso de limpieza libera un bloqueo vencido justo mientras otra
    /// persona lo está tomando. La liberación cambia la <c>RowVersion</c> y la escritura de esa persona no encuentra la
    /// fila, aunque el asiento haya quedado libre. Al recargar no hay ningún conflicto real (nadie más lo tiene), así
    /// que se repite una vez en lugar de devolver un 409 engañoso. Si al recargar sí hay un dueño, el 409 es verdadero.
    /// </remarks>
    private async Task<LockResultDto?> TryLockAsync(int showtimeId, int userId, int[] seatIds, bool canRetry, CancellationToken ct)
    {
        var options = seating.Value;
        var now = Now();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // RN-20 es un límite entre varias solicitudes del mismo usuario. Se serializan las solicitudes de un mismo
        // usuario sobre una misma función con un lock de aplicación de SQL Server (dura lo que la transacción);
        // sin él, dos solicitudes simultáneas verían el conteo antes de la otra y ambas pasarían el límite.
        await AcquireUserLockAsync(showtimeId, userId, ct);

        // Se cargan en un orden fijo (por SeatId): dos solicitudes con asientos en común actualizan en el mismo
        // orden y no se bloquean mutuamente (sin interbloqueos).
        var showSeats = await db.ShowSeats.Include(ss => ss.Seat)
            .Where(ss => ss.ShowtimeId == showtimeId && seatIds.Contains(ss.SeatId))
            .OrderBy(ss => ss.SeatId)
            .ToListAsync(ct);

        if (showSeats.Count != seatIds.Length)
            throw ShowtimeSalesGuard.MissingSeats(seatIds, showSeats.Select(ss => ss.SeatId));

        var heldElsewhere = await db.ShowSeats.CountAsync(ss =>
            ss.ShowtimeId == showtimeId && ss.Status == ShowSeatStatus.Locked && ss.LockedByUserId == userId
            && ss.LockedUntil > now && !seatIds.Contains(ss.SeatId), ct);
        if (heldElsewhere + seatIds.Length > options.MaxSeatsPerUser)
            throw LimitExceeded(options.MaxSeatsPerUser);

        var changed = new List<ShowSeat>();
        var unavailable = new List<ShowSeat>();
        foreach (var showSeat in showSeats)
        {
            switch (showSeat.TryLock(userId, now, options.LockDuration))
            {
                case LockOutcome.Locked: changed.Add(showSeat); break;
                case LockOutcome.Unavailable: unavailable.Add(showSeat); break;
            }
        }

        if (unavailable.Count > 0)
            throw SeatsUnavailable(unavailable.Select(ss => (ss.SeatId, ss.Seat.Label)));

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Otra solicitud (o la limpieza) modificó alguno de estos asientos entre nuestra lectura y nuestra escritura.
            await tx.RollbackAsync(ct);

            var conflicts = await CurrentConflictsAsync(showtimeId, userId, seatIds, ct);
            if (conflicts.Count == 0 && canRetry)
                return null;

            throw SeatsUnavailable(conflicts.Count > 0 ? conflicts : await AllOf(showtimeId, seatIds, ct));
        }

        await NotifyAsync(showtimeId, changed.Select(ss => new SeatChange(ss.SeatId, ShowSeatStatus.Locked)), ct);

        var seatsDto = showSeats.Select(ss => SeatMapService.ToState(ss, userId, now)).ToList();
        return new LockResultDto(showtimeId, showSeats.Min(ss => ss.LockedUntil!.Value), seatsDto);
    }

    /// <summary>
    /// Libera los asientos propios (RN-22). Si alguno está bloqueado por otra persona o vendido, no se libera ninguno y
    /// la respuesta es 404, igual que si no existiera. Liberar un asiento que ya está libre no es un error.
    /// </summary>
    public async Task ReleaseAsync(int showtimeId, int userId, SeatSelectionRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var seatIds = request.SeatIds.Distinct().Order().ToArray();
        var now = Now();

        if (!await db.Showtimes.AnyAsync(s => s.Id == showtimeId, ct))
            throw new NotFoundException("showtime_not_found", "La función no existe.");

        var showSeats = await db.ShowSeats.Include(ss => ss.Seat)
            .Where(ss => ss.ShowtimeId == showtimeId && seatIds.Contains(ss.SeatId))
            .OrderBy(ss => ss.SeatId)
            .ToListAsync(ct);

        if (showSeats.Count != seatIds.Length)
            throw ShowtimeSalesGuard.MissingSeats(seatIds, showSeats.Select(ss => ss.SeatId));

        var released = new List<ShowSeat>();
        foreach (var showSeat in showSeats)
        {
            switch (showSeat.TryRelease(userId, now))
            {
                case ReleaseOutcome.Released: released.Add(showSeat); break;
                case ReleaseOutcome.NotYours:
                    throw new NotFoundException("lock_not_found", "No tienes un bloqueo vigente sobre esos asientos.");
            }
        }

        if (released.Count == 0)
            return;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // La RowVersion no coincide: la limpieza liberó mi bloqueo vencido a la vez, o alguien lo tomó. Nunca se
            // libera el bloqueo de otra persona. Si ya no hay ningún asiento ajeno, el objetivo (que queden libres) se
            // cumplió; si alguno es de otra persona, es el mismo 404 que si lo hubiera intentado desde el principio.
            var others = await CurrentConflictsAsync(showtimeId, userId, seatIds, ct);
            if (others.Count > 0)
                throw new NotFoundException("lock_not_found", "No tienes un bloqueo vigente sobre esos asientos.");
            return;
        }

        await NotifyAsync(showtimeId, released.Select(ss => new SeatChange(ss.SeatId, ShowSeatStatus.Available)), ct);
    }

    private async Task AcquireUserLockAsync(int showtimeId, int userId, CancellationToken ct)
    {
        if (!await db.TryAcquireTransactionLockAsync($"cinerama:seatlock:{showtimeId}:{userId}", TimeSpan.FromSeconds(10), ct))
            throw new ConflictException("concurrency_conflict", "El sistema está ocupado con otra solicitud tuya. Vuelve a intentarlo.");
    }

    /// <summary>
    /// Estado real después de perder una carrera: los asientos pedidos que hoy tiene otra persona (con un bloqueo
    /// vigente) o que están vendidos. Vacío significa que ninguno tiene dueño: nadie "ganó" nada.
    /// </summary>
    private async Task<List<(int SeatId, string Label)>> CurrentConflictsAsync(int showtimeId, int userId, int[] seatIds, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var now = Now();

        var current = await db.ShowSeats.AsNoTracking().Include(ss => ss.Seat)
            .Where(ss => ss.ShowtimeId == showtimeId && seatIds.Contains(ss.SeatId))
            .ToListAsync(ct);

        return current.Where(ss => !ss.IsHeldBy(userId, now) && ss.StatusAt(now) != ShowSeatStatus.Available)
            .Select(ss => (ss.SeatId, ss.Seat.Label)).ToList();
    }

    private async Task<List<(int SeatId, string Label)>> AllOf(int showtimeId, int[] seatIds, CancellationToken ct) =>
        (await db.ShowSeats.AsNoTracking().Include(ss => ss.Seat)
            .Where(ss => ss.ShowtimeId == showtimeId && seatIds.Contains(ss.SeatId)).ToListAsync(ct))
        .Select(ss => (ss.SeatId, ss.Seat.Label)).ToList();

    private static ConflictException SeatsUnavailable(IEnumerable<(int SeatId, string Label)> seats) =>
        ShowtimeSalesGuard.Conflict("seat_unavailable", "Uno o más asientos no están disponibles.", seats);

    private static DomainRuleException LimitExceeded(int max) =>
        new("lock_limit_exceeded", $"Puedes seleccionar hasta {max} asientos por función.");

    /// <summary>La notificación ocurre después del commit y nunca revierte ni falla la operación (ISeatNotifier).</summary>
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
}
