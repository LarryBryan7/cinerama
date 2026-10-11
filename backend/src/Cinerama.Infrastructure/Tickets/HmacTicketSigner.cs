using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Cinerama.Application.Common;
using Cinerama.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace Cinerama.Infrastructure.Tickets;

/// <summary>
/// RN-35: el QR contiene <c>{código}.{HMAC-SHA256 del código}</c>. Sin la clave nadie puede fabricar un QR válido, y
/// la comparación es de tiempo constante para no filtrar la firma por diferencias de tiempo.
/// </summary>
public sealed class HmacTicketSigner : ITicketSigner
{
    private readonly byte[] _key;

    public HmacTicketSigner(IOptions<TicketOptions> options) => _key = Encoding.UTF8.GetBytes(options.Value.SigningKey);

    public string Sign(string bookingCode) => $"{bookingCode}.{Base64Url.EncodeToString(Hash(bookingCode))}";

    public bool TryVerify(string payload, out string bookingCode)
    {
        bookingCode = "";

        var separator = payload.LastIndexOf('.');
        if (separator <= 0 || separator == payload.Length - 1)
            return false;

        byte[] given;
        try
        {
            given = Base64Url.DecodeFromChars(payload.AsSpan(separator + 1));
        }
        catch (FormatException)
        {
            return false;
        }

        var code = payload[..separator];
        if (!CryptographicOperations.FixedTimeEquals(given, Hash(code)))
            return false;

        bookingCode = code;
        return true;
    }

    private byte[] Hash(string value) => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value));
}
