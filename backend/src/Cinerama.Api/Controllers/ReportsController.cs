using Cinerama.Api.Auth;
using Cinerama.Api.Configuration;
using Cinerama.Application.Admin;
using Cinerama.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cinerama.Api.Controllers;

/// <summary>
/// Reportes del dashboard (RF-REP-01 a RF-REP-04). <c>from</c> y <c>to</c> son fechas locales del cine (<c>yyyy-MM-dd</c>, ambas incluidas) y el
/// periodo se mide por la fecha de la función. Rango máximo de 366 días.
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<SalesSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SalesSummaryDto>> Summary(DateOnly? from, DateOnly? to, CancellationToken ct) => await reports.SummaryAsync(from, to, ct);

    /// <summary>Un registro por día del rango, también los días sin funciones.</summary>
    [HttpGet("sales-daily")]
    [ProducesResponseType<IReadOnlyList<DailySalesDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<DailySalesDto>>> Daily(DateOnly? from, DateOnly? to, CancellationToken ct) => Ok(await reports.DailyAsync(from, to, ct));

    [HttpGet("sales-by-movie")]
    [ProducesResponseType<IReadOnlyList<MovieSalesDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<MovieSalesDto>>> ByMovie(DateOnly? from, DateOnly? to, CancellationToken ct) => Ok(await reports.ByMovieAsync(from, to, ct));

    /// <param name="groupBy"><c>showtime</c> (por defecto) o <c>room</c>.</param>
    [HttpGet("occupancy")]
    [ProducesResponseType<PagedResult<OccupancyRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<OccupancyRowDto>>> Occupancy(
        DateOnly? from, DateOnly? to, string? groupBy, int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        await reports.OccupancyAsync(from, to, groupBy, page, pageSize, ct);
}
