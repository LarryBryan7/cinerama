using System.ComponentModel.DataAnnotations;

namespace Cinerama.Application.Common.Options;

public sealed class TicketOptions
{
    public const string SectionName = "Ticket";

    /// <summary>Clave HMAC del QR. Distinta de la clave JWT, para que comprometer una no comprometa la otra. Nunca se versiona.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = "";
}
