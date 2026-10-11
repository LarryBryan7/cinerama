using Microsoft.EntityFrameworkCore;

namespace Cinerama.Application.Common.Errors;

/// <summary>
/// Una inserción o actualización violó un índice único. La infraestructura traduce el error de SQL Server a esta
/// excepción para que los servicios decidan qué significa cada índice (un asiento ya reservado, un email repetido,
/// una clave de idempotencia usada) sin depender del proveedor de base de datos.
/// </summary>
/// <remarks>Hereda de <see cref="DbUpdateException"/>: quien ya captura esa excepción sigue funcionando.</remarks>
public sealed class UniqueConstraintViolationException(string? constraintName, string message, Exception inner)
    : DbUpdateException(message, inner)
{
    public string? ConstraintName { get; } = constraintName;

    /// <summary>
    /// ¿Involucra este índice? Busca el nombre en el mensaje original, así funciona aunque el servidor SQL esté
    /// configurado en otro idioma y el texto no se pueda interpretar.
    /// </summary>
    public bool Involves(string indexName) =>
        string.Equals(ConstraintName, indexName, StringComparison.OrdinalIgnoreCase)
        || (InnerException?.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase) ?? false);
}
