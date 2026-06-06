using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Webhook_Service.Options;

namespace Webhook_Service.Services;

public interface IFacebookSignatureVerifier
{
    bool Verify(ReadOnlySpan<byte> payload, string? signatureHeader);
}

public sealed class FacebookSignatureVerifier : IFacebookSignatureVerifier
{
    private const string SignaturePrefix = "sha256=";
    private readonly FacebookWebhookOptions _options;

    public FacebookSignatureVerifier(IOptions<FacebookWebhookOptions> options)
    {
        _options = options.Value;
    }

    public bool Verify(ReadOnlySpan<byte> payload, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(_options.AppSecret))
        {
            throw new InvalidOperationException(
                "Facebook:AppSecret chưa được cấu hình.");
        }

        if (string.IsNullOrWhiteSpace(signatureHeader) ||
            !signatureHeader.StartsWith(
                SignaturePrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suppliedHex = signatureHeader[SignaturePrefix.Length..];
        if (suppliedHex.Length != 64)
        {
            return false;
        }

        byte[] suppliedHash;
        try
        {
            suppliedHash = Convert.FromHexString(suppliedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        var secret = Encoding.UTF8.GetBytes(_options.AppSecret);
        var expectedHash = HMACSHA256.HashData(secret, payload);
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}
