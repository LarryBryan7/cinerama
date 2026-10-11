namespace Cinerama.Application.Common;

/// <summary>
/// Firma el contenido del QR del ticket (RN-35): <c>{código}.{firma HMAC}</c>. Un QR alterado no verifica.
/// </summary>
public interface ITicketSigner
{
    string Sign(string bookingCode);

    /// <summary>Devuelve el código de reserva si la firma es válida.</summary>
    bool TryVerify(string payload, out string bookingCode);
}
