namespace Cinerama.Application.Common;

/// <summary>
/// Convierte lo que se guarda en la base (una ruta relativa como <c>posters/marea-alta.jpg</c> o una URL absoluta) en la
/// URL absoluta que usa el cliente. La implementación vive en Api porque necesita conocer la dirección pública del servidor.
/// </summary>
public interface IMediaUrls
{
    string? Absolute(string? stored);
}
