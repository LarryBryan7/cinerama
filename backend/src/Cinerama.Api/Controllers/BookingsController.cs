using Cinerama.Api.Auth;
using Cinerama.Application.Bookings;
using Cinerama.Application.Common;
using Cinerama.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Cinerama.Api.Controllers;

[ApiController]
[Route("api/v1/bookings")]
public sealed class BookingsController(BookingService bookings) : ControllerBase
{
    /// <summary>
    /// Confirma los asientos que el usuario tiene bloqueados (pago simulado). Con <c>Idempotency-Key</c>, repetir la
    /// solicitud devuelve la reserva original (200) en lugar de crear otra (RN-27).
    /// </summary>
    [HttpPost]
    [ProducesResponseType<BookingDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BookingDto>> Create(
        CreateBookingRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        var result = await bookings.ConfirmAsync(User.GetUserId(), request, idempotencyKey, ct);

        return result.Created
            ? CreatedAtAction(nameof(Get), new { id = result.Booking.Id }, result.Booking)
            : Ok(result.Booking);
    }

    /// <summary>Un cliente recibe solo sus reservas: el filtro por dueño sale del token, no de un parámetro.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<BookingListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<BookingListItemDto>>> List(
        string? status, int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        await bookings.ListAsync(User.GetUserId(), status, page, pageSize, ct);

    /// <summary>404 si no existe <b>o no es tuya</b> (RN-32): no se revela que existe.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookingDto>> Get(int id, CancellationToken ct) =>
        await bookings.GetAsync(id, User.GetUserId(), User.IsInRole(Roles.Admin), ct);

    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BookingDto>> Cancel(int id, CancellationToken ct) =>
        await bookings.CancelAsync(id, User.GetUserId(), User.IsInRole(Roles.Admin), ct);
}
