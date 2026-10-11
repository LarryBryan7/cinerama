using Cinerama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cinerama.Application.Common;

/// <summary>
/// Acceso a datos de la aplicación. <c>DbContext</c> ya implementa unidad de trabajo y repositorio, así que
/// no se agrega una capa genérica encima (ADR-011). Compromiso aceptado: Application depende de EF Core.
/// </summary>
public interface IAppDbContext
{
    DbSet<Role> Roles { get; }
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Genre> Genres { get; }
    DbSet<Movie> Movies { get; }
    DbSet<Cinema> Cinemas { get; }
    DbSet<CinemaRoom> CinemaRooms { get; }
    DbSet<Review> Reviews { get; }
    DbSet<Seat> Seats { get; }
    DbSet<Showtime> Showtimes { get; }
    DbSet<ShowSeat> ShowSeats { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<BookingSeat> BookingSeats { get; }

    /// <summary>Vista de solo lectura <c>vw_ShowtimeSales</c> (RN-36, RN-37).</summary>
    DbSet<ShowtimeSales> ShowtimeSales { get; }

    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Toma un lock de aplicación exclusivo sobre <paramref name="resource"/> que dura lo que la transacción actual
    /// (debe haber una abierta). Serializa solicitudes que compiten por un mismo recurso lógico que no es una fila
    /// (por ejemplo, "el usuario X en la función Y"). Devuelve <c>false</c> si no se obtuvo dentro del plazo.
    /// </summary>
    /// <summary>
    /// RN-15: crea un <c>ShowSeat</c> <c>Available</c> por cada asiento activo de la sala, en una sola sentencia
    /// (<c>INSERT ... SELECT</c>). Debe ejecutarse en la misma transacción que crea la función.
    /// </summary>
    Task<int> GenerateShowSeatsAsync(int showtimeId, int cinemaRoomId, CancellationToken cancellationToken = default);

    /// <summary>Ejecuta <c>sp_GetSalesSummary</c>: los KPIs de las funciones que empiezan en [<paramref name="fromUtc"/>, <paramref name="toUtc"/>).</summary>
    Task<SalesSummaryRow> GetSalesSummaryAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    Task<bool> TryAcquireTransactionLockAsync(string resource, TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>Resultado de <c>sp_GetSalesSummary</c> (una fila). <c>AverageOccupancy</c> va de 0 a 1.</summary>
public sealed class SalesSummaryRow
{
    // Las mismas escalas que devuelve el procedimiento: sin ellas EF Core avisa al arrancar de un posible truncamiento.
    [Precision(18, 2)] public decimal Revenue { get; set; }
    public int Bookings { get; set; }
    public int TicketsSold { get; set; }
    [Precision(18, 2)] public decimal AverageTicket { get; set; }
    [Precision(9, 6)] public decimal AverageOccupancy { get; set; }
    public int Showtimes { get; set; }
}
