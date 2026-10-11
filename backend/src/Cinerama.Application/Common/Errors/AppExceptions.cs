namespace Cinerama.Application.Common.Errors;

/// <summary>Recurso inexistente, o que existe pero no es del usuario (RN-32): ambos casos responden 404.</summary>
public sealed class NotFoundException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Conflicto con el estado actual (HTTP 409). <paramref name="extensions"/> viaja al cliente (por ejemplo, la lista de asientos en conflicto).</summary>
public sealed class ConflictException(string code, string message, IReadOnlyDictionary<string, object?>? extensions = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?>? Extensions { get; } = extensions;
}

/// <summary>Credencial o sesión inválida (HTTP 401). El <c>code</c> permite al cliente decidir qué hacer.</summary>
public sealed class AuthenticationFailedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
