using System.Text.RegularExpressions;

namespace Cinerama.Application.Admin;

/// <summary>
/// Valida lo que el panel guarda como dirección de una imagen o un video (RN-48): una ruta **relativa** a la carpeta de medios de la API
/// (<c>posters/marea-alta.jpg</c>) o una URL absoluta <c>https</c>. Nada de rutas que salgan de la carpeta (<c>..</c>), de esquemas raros
/// (<c>javascript:</c>, <c>file:</c>) ni de extensiones que la API no sirve.
/// </summary>
public static partial class MediaValues
{
    public static readonly string[] ImageExtensions = ["jpg", "jpeg", "png", "webp"];
    public static readonly string[] VideoExtensions = ["mp4"];

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_\-/]*\.[A-Za-z0-9]+$")]
    private static partial Regex RelativePath();

    public static bool IsValid(string? value, string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true; // opcional

        value = value.Trim();
        if (value.Length > 500)
            return false;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0 && HasExtension(uri.AbsolutePath, extensions);

        // Una ruta relativa: sin "..", sin "//" ni "\" y con una extensión permitida.
        return !value.Contains("..") && !value.Contains("//") && !value.Contains('\\') && RelativePath().IsMatch(value) && HasExtension(value, extensions);
    }

    /// <summary>Texto opcional listo para guardar: recortado y <c>null</c> si está vacío.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool HasExtension(string path, string[] extensions)
    {
        var dot = path.LastIndexOf('.');
        return dot >= 0 && extensions.Contains(path[(dot + 1)..], StringComparer.OrdinalIgnoreCase);
    }
}
