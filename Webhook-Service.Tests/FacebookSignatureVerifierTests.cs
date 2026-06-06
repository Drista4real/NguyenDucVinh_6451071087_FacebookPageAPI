using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Webhook_Service.Options;
using Webhook_Service.Services;

namespace Webhook_Service.Tests;

public sealed class FacebookSignatureVerifierTests
{
    private const string AppSecret = "test-app-secret";

    [Fact]
    public void Verify_AcceptsValidSha256Signature()
    {
        var payload = Encoding.UTF8.GetBytes("""{"object":"page"}""");
        var verifier = CreateVerifier();
        var signature =
            $"sha256={Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(AppSecret),
                payload)).ToLowerInvariant()}";

        Assert.True(verifier.Verify(payload, signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha1=1234")]
    [InlineData("sha256=not-hex")]
    public void Verify_RejectsMissingOrMalformedSignature(string? signature)
    {
        var verifier = CreateVerifier();

        Assert.False(verifier.Verify("payload"u8, signature));
    }

    [Fact]
    public void Verify_RejectsTamperedPayload()
    {
        var signedPayload = Encoding.UTF8.GetBytes("""{"message":"original"}""");
        var signature =
            $"sha256={Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(AppSecret),
                signedPayload)).ToLowerInvariant()}";
        var verifier = CreateVerifier();

        Assert.False(verifier.Verify(
            Encoding.UTF8.GetBytes("""{"message":"tampered"}"""),
            signature));
    }

    private static FacebookSignatureVerifier CreateVerifier() =>
        new(Microsoft.Extensions.Options.Options.Create(new FacebookWebhookOptions
        {
            AppSecret = AppSecret
        }));
}
