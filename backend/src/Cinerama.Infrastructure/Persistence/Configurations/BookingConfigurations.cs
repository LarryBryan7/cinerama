using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cinerama.Infrastructure.Persistence.Configurations;

internal sealed class ShowtimeConfiguration : IEntityTypeConfiguration<Showtime>
{
    public void Configure(EntityTypeBuilder<Showtime> b)
    {
        b.ToTable("Showtimes", t =>
        {
            t.HasCheckConstraint("CK_Showtimes_EndsAfterStart", "[EndsAt] > [StartsAt]");
            t.HasCheckConstraint("CK_Showtimes_Price", "[Price] >= 0");
            t.HasCheckConstraint("CK_Showtimes_Status", "[Status] IN ('Scheduled','Cancelled')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).IsUnicode(false).IsRequired();

        // Sirve a la validación de solapamiento de funciones de una sala (RN-13).
        b.HasIndex(x => new { x.CinemaRoomId, x.StartsAt }).IncludeProperties(x => new { x.EndsAt, x.Status })
            .HasDatabaseName("IX_Showtimes_Room_StartsAt");
        b.HasIndex(x => new { x.MovieId, x.StartsAt }).HasDatabaseName("IX_Showtimes_Movie_StartsAt");

        b.HasOne(x => x.Movie).WithMany(m => m.Showtimes).HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CinemaRoom).WithMany(r => r.Showtimes).HasForeignKey(x => x.CinemaRoomId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ShowSeatConfiguration : IEntityTypeConfiguration<ShowSeat>
{
    public void Configure(EntityTypeBuilder<ShowSeat> b)
    {
        b.ToTable("ShowSeats", t =>
        {
            t.HasCheckConstraint("CK_ShowSeats_Status", "[Status] IN ('Available','Locked','Booked')");

            // Un error de código no puede dejar un asiento "bloqueado sin dueño" ni "disponible con vencimiento".
            t.HasCheckConstraint("CK_ShowSeats_LockConsistency",
                "([Status] = 'Available' AND [LockedByUserId] IS NULL AND [LockedUntil] IS NULL) " +
                "OR ([Status] = 'Locked' AND [LockedByUserId] IS NOT NULL AND [LockedUntil] IS NOT NULL) " +
                "OR ([Status] = 'Booked' AND [LockedByUserId] IS NULL AND [LockedUntil] IS NULL)");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).IsUnicode(false).IsRequired();

        // Primera barrera contra la doble venta (ADR-004): EF Core lo incluye en el WHERE de cada UPDATE.
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.ShowtimeId, x.SeatId }).IsUnique().HasDatabaseName("UX_ShowSeats_Showtime_Seat");

        // Solo indexa los bloqueos activos: la limpieza de vencidos no recorre todo el historial.
        b.HasIndex(x => x.LockedUntil).HasFilter("[Status] = 'Locked'").HasDatabaseName("IX_ShowSeats_LockedUntil_Active");

        b.HasOne(x => x.Showtime).WithMany(s => s.ShowSeats).HasForeignKey(x => x.ShowtimeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Seat).WithMany().HasForeignKey(x => x.SeatId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.LockedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> b)
    {
        b.ToTable("Bookings", t =>
        {
            t.HasCheckConstraint("CK_Bookings_Status", "[Status] IN ('Confirmed','Cancelled')");
            t.HasCheckConstraint("CK_Bookings_TotalAmount", "[TotalAmount] >= 0");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasColumnType("char(8)").IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(64);

        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_Bookings_Code");

        // RN-27: la misma clave del mismo usuario no puede crear dos reservas.
        b.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL").HasDatabaseName("UX_Bookings_User_IdempotencyKey");

        // Cubre "Mis reservas" y aplica el filtro de dueño (RN-32).
        b.HasIndex(x => new { x.UserId, x.CreatedAt }).IsDescending(false, true).HasDatabaseName("IX_Bookings_User_CreatedAt");

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Showtime).WithMany().HasForeignKey(x => x.ShowtimeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BookingSeatConfiguration : IEntityTypeConfiguration<BookingSeat>
{
    public void Configure(EntityTypeBuilder<BookingSeat> b)
    {
        b.ToTable("BookingSeats", t => t.HasCheckConstraint("CK_BookingSeats_Price", "[Price] >= 0"));
        b.HasKey(x => new { x.BookingId, x.ShowSeatId });

        // Tercera barrera contra la doble venta (RN-31, ADR-004): un asiento solo puede estar en una reserva
        // activa, aunque un error de la aplicación se saltara las otras dos.
        b.HasIndex(x => x.ShowSeatId).IsUnique()
            .HasFilter("[ReleasedAt] IS NULL").HasDatabaseName("UX_BookingSeats_ShowSeat_Active");

        b.HasOne(x => x.Booking).WithMany(bk => bk.Seats).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ShowSeat).WithMany().HasForeignKey(x => x.ShowSeatId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>La vista <c>vw_ShowtimeSales</c> como entidad sin clave: EF solo la lee, nunca la crea (la crea la migración con su .sql).</summary>
internal sealed class ShowtimeSalesConfiguration : IEntityTypeConfiguration<ShowtimeSales>
{
    public void Configure(EntityTypeBuilder<ShowtimeSales> b)
    {
        b.HasNoKey();
        b.ToView("vw_ShowtimeSales");
        b.Property(x => x.Revenue).HasColumnType("decimal(18,2)");
        b.Property(x => x.Occupancy).HasColumnType("decimal(9,6)");
        b.Property(x => x.StartsAt).HasColumnType("datetime2(3)");
    }
}
