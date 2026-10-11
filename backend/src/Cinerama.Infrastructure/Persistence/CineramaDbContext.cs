using System.Text.RegularExpressions;
using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.Infrastructure.Persistence;

public partial class CineramaDbContext(DbContextOptions<CineramaDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<Cinema> Cinemas => Set<Cinema>();
    public DbSet<CinemaRoom> CinemaRooms => Set<CinemaRoom>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Showtime> Showtimes => Set<Showtime>();
    public DbSet<ShowSeat> ShowSeats => Set<ShowSeat>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSeat> BookingSeats => Set<BookingSeat>();
    public DbSet<ShowtimeSales> ShowtimeSales => Set<ShowtimeSales>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HaveColumnType("datetime2(3)");
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>().HaveColumnType("datetime2(3)");
        configurationBuilder.Properties<decimal>().HavePrecision(10, 2);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CineramaDbContext).Assembly);

    // Números de error de SQL Server: violación de índice único (2601) y de restricción UNIQUE (2627).
    private static readonly int[] UniqueViolationNumbers = [2601, 2627];

    /// <summary>
    /// Traduce las violaciones de índices únicos a <see cref="UniqueConstraintViolationException"/>, para que los
    /// servicios decidan qué significa cada índice sin conocer SqlClient. Cualquier otro error pasa tal cual.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && UniqueViolationNumbers.Contains(sql.Number))
        {
            throw new UniqueConstraintViolationException(ExtractIndexName(sql.Message), ex.Message, sql);
        }
    }

    public async Task<SalesSummaryRow> GetSalesSummaryAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        // ToListAsync y no SingleAsync: un EXEC no es SQL "componible" y EF no puede envolverlo en una subconsulta.
        var rows = await Database.SqlQuery<SalesSummaryRow>($"EXEC dbo.sp_GetSalesSummary @FromUtc = {fromUtc}, @ToUtc = {toUtc}").ToListAsync(cancellationToken);
        return rows.Single();
    }

    public Task<int> GenerateShowSeatsAsync(int showtimeId, int cinemaRoomId, CancellationToken cancellationToken = default) =>
        ShowSeatGenerator.GenerateAsync(this, showtimeId, cinemaRoomId, cancellationToken);

    public async Task<bool> TryAcquireTransactionLockAsync(string resource, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var timeoutMs = (int)timeout.TotalMilliseconds;

        // sp_getapplock devuelve 0 o 1 si obtuvo el lock y un valor negativo si venció el plazo, se canceló o hubo interbloqueo.
        // ToListAsync y no SingleAsync: un bloque con EXEC no es SQL "componible" y EF no puede envolverlo en una subconsulta.
        var result = await Database.SqlQuery<int>($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {timeoutMs};
            SELECT @result AS Value;
            """).ToListAsync(cancellationToken);

        return result.Single() >= 0;
    }

    [GeneratedRegex("(?:unique index|constraint) '([^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex IndexNamePattern();

    private static string? ExtractIndexName(string message) => IndexNamePattern().Match(message) is { Success: true } m ? m.Groups[1].Value : null;
}
