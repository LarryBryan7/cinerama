using Cinerama.Api.Auth;
using Cinerama.Api.Configuration;
using Cinerama.Application.Admin;
using Cinerama.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cinerama.Api.Controllers;

// Los controladores públicos del catálogo llevan [AllowAnonymous] a nivel de clase, y ese atributo anula cualquier [Authorize] de una
// acción. Por eso la escritura vive en controladores aparte, con la misma ruta y otro verbo, y la lectura de administración bajo /admin.

/// <summary>Géneros: alta, edición y baja (RF-CAT-04). La lectura pública sigue en <c>GET /genres</c>.</summary>
[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class GenresAdminController(GenreAdminService genres) : ControllerBase
{
    /// <summary>Los géneros con cuántas películas tiene cada uno.</summary>
    [HttpGet("api/v1/admin/genres")]
    [ProducesResponseType<IReadOnlyList<AdminGenreDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminGenreDto>>> List(CancellationToken ct) =>
        Ok(await genres.ListAsync(ct));

    [HttpPost("api/v1/genres")]
    [ProducesResponseType<AdminGenreDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminGenreDto>> Create(GenreRequest request, CancellationToken ct)
    {
        var genre = await genres.CreateAsync(request, ct);
        return Created($"/api/v1/genres/{genre.Id}", genre);
    }

    [HttpPut("api/v1/genres/{id:int}")]
    [ProducesResponseType<AdminGenreDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminGenreDto>> Update(int id, GenreRequest request, CancellationToken ct) =>
        await genres.UpdateAsync(id, request, ct);

    /// <summary>409 <c>genre_in_use</c> si alguna película lo usa.</summary>
    [HttpDelete("api/v1/genres/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await genres.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Películas: alta, edición y baja lógica (RF-CAT-03, RN-08, RN-09). Incluye las dadas de baja.</summary>
[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class MoviesAdminController(MovieAdminService movies) : ControllerBase
{
    /// <param name="isActive">Sin valor, todas (también las dadas de baja).</param>
    /// <param name="status"><c>nowShowing</c>, <c>presale</c> o <c>comingSoon</c>.</param>
    [HttpGet("api/v1/admin/movies")]
    [ProducesResponseType<PagedResult<AdminMovieListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<AdminMovieListItemDto>>> List(
        string? search, bool? isActive, string? status, int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        await movies.ListAsync(search, isActive, status, page, pageSize, ct);

    [HttpGet("api/v1/admin/movies/{id:int}")]
    [ProducesResponseType<AdminMovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminMovieDto>> Get(int id, CancellationToken ct) => await movies.GetAsync(id, ct);

    [HttpPost("api/v1/movies")]
    [ProducesResponseType<AdminMovieDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminMovieDto>> Create(UpsertMovieRequest request, CancellationToken ct)
    {
        var movie = await movies.CreateAsync(request, ct);
        return Created($"/api/v1/movies/{movie.Id}", movie);
    }

    [HttpPut("api/v1/movies/{id:int}")]
    [ProducesResponseType<AdminMovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminMovieDto>> Update(int id, UpsertMovieRequest request, CancellationToken ct) =>
        await movies.UpdateAsync(id, request, ct);

    /// <summary>Baja lógica. 409 <c>movie_has_active_bookings</c> si tiene funciones futuras con reservas confirmadas.</summary>
    [HttpDelete("api/v1/movies/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await movies.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Cines (sedes): lectura completa, alta y edición. La lectura pública sigue en <c>GET /cinemas</c>.</summary>
[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class CinemasAdminController(CinemaAdminService cinemas) : ControllerBase
{
    [HttpGet("api/v1/admin/cinemas")]
    [ProducesResponseType<IReadOnlyList<AdminCinemaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminCinemaDto>>> List(CancellationToken ct) => Ok(await cinemas.ListAsync(ct));

    [HttpGet("api/v1/admin/cinemas/{id:int}")]
    [ProducesResponseType<AdminCinemaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminCinemaDto>> Get(int id, CancellationToken ct) => await cinemas.GetAsync(id, ct);

    [HttpPost("api/v1/cinemas")]
    [ProducesResponseType<AdminCinemaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminCinemaDto>> Create(UpsertCinemaRequest request, CancellationToken ct)
    {
        var cinema = await cinemas.CreateAsync(request, ct);
        return Created($"/api/v1/admin/cinemas/{cinema.Id}", cinema);
    }

    /// <summary>Desactivar un cine con funciones futuras con reservas confirmadas da 409 <c>cinema_has_active_bookings</c>.</summary>
    [HttpPut("api/v1/cinemas/{id:int}")]
    [ProducesResponseType<AdminCinemaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminCinemaDto>> Update(int id, UpsertCinemaRequest request, CancellationToken ct) =>
        await cinemas.UpdateAsync(id, request, ct);
}
