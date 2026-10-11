using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.Application.Admin;

/// <summary>
/// Cines (sedes). Las salas pertenecen a un cine y las ciudades salen de los cines activos (RN-49). Desactivar un cine con funciones futuras
/// con reservas confirmadas se rechaza; si no las tiene, se cancelan sus funciones futuras (RN-50: un cine inactivo no vende).
/// </summary>
public sealed class CinemaAdminService(
    IAppDbContext db, IValidator<UpsertCinemaRequest> validator, IMediaUrls media, ShowtimeCanceller canceller, TimeProvider time)
{
    private const string UniqueName = "UX_Cinemas_Name";

    public async Task<IReadOnlyList<AdminCinemaDto>> ListAsync(CancellationToken ct)
    {
        var now = time.UtcNowMs();
        var rows = await db.Cinemas.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id, c.Name, c.Address, c.City, c.Region, c.Latitude, c.Longitude, c.LogoUrl, c.IsActive,
                Rooms = c.Rooms.Count,
                Upcoming = c.Rooms.SelectMany(r => r.Showtimes).Count(s => s.Status == ShowtimeStatus.Scheduled && s.StartsAt > now),
            })
            .ToListAsync(ct);

        return rows.Select(r => new AdminCinemaDto(r.Id, r.Name, r.Address, r.City, r.Region, r.Latitude, r.Longitude,
            r.LogoUrl, media.Absolute(r.LogoUrl), r.IsActive, r.Rooms, r.Upcoming)).ToList();
    }

    public async Task<AdminCinemaDto> GetAsync(int id, CancellationToken ct) =>
        (await ListAsync(ct)).FirstOrDefault(c => c.Id == id) ?? throw NotFound();

    public async Task<AdminCinemaDto> CreateAsync(UpsertCinemaRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var name = request.Name.Trim();
        await EnsureNameFreeAsync(name, null, ct);

        var cinema = new Cinema();
        Apply(cinema, request);
        db.Cinemas.Add(cinema);
        await SaveAsync(ct);
        return await GetAsync(cinema.Id, ct);
    }

    public async Task<AdminCinemaDto> UpdateAsync(int id, UpsertCinemaRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var cinema = await db.Cinemas.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();
        await EnsureNameFreeAsync(request.Name.Trim(), id, ct);

        if (cinema.IsActive && !request.IsActive)
        {
            if (await canceller.HasConfirmedBookingsAsync(s => s.CinemaRoom.CinemaId == id, ct))
                throw new ConflictException("cinema_has_active_bookings", "El cine tiene funciones futuras con reservas confirmadas: cancélalas antes de desactivarlo.");

            await canceller.CancelAsync(await canceller.FutureIdsAsync(s => s.CinemaRoom.CinemaId == id, ct), ct);
        }

        Apply(cinema, request);
        await SaveAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task EnsureNameFreeAsync(string name, int? exceptId, CancellationToken ct)
    {
        if (await db.Cinemas.AsNoTracking().AnyAsync(c => c.Name == name && c.Id != exceptId, ct))
            throw new ConflictException("cinema_name_taken", "Ya existe un cine con ese nombre.");
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException e) when (e.Involves(UniqueName))
        {
            throw new ConflictException("cinema_name_taken", "Ya existe un cine con ese nombre.");
        }
    }

    private static void Apply(Cinema cinema, UpsertCinemaRequest r)
    {
        cinema.Name = r.Name.Trim();
        cinema.Address = r.Address.Trim();
        cinema.City = r.City.Trim();
        cinema.Region = r.Region.Trim();
        cinema.Latitude = Math.Round(r.Latitude, 6);
        cinema.Longitude = Math.Round(r.Longitude, 6);
        cinema.LogoUrl = MediaValues.Clean(r.LogoUrl);
        cinema.IsActive = r.IsActive;
    }

    private static NotFoundException NotFound() => new("cinema_not_found", "El cine no existe.");
}
