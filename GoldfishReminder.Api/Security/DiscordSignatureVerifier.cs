using System;
using System.Text;
using GoldfishReminder.Infrastructure.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSec.Cryptography;

namespace GoldfishReminder.Api.Security;

public class DiscordSignatureVerifier : IDiscordSignatureVerifier
{
    private readonly DiscordOptions discordOptions;

    public DiscordSignatureVerifier(IOptions<DiscordOptions> discordOptions)
    {
        this.discordOptions = discordOptions.Value;
    }

    public bool Verify(HttpRequest request, string rawBody)
    {
        var sigHex = request.Headers["X-Signature-Ed25519"].ToString();
        var timestamp = request.Headers["X-Signature-Timestamp"].ToString();

        if (string.IsNullOrWhiteSpace(discordOptions.PublicKey)
            || string.IsNullOrWhiteSpace(sigHex)
            || string.IsNullOrWhiteSpace(timestamp))
        {
            return false;
        }

        byte[] publicKeyBytes;
        byte[] signatureBytes;

        try
        {
            publicKeyBytes = Convert.FromHexString(discordOptions.PublicKey);
            signatureBytes = Convert.FromHexString(sigHex);
        }
        catch
        {
            return false;
        }

        var messageBytes = Encoding.UTF8.GetBytes(timestamp + rawBody);

        var publicKey = PublicKey.Import(
            SignatureAlgorithm.Ed25519,
            publicKeyBytes,
            KeyBlobFormat.RawPublicKey);

        return SignatureAlgorithm.Ed25519.Verify(publicKey, messageBytes, signatureBytes);
    }
}