using Cinerama.Application.Common.Errors;
using Cinerama.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cinerama.Api.Errors;

/// <summary>
/// Único lugar donde las excepciones de la aplicación se traducen a HTTP (application/problem+json, RFC 9457).
/// Un error inesperado responde 500 con el traceId, pero sin mensaje interno ni stack trace.
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problems, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            // El cliente cerró la conexión: no hay nadie a quien responder y no es un error del servidor.
            context.Response.StatusCode = 499;
            return true;
        }

        var problem = Map(exception);

        if (problem.Status >= 500)
            logger.LogError(exception, "Error no controlado en {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogInformation("{Code} ({Status}) en {Method} {Path}",
                problem.Extensions["code"], problem.Status, context.Request.Method, context.Request.Path);

        await problems.WriteAsync(context, problem, exception);
        return true;
    }

    public static ProblemDetails Map(Exception exception) => exception switch
    {
        ValidationException v => Validation(v),
        NotFoundException e => ProblemResponses.Create(StatusCodes.Status404NotFound, e.Code, "Recurso no encontrado", e.Message),
        ConflictException e => WithExtensions(
            ProblemResponses.Create(StatusCodes.Status409Conflict, e.Code, "Conflicto con el estado actual", e.Message), e.Extensions),
        AuthenticationFailedException e => ProblemResponses.Create(StatusCodes.Status401Unauthorized, e.Code, "No autenticado", e.Message),
        DomainRuleException e => ProblemResponses.Create(StatusCodes.Status422UnprocessableEntity, e.Code, "Regla de negocio incumplida", e.Message),

        // Otra solicitud modificó la misma fila (RowVersion): es un conflicto, no un error del servidor.
        DbUpdateConcurrencyException => ProblemResponses.Create(StatusCodes.Status409Conflict, "concurrency_conflict",
            "Conflicto de concurrencia", "El recurso cambió mientras se procesaba la solicitud. Vuelve a intentarlo."),

        // Los servicios traducen los índices que conocen; los demás llegan aquí como un conflicto genérico.
        UniqueConstraintViolationException => ProblemResponses.Create(StatusCodes.Status409Conflict, "unique_violation",
            "Conflicto con el estado actual", "Ya existe un recurso con esos datos."),

        _ => ProblemResponses.Create(StatusCodes.Status500InternalServerError, "internal_error",
            "Ocurrió un error inesperado", "Si el problema continúa, indica este identificador de seguimiento."),
    };

    private static ProblemDetails WithExtensions(ProblemDetails problem, IReadOnlyDictionary<string, object?>? extensions)
    {
        if (extensions is not null)
            foreach (var (key, value) in extensions)
                problem.Extensions[key] = value;
        return problem;
    }

    private static ProblemDetails Validation(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        var problem = ProblemResponses.Create(StatusCodes.Status400BadRequest, "validation_failed",
            "Hay errores de validación", "Revisa los campos indicados.");
        problem.Extensions["errors"] = errors;
        return problem;
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
