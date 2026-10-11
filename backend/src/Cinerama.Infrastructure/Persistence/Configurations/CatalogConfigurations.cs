using Cinerama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cinerama.Infrastructure.Persistence.Configurations;

internal sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> b)
    {
        b.ToTable("Genres");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("UX_Genres_Name");
    }
}

internal sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> b)
    {
        b.ToTable("Movies", t =>
        {
            t.HasCheckConstraint("CK_Movies_Duration", "[DurationMinutes] BETWEEN 1 AND 400");
            // La preventa abre antes (o el mismo día) del estreno, nunca después.
            t.HasCheckConstraint("CK_Movies_PreSale", "[PreSaleStartsOn] IS NULL OR [PreSaleStartsOn] <= [ReleaseDate]");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Synopsis).HasMaxLength(2000);
        b.Property(x => x.Classification).HasMaxLength(20).IsRequired();
        b.Property(x => x.PosterUrl).HasMaxLength(500);
        b.Property(x => x.TrailerUrl).HasMaxLength(500);
        b.Property(x => x.PreviewVideoUrl).HasMaxLength(500);
        b.Property(x => x.ReleaseDate).HasColumnType("date");
        b.Property(x => x.PreSaleStartsOn).HasColumnType("date");

        // Cubre el listado de cartelera: filtra por activas y ordena por título.
        b.HasIndex(x => new { x.IsActive, x.Title }).HasDatabaseName("IX_Movies_IsActive_Title");

        b.HasMany(x => x.Genres).WithMany(g => g.Movies).UsingEntity<Dictionary<string, object>>(
            "MovieGenres",
            right => right.HasOne<Genre>().WithMany().HasForeignKey("GenreId").OnDelete(DeleteBehavior.Cascade),
            left => left.HasOne<Movie>().WithMany().HasForeignKey("MovieId").OnDelete(DeleteBehavior.Cascade),
            join =>
            {
                join.ToTable("MovieGenres");
                join.HasKey("MovieId", "GenreId");
                join.HasIndex("GenreId").HasDatabaseName("IX_MovieGenres_GenreId");
            });
    }
}

internal sealed class CinemaRoomConfiguration : IEntityTypeConfiguration<CinemaRoom>
{
    public void Configure(EntityTypeBuilder<CinemaRoom> b)
    {
        b.ToTable("CinemaRooms");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(50).IsRequired();

        // El nombre de la sala es único dentro de su cine ("Sala 1" existe en cada sede).
        b.HasIndex(x => new { x.CinemaId, x.Name }).IsUnique().HasDatabaseName("UX_CinemaRooms_Cinema_Name");
        b.HasOne(x => x.Cinema).WithMany(c => c.Rooms).HasForeignKey(x => x.CinemaId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CinemaConfiguration : IEntityTypeConfiguration<Cinema>
{
    public void Configure(EntityTypeBuilder<Cinema> b)
    {
        b.ToTable("Cinemas", t =>
        {
            t.HasCheckConstraint("CK_Cinemas_Latitude", "[Latitude] BETWEEN -90 AND 90");
            t.HasCheckConstraint("CK_Cinemas_Longitude", "[Longitude] BETWEEN -180 AND 180");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Address).HasMaxLength(200).IsRequired();
        b.Property(x => x.City).HasMaxLength(100).IsRequired();
        b.Property(x => x.Region).HasMaxLength(100).IsRequired();
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
        b.Property(x => x.LogoUrl).HasMaxLength(500);

        b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("UX_Cinemas_Name");
        // Cubre el selector de ciudades y el filtro por ciudad.
        b.HasIndex(x => new { x.IsActive, x.City }).HasDatabaseName("IX_Cinemas_IsActive_City");
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.ToTable("Reviews", t => t.HasCheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Comment).HasMaxLength(Review.MaxCommentLength);

        // RN-43: una opinión por persona y película. El índice es la barrera final si dos solicitudes compiten.
        b.HasIndex(x => new { x.MovieId, x.UserId }).IsUnique().HasDatabaseName("UX_Reviews_Movie_User");
        // Promedio, cantidad y últimas opiniones de una película salen de este índice sin tocar la tabla.
        b.HasIndex(x => new { x.MovieId, x.CreatedAt }).IsDescending(false, true).IncludeProperties(x => x.Rating).HasDatabaseName("IX_Reviews_Movie_CreatedAt");

        b.HasOne(x => x.Movie).WithMany(m => m.Reviews).HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> b)
    {
        b.ToTable("Seats", t => t.HasCheckConstraint("CK_Seats_RowLabel", "[RowLabel] BETWEEN 'A' AND 'Z'"));
        b.HasKey(x => x.Id);
        b.Property(x => x.RowLabel).HasColumnType("char(1)").IsRequired();
        b.Ignore(x => x.Label);

        b.HasIndex(x => new { x.CinemaRoomId, x.RowLabel, x.Number }).IsUnique().HasDatabaseName("UX_Seats_Room_Row_Number");
        b.HasOne(x => x.CinemaRoom).WithMany(r => r.Seats).HasForeignKey(x => x.CinemaRoomId).OnDelete(DeleteBehavior.Restrict);
    }
}
