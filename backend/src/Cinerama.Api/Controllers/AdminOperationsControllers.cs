using Cinerama.Api.Auth;
using Cinerama.Api.Configuration;
using Cinerama.Application.Admin;
using Cinerama.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cinerama.Api.Controllers;

/// <summary>Salas y su distribución de asientos (RF-ROOM-01, RF-ROOM-02, RN-10, RN-11).</summary>
[ApiController]
[Route("api/v1/cinema-rooms")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class RoomsAdminController(RoomAdminService rooms) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminRoomDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminRoomDto>>> List(int? cinemaId, CancellationToken ct) => Ok(await rooms.ListAsync(cinemaId, ct));

    [HttpPost]
    [ProducesResponseType<AdminRoomDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminRoomDto>> Create(CreateRoomRequest request, CancellationToken ct)
    {
        var room = await rooms.CreateAsync(request, ct);
        return Created($"/api/v1/cinema-rooms/{room.Id}/seats", room);
    }

    /// <summary>Nombre y estado. Desactivar una sala con funciones futuras da 409 <c>room_has_future_showtimes</c>.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<AdminRoomDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminRoomDto>> Update(int id, UpdateRoomRequest request, CancellationToken ct) =>
        await rooms.UpdateAsync(id, request, ct);

    [HttpGet("{id:int}/seats")]
    [ProducesResponseType<RoomSeatsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomSeatsDto>> Seats(int id, CancellationToken ct) => await rooms.GetSeatsAsync(id, ct);

    /// <summary>Redefine la distribución. 409 <c>room_has_future_showtimes</c> si la sala tiene funciones futuras (RN-11).</summary>
    [HttpPut("{id:int}/layout")]
    [ProducesResponseType<RoomSeatsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoomSeatsDto>> Layout(int id, RoomLayoutRequest request, CancellationToken ct) =>
        await rooms.UpdateLayoutAsync(id, request, ct);
}

/// <summary>Programación y cancelación de funciones (CU-03, RF-SHOW-02, RF-SHOW-03). La lectura pública sigue en <c>GET /showtimes</c>.</summary>
[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class ShowtimesAdminController(ShowtimeAdminService showtimes) : ControllerBase
{
    /// <summary>Todas las funciones, también las pasadas y las canceladas. <c>from</c> y <c>to</c> son fechas locales del cine.</summary>
    [HttpGet("api/v1/admin/showtimes")]
    [ProducesResponseType<PagedResult<AdminShowtimeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<AdminShowtimeDto>>> List(
        int? movieId, int? cinemaId, int? roomId, string? status, DateOnly? from, DateOnly? to, bool? upcomingOnly,
        int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        await showtimes.ListAsync(movieId, cinemaId, roomId, status, from, to, upcomingOnly, page, pageSize, ct);

    [HttpGet("api/v1/admin/showtimes/{id:int}")]
    [ProducesResponseType<AdminShowtimeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminShowtimeDto>> Get(int id, CancellationToken ct) => await showtimes.GetAsync(id, ct);

    [HttpPost("api/v1/showtimes")]
    [ProducesResponseType<AdminShowtimeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AdminShowtimeDto>> Create(UpsertShowtimeRequest request, CancellationToken ct)
    {
        var showtime = await showtimes.CreateAsync(request, ct);
        return Created($"/api/v1/showtimes/{showtime.Id}", showtime);
    }

    /// <summary>Solo si la función no empezó y no tiene reservas.</summary>
    [HttpPut("api/v1/showtimes/{id:int}")]
    [ProducesResponseType<AdminShowtimeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AdminShowtimeDto>> Update(int id, UpsertShowtimeRequest request, CancellationToken ct) =>
        await showtimes.UpdateAsync(id, request, ct);

    /// <summary>Cancela la función, sus reservas confirmadas y libera sus bloqueos (RN-17).</summary>
    [HttpPost("api/v1/showtimes/{id:int}/cancel")]
    [ProducesResponseType<AdminShowtimeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AdminShowtimeDto>> Cancel(int id, CancellationToken ct) => await showtimes.CancelAsync(id, ct);
}

/// <summary>Reservas de todos los clientes (RF-BOOK-05). Cancelar sigue siendo <c>POST /bookings/{id}/cancel</c>.</summary>
[ApiController]
[Route("api/v1/admin/bookings")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class BookingsAdminController(BookingAdminService bookings) : ControllerBase
{
    /// <param name="from">Fecha local de la función (inclusive).</param>
    /// <param name="to">Fecha local de la función (inclusive).</param>
    /// <param name="search">Código de reserva, nombre o email del cliente.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<AdminBookingListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<AdminBookingListItemDto>>> List(
        string? status, int? movieId, int? cinemaId, DateOnly? from, DateOnly? to, string? search,
        int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        await bookings.ListAsync(status, movieId, cinemaId, from, to, search, page, pageSize, ct);
}
