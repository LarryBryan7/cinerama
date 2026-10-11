using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Cinerama.Api.Errors;

public static class ProblemResponses
{
    public static ProblemDetails Create(int status, string code, string title, string? detail = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://cinerama.example/errors/{code.Replace('_', '-')}",
        };
        problem.Extensions["code"] = code;
        return problem;
    }

    public static ValueTask WriteAsync(
        this IProblemDetailsService service, HttpContext context, ProblemDetails problem, Exception? exception = null)
    {
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return service.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    /// <summary>Código estable para respuestas sin cuerpo (404 de una ruta inexistente, 405, etc.).</summary>
    public static string DefaultCode(int status) => status switch
    {
        400 => "bad_request",
        401 => "unauthorized",
        403 => "forbidden",
        404 => "not_found",
        405 => "method_not_allowed",
        415 => "unsupported_media_type",
        429 => "rate_limited",
        >= 500 => "internal_error",
        _ => "error",
    };
}
