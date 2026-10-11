using Cinerama.Domain.Enums;

namespace Cinerama.Domain.Entities;

public enum LockOutcome
{
    /// <summary>El asiento quedó bloqueado por el usuario.</summary>
    Locked,

    /// <summary>Ya estaba bloqueado por el mismo usuario y vigente: no cambia nada, ni siquiera el vencimiento (RN-19).</summary>
    AlreadyMine,

    /// <summary>Vendido, o bloqueado y vigente a nombre de otra persona (RN-18).</summary>
    Unavailable,
}

public enum ReleaseOutcome
{
    /// <summary>El bloqueo del usuario se liberó.</summary>
    Released,

    /// <summary>No había nada que liberar (ya estaba disponible o su bloqueo ajeno había vencido): la operación es idempotente.</summary>
    NothingToRelease,

    /// <summary>Está bloqueado por otra persona o vendido: no se puede liberar (RN-22).</summary>
    NotYours,
}

/// <summary>
/// Estado de un asiento para una función. Es la entidad central del dominio: su <see cref="RowVersion"/>
/// es la primera barrera contra la doble venta (ADR-004).
/// </summary>
/// <remarks>
/// Las reglas reciben la hora actual como parámetro (nunca leen el reloj): así se prueban sin esperar 5 minutos.
/// Un bloqueo cuyo vencimiento ya pasó equivale a un asiento disponible (RN-23), aunque el proceso de limpieza
/// todavía no lo haya liberado en la base.
/// </remarks>
public class ShowSeat
{
    public long Id { get; set; }
    public int ShowtimeId { get; set; }
    public Showtime Showtime { get; set; } = null!;
    public int SeatId { get; set; }
    public Seat Seat { get; set; } = null!;

    public ShowSeatStatus Status { get; set; } = ShowSeatStatus.Available;
    public int? LockedByUserId { get; set; }
    public DateTime? LockedUntil { get; set; }

    /// <summary>Token de concurrencia: SQL Server lo incrementa en cada UPDATE.</summary>
    public byte[] RowVersion { get; set; } = null!;

    /// <summary>Estado que ven los clientes: un bloqueo vencido se muestra como disponible (RN-23).</summary>
    public static ShowSeatStatus EffectiveStatus(ShowSeatStatus status, DateTime? lockedUntil, DateTime nowUtc) =>
        status == ShowSeatStatus.Locked && IsExpired(lockedUntil, nowUtc) ? ShowSeatStatus.Available : status;

    /// <summary>Un bloqueo vence cuando <paramref name="nowUtc"/> alcanza su <c>LockedUntil</c>.</summary>
    public static bool IsExpired(DateTime? lockedUntil, DateTime nowUtc) => lockedUntil is null || lockedUntil <= nowUtc;

    public ShowSeatStatus StatusAt(DateTime nowUtc) => EffectiveStatus(Status, LockedUntil, nowUtc);

    /// <summary>¿Lo tiene bloqueado, y vigente, este usuario?</summary>
    public bool IsHeldBy(int userId, DateTime nowUtc) =>
        Status == ShowSeatStatus.Locked && LockedByUserId == userId && !IsExpired(LockedUntil, nowUtc);

    /// <summary>RN-18, RN-19, RN-23.</summary>
    public LockOutcome TryLock(int userId, DateTime nowUtc, TimeSpan duration)
    {
        if (Status == ShowSeatStatus.Booked)
            return LockOutcome.Unavailable;

        if (Status == ShowSeatStatus.Locked && !IsExpired(LockedUntil, nowUtc))
            return LockedByUserId == userId ? LockOutcome.AlreadyMine : LockOutcome.Unavailable;

        // Disponible, o con un bloqueo vencido que cualquiera puede tomar.
        Status = ShowSeatStatus.Locked;
        LockedByUserId = userId;
        LockedUntil = nowUtc + duration;
        return LockOutcome.Locked;
    }

    /// <summary>RN-22: solo se liberan los propios.</summary>
    public ReleaseOutcome TryRelease(int userId, DateTime nowUtc)
    {
        switch (Status)
        {
            case ShowSeatStatus.Available:
                return ReleaseOutcome.NothingToRelease;

            case ShowSeatStatus.Locked when LockedByUserId == userId:
                MakeAvailable();
                return ReleaseOutcome.Released;

            case ShowSeatStatus.Locked when IsExpired(LockedUntil, nowUtc):
                return ReleaseOutcome.NothingToRelease; // bloqueo ajeno ya vencido: es como si estuviera libre

            default:
                return ReleaseOutcome.NotYours;
        }
    }

    /// <summary>RN-24: solo se confirma un asiento bloqueado por el mismo usuario y con el bloqueo vigente.</summary>
    public bool TryConfirm(int userId, DateTime nowUtc)
    {
        if (!IsHeldBy(userId, nowUtc))
            return false;

        Status = ShowSeatStatus.Booked;
        LockedByUserId = null;
        LockedUntil = null;
        return true;
    }

    /// <summary>Al cancelar la reserva, el asiento vuelve a estar disponible (RN-30).</summary>
    public void ReleaseFromBooking() => MakeAvailable();

    private void MakeAvailable()
    {
        Status = ShowSeatStatus.Available;
        LockedByUserId = null;
        LockedUntil = null;
    }
}
