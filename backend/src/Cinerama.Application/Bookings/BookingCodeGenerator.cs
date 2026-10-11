using System.Security.Cryptography;

namespace Cinerama.Application.Bookings;

/// <summary>
/// Código público de la reserva (RN-28): 8 caracteres aleatorios, no secuenciales y sin caracteres ambiguos
/// (sin 0/O ni 1/I), pensados para dictarse o leerse en una boletería. 32^8 ≈ 1,1 × 10^12 combinaciones.
/// </summary>
public static class BookingCodeGenerator
{
    public const int Length = 8;
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate() => string.Create(Length, 0, static (span, _) =>
    {
        for (var i = 0; i < span.Length; i++)
            span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
    });
}
