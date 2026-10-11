using Cinerama.Api.Auth;
using Cinerama.Api.Configuration;
using Cinerama.Application.Catalog;
using Cinerama.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Cinerama.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/movies")]
public sealed class MoviesController(MovieService movies, TrailerCommentsService trailerComments) : ControllerBase
{
    /// <param name="status"><c>nowShowing</c>, <c>presale</c> o <c>comingSoon</c>. Sin valor, todas.</param>
    /// <param name="city">Películas con funciones próximas en un cine de esa ciudad.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<MovieListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<MovieListItemDto>>> List(
        string? search, int? genreId, string? status, string? city, int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        await movies.ListAsync(search, genreId, status, city, User.GetUserIdOrNull(), page, pageSize, ct);

    [HttpGet("{id:int}")]
    [ProducesResponseType<MovieDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieDetailDto>> Get(int id, CancellationToken ct) =>
        await movies.GetAsync(id, User.GetUserIdOrNull(), ct);

    /// <summary>Comentarios públicos del tráiler en YouTube (hasta 20). Si el video tiene menos, los que haya; ver <c>status</c>.</summary>
    [HttpGet("{id:int}/trailer-comments")]
    [ProducesResponseType<TrailerCommentsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TrailerCommentsDto>> TrailerComments(int id, int limit = TrailerCommentsService.MaxLimit, CancellationToken ct = default) =>
        await trailerComments.ListAsync(id, limit, ct);
}

/// <summary>
/// Opiniones. Leerlas es público; publicar, cambiar o borrar la propia exige sesión. La ruta es siempre <c>/reviews/mine</c>:
/// no hay forma de indicar el id de otra persona (RN-34).
/// </summary>
[ApiController]
[Route("api/v1/movies/{movieId:int}/reviews")]
public sealed class ReviewsController(ReviewService reviews) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<ReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<ReviewDto>>> List(
        int movieId, int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        await reviews.ListAsync(movieId, User.GetUserIdOrNull(), page, pageSize, ct);

    /// <summary>Publica la opinión propia o reemplaza la anterior: 201 la primera vez, 200 después.</summary>
    [HttpPut("mine")]
    [EnableRateLimiting(RateLimitSettings.ReviewsPolicy)]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ReviewDto>> PutMine(int movieId, UpsertReviewRequest request, CancellationToken ct)
    {
        var result = await reviews.UpsertMineAsync(movieId, User.GetUserId(), request, ct);
        return result.Created ? StatusCode(StatusCodes.Status201Created, result.Review) : Ok(result.Review);
    }

    [HttpDelete("mine")]
    [EnableRateLimiting(RateLimitSettings.ReviewsPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeleteMine(int movieId, CancellationToken ct)
    {
        await reviews.DeleteMineAsync(movieId, User.GetUserId(), ct);
        return NoContent();
    }
}

[ApiController]
[AllowAnonymous]
[Route("api/v1/cities")]
public sealed class CitiesController(CinemaService cinemas) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CityDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CityDto>>> List(CancellationToken ct) => Ok(await cinemas.ListCitiesAsync(ct));
}

[ApiController]
[AllowAnonymous]
[Route("api/v1/cinemas")]
public sealed class CinemasController(CinemaService cinemas) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CinemaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CinemaDto>>> List(string? city, CancellationToken ct) =>
        Ok(await cinemas.ListAsync(city, ct));
}

[ApiController]
[AllowAnonymous]
[Route("api/v1/genres")]
public sealed class GenresController(GenreService genres) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GenreDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> List(CancellationToken ct) =>
        Ok(await genres.ListAsync(ct));
}

[ApiController]
[AllowAnonymous]
[Route("api/v1/showtimes")]
public sealed class ShowtimesController(ShowtimeService showtimes) : ControllerBase
{
    /// <param name="date">Fecha local del cine (yyyy-MM-dd).</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<ShowtimeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ShowtimeDto>>> List(
        int? movieId, DateOnly? date, int? cinemaId, int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        await showtimes.ListAsync(movieId, date, cinemaId, page, pageSize, ct);

    [HttpGet("{id:int}")]
    [ProducesResponseType<ShowtimeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShowtimeDto>> Get(int id, CancellationToken ct) =>
        await showtimes.GetAsync(id, ct);
}
