using Cinerama.Application.Common.Options;
using Cinerama.Infrastructure.Tickets;
using Microsoft.Extensions.Options;

namespace Cinerama.UnitTests.Bookings;

public class TicketSignerTests
{
    private const string Key = "unit-test-ticket-signing-key-with-32-chars+";

    private static HmacTicketSigner Signer(string key = Key) =>
        new(Options.Create(new TicketOptions { SigningKey = key }));

    [Fact]
    [Trait("Case", "RN-35")]
    public void ASignedPayload_VerifiesAndReturnsTheBookingCode()
    {
        var signer = Signer();

        var payload = signer.Sign("K7QH3MXP");

        payload.ShouldStartWith("K7QH3MXP.");
        signer.TryVerify(payload, out var code).ShouldBeTrue();
        code.ShouldBe("K7QH3MXP");
    }

    [Fact]
    [Trait("Case", "RN-35")]
    public void Signing_IsDeterministic() =>
        Signer().Sign("K7QH3MXP").ShouldBe(Signer().Sign("K7QH3MXP"));

    [Fact]
    [Trait("Case", "RN-35")]
    public void ChangingTheCode_InvalidatesThePayload()
    {
        var signer = Signer();
        var payload = signer.Sign("K7QH3MXP");

        signer.TryVerify(payload.Replace("K7QH3MXP", "K7QH3MXQ"), out _).ShouldBeFalse();
    }

    [Fact]
    [Trait("Case", "RN-35")]
    public void ChangingASingleCharacterOfTheSignature_InvalidatesThePayload()
    {
        var signer = Signer();
        var payload = signer.Sign("K7QH3MXP");
        var tampered = payload[..^1] + (payload[^1] == 'A' ? 'B' : 'A');

        signer.TryVerify(tampered, out _).ShouldBeFalse();
    }

    [Fact]
    [Trait("Case", "RN-35")]
    public void APayloadSignedWithAnotherKey_IsRejected()
    {
        var forged = Signer("a-completely-different-ticket-key-32-chars!!").Sign("K7QH3MXP");

        Signer().TryVerify(forged, out _).ShouldBeFalse();
    }

    [Theory]
    [Trait("Case", "RN-35")]
    [InlineData("")]
    [InlineData("K7QH3MXP")]
    [InlineData(".")]
    [InlineData(".abc")]
    [InlineData("K7QH3MXP.")]
    [InlineData("K7QH3MXP.no es base64 !!")]
    public void MalformedPayloads_AreRejectedWithoutThrowing(string payload)
    {
        Signer().TryVerify(payload, out var code).ShouldBeFalse();
        code.ShouldBeEmpty();
    }
}
