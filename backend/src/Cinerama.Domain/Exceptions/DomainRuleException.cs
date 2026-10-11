namespace Cinerama.Domain.Exceptions;

/// <summary>
/// Se lanza cuando una operación viola una regla de negocio (RN-xx). Se traduce a HTTP 422.
/// El <see cref="Code"/> es estable: los clientes deciden según él, nunca según el texto del mensaje.
/// </summary>
public class DomainRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
