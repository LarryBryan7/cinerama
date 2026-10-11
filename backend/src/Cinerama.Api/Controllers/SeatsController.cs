using Cinerama.Api.Auth;
using Cinerama.Api.Configuration;
using Cinerama.Application.Common;
using Cinerama.Application.Seating;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Cinerama.Api.Controllers;

/// <summary>
/// Mapa, bloqueo y liberación de asientos de una función. La identidad sale siempre del token (RN-34): nadie puede
/// bloquear o liberar "en nombre de" otra persona.
/// </summary>
[ApiController]
[Route("api/v1/showtimes/{showtimeId:int}/seats")]
public sealed class SeatsController(SeatMapService map, SeatLockService locks) : ControllerBase
{
    /// <summary>Estado de cada asiento. Un bloqueo vencido se muestra como disponible (RN-23); <c>isMine</c> marca los propios.</summary>
    [HttpGet]
    [ProducesResponseType<SeatMapDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SeatMapDto>> GetMap(int showtimeId, CancellationToken ct) =>
        await map.GetAsync(showtimeId, User.GetUserId(), ct);

    /// <summary>
    /// Bloquea asientos durante 5 minutos, todo o nada. Si dos personas piden el mismo asiento a la vez, una recibe
    /// 200 y la otra 409 con la lista de asientos en conflicto.
    /// </summary>
    [HttpPost("lock")]
    [EnableRateLimiting(RateLimitSettings.SeatsPolicy)]
    [ProducesResponseType<LockResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LockResultDto>> Lock(int showtimeId, SeatSelectionRequest request, CancellationToken ct) =>
        await locks.LockAsync(showtimeId, User.GetUserId(), request, ct);

    /// <summary>Libera asientos propios. Si alguno es de otra persona, no se libera ninguno (404).</summary>
    [HttpPost("release")]
    [EnableRateLimiting(RateLimitSettings.SeatsPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Release(int showtimeId, SeatSelectionRequest request, CancellationToken ct)
    {
        await locks.ReleaseAsync(showtimeId, User.GetUserId(), request, ct);
        return NoContent();
    }

    /// <summary>Hasta 3 grupos de <c>count</c> asientos contiguos y céntricos, sin asientos en común. Lista vacía si no hay lugar.</summary>
    [HttpGet("recommendations")]
    [ProducesResponseType<IReadOnlyList<SeatRecommendationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SeatRecommendationDto>>> Recommend(int showtimeId, int count = 2, CancellationToken ct = default) =>
        Ok(await map.RecommendAsync(showtimeId, User.GetUserId(), count, ct));
}
