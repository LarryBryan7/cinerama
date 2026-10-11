using Cinerama.Application.Common;
using Cinerama.Application.Common.Errors;
using Cinerama.Domain.Entities;
using Cinerama.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.Application.Admin;

/// <summary>
/// Salas y su distribución de asientos (RF-ROOM-01, RF-ROOM-02). El nombre es único dentro de cada cine (RN-10). La distribución solo se
/// cambia si la sala no tiene funciones futuras (RN-11): cada función copia los asientos de la sala al crearse, y cambiarlos debajo
/// dejaría mapas inconsistentes. Los asientos que ya aparecen en funciones pasadas no se borran (son historial): se desactivan.
/// </summary>
public sealed class RoomAdminService(
    IAppDbContext db, IValidator<CreateRoomRequest> createValidator, IValidator<UpdateRoomRequest> updateValidator,
    IValidator<RoomLayoutRequest> layoutValidator, TimeProvider time)
{
    private const string UniqueName = "UX_CinemaRooms_Cinema_Name";

    /// <summary>El mismo candado que toma quien programa una función en la sala: o se cambia la sala o se programa, nunca las dos a la vez.</summary>
    public static string LockKey(int roomId) => $"cinerama:room:{roomId}";

    public async Task<IReadOnlyList<AdminRoomDto>> ListAsync(int? cinemaId, CancellationToken ct)
    {
        var now = time.UtcNowMs();
        var query = db.CinemaRooms.AsNoTracking();
        if (cinemaId is not null)
            query = query.Where(r => r.CinemaId == cinemaId);

        var rows = await query.OrderBy(r => r.Cinema.Name).ThenBy(r => r.Name)
            .Select(r => new
            {
                r.Id, r.CinemaId, CinemaName = r.Cinema.Name, r.Name, r.IsActive,
                Seats = r.Seats.Where(s => s.IsActive).Count(),
                MaxRow = r.Seats.Where(s => s.IsActive).Max(s => (char?)s.RowLabel),
                MaxNumber = r.Seats.Where(s => s.IsActive).Max(s => (int?)s.Number),
                Upcoming = r.Showtimes.Count(s => s.Status == ShowtimeStatus.Scheduled && s.StartsAt > now),
            })
            .ToListAsync(ct);

        return rows.Select(r => new AdminRoomDto(r.Id, r.CinemaId, r.CinemaName, r.Name, r.IsActive, r.Seats,
            r.MaxRow is { } row ? row - 'A' + 1 : 0, r.MaxNumber ?? 0, r.Upcoming)).ToList();
    }

    public async Task<AdminRoomDto> GetAsync(int id, CancellationToken ct)
    {
        var room = (await ListAsync(null, ct)).FirstOrDefault(r => r.Id == id);
        return room ?? throw NotFound();
    }

    public async Task<RoomSeatsDto> GetSeatsAsync(int id, CancellationToken ct)
    {
        var room = await db.CinemaRooms.AsNoTracking().Where(r => r.Id == id).Select(r => new { r.Id, r.Name }).FirstOrDefaultAsync(ct) ?? throw NotFound();
        var seats = await db.Seats.AsNoTracking().Where(s => s.CinemaRoomId == id && s.IsActive)
            .OrderBy(s => s.RowLabel).ThenBy(s => s.Number)
            .Select(s => new { s.Id, s.RowLabel, s.Number }).ToListAsync(ct);

        var rows = seats.Count == 0 ? 0 : seats.Max(s => s.RowLabel) - 'A' + 1;
        var positions = seats.Count == 0 ? 0 : seats.Max(s => (int)s.Number);
        var existing = seats.Select(s => (s.RowLabel, (int)s.Number)).ToHashSet();
        var empty = new List<string>();
        for (var r = 0; r < rows; r++)
            for (var n = 1; n <= positions; n++)
                if (!existing.Contains(((char)('A' + r), n)))
                    empty.Add($"{(char)('A' + r)}{n}");

        return new RoomSeatsDto(room.Id, room.Name, rows, positions, seats.Select(s => new RoomSeatDto(s.Id, s.RowLabel.ToString(), s.Number)).ToList(), empty);
    }

    public async Task<AdminRoomDto> CreateAsync(CreateRoomRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        if (!await db.Cinemas.AsNoTracking().AnyAsync(c => c.Id == request.CinemaId, ct))
            throw new NotFoundException("cinema_not_found", "El cine no existe.");

        var name = request.Name.Trim();
        await EnsureNameFreeAsync(request.CinemaId, name, null, ct);

        var room = new CinemaRoom { CinemaId = request.CinemaId, Name = name, IsActive = true };
        foreach (var (row, number) in Cells(request.Layout!))
            room.Seats.Add(new Seat { RowLabel = row, Number = (byte)number, IsActive = true });

        db.CinemaRooms.Add(room);
        await SaveAsync(ct);
        return await GetAsync(room.Id, ct);
    }

    public async Task<AdminRoomDto> UpdateAsync(int id, UpdateRoomRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var room = await db.CinemaRooms.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw NotFound();
        var name = request.Name.Trim();
        await EnsureNameFreeAsync(room.CinemaId, name, id, ct);

        if (room.IsActive && !request.IsActive && await HasFutureShowtimesAsync(id, ct))
            throw FutureShowtimes();

        room.Name = name;
        room.IsActive = request.IsActive;
        await SaveAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<RoomSeatsDto> UpdateLayoutAsync(int id, RoomLayoutRequest layout, CancellationToken ct)
    {
        await layoutValidator.ValidateAndThrowAsync(layout, ct);
        if (!await db.CinemaRooms.AsNoTracking().AnyAsync(r => r.Id == id, ct))
            throw NotFound();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.TryAcquireTransactionLockAsync(LockKey(id), TimeSpan.FromSeconds(10), ct))
            throw new ConflictException("concurrency_conflict", "La sala está ocupada con otro cambio. Vuelve a intentarlo.");

        if (await HasFutureShowtimesAsync(id, ct))
            throw FutureShowtimes();

        var seats = await db.Seats.Where(s => s.CinemaRoomId == id).ToListAsync(ct);
        var inHistory = (await db.ShowSeats.Where(ss => ss.Seat.CinemaRoomId == id).Select(ss => ss.SeatId).Distinct().ToListAsync(ct)).ToHashSet();
        var wanted = Cells(layout).ToHashSet();

        foreach (var seat in seats)
        {
            if (wanted.Remove((seat.RowLabel, seat.Number)))
                seat.IsActive = true;
            else if (inHistory.Contains(seat.Id))
                seat.IsActive = false; // aparece en funciones pasadas: se conserva como historial
            else
                db.Seats.Remove(seat);
        }

        foreach (var (row, number) in wanted)
            db.Seats.Add(new Seat { CinemaRoomId = id, RowLabel = row, Number = number, IsActive = true });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetSeatsAsync(id, ct);
    }

    private static IEnumerable<(char Row, byte Number)> Cells(RoomLayoutRequest layout)
    {
        var empty = (layout.EmptyCells ?? []).Select(c => (c[0], (byte)int.Parse(c[1..]))).ToHashSet();
        for (var r = 0; r < layout.Rows; r++)
            for (var n = 1; n <= layout.Positions; n++)
                if (!empty.Contains(((char)('A' + r), (byte)n)))
                    yield return ((char)('A' + r), (byte)n);
    }

    private async Task<bool> HasFutureShowtimesAsync(int roomId, CancellationToken ct)
    {
        var now = time.UtcNowMs();
        return await db.Showtimes.AsNoTracking().AnyAsync(s => s.CinemaRoomId == roomId && s.Status == ShowtimeStatus.Scheduled && s.StartsAt > now, ct);
    }

    private async Task EnsureNameFreeAsync(int cinemaId, string name, int? exceptId, CancellationToken ct)
    {
        if (await db.CinemaRooms.AsNoTracking().AnyAsync(r => r.CinemaId == cinemaId && r.Name == name && r.Id != exceptId, ct))
            throw new ConflictException("room_name_taken", "Ya hay una sala con ese nombre en este cine.");
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException e) when (e.Involves(UniqueName))
        {
            throw new ConflictException("room_name_taken", "Ya hay una sala con ese nombre en este cine.");
        }
    }

    private static NotFoundException NotFound() => new("room_not_found", "La sala no existe.");

    private static ConflictException FutureShowtimes() =>
        new("room_has_future_showtimes", "La sala tiene funciones futuras programadas: cancélalas antes de cambiarla.");
}
