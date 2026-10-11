using FluentValidation;
using FluentValidation.Results;

namespace Cinerama.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Valida página y tamaño. Un valor fuera de rango es un error del cliente (400), no se corrige en silencio.</summary>
    public static void Validate(int page, int pageSize)
    {
        var failures = new List<ValidationFailure>();
        if (page < 1)
            failures.Add(new ValidationFailure("page", "La página debe ser mayor o igual a 1."));
        if (pageSize is < 1 or > MaxPageSize)
            failures.Add(new ValidationFailure("pageSize", $"El tamaño de página debe estar entre 1 y {MaxPageSize}."));
        if (failures.Count > 0)
            throw new ValidationException(failures);
    }
}
