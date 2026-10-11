using Cinerama.Api.Configuration;
using Cinerama.Application.Common;
using Microsoft.Extensions.Options;

namespace Cinerama.Api.Storage;

/// <summary>Arma la URL absoluta de una imagen. Una URL que ya es absoluta se respeta; una ruta relativa cuelga de <c>/media</c>.</summary>
public sealed class HttpMediaUrls(IHttpContextAccessor accessor, IOptions<MediaSettings> options) : IMediaUrls
{
    public string? Absolute(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return null;

        if (stored.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || stored.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return stored;

        var path = $"{MediaSettings.RequestPath}/{stored.TrimStart('/')}";
        var configured = options.Value.PublicBaseUrl?.TrimEnd('/');
        if (!string.IsNullOrEmpty(configured))
            return configured + path;

        var request = accessor.HttpContext?.Request;
        return request is null ? path : $"{request.Scheme}://{request.Host}{request.PathBase}{path}";
    }
}
