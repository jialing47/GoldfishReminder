using System;
using System.Text;
using GoldfishReminder.Infrastructure.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSec.Cryptography;

namespace GoldfishReminder.Api.Security;

public class DiscordSignatureVerifier : IDiscordSignatureVerifier
{
    private const int MaxTimestampSkewSeconds = 300; // 簽章時間戳容忍區間 超過 ±5 分鐘視為 replay 直接拒絕

    private readonly DiscordOptions discordOptions;

    public DiscordSignatureVerifier(IOptions<DiscordOptions> discordOptions)
    {
        this.discordOptions = discordOptions.Value;
    }

    //驗證 Discord interaction 請求 包含時間戳防 replay 與 Ed25519 簽章兩段
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

        //時間戳必須為 Unix 秒數字串 解析失敗即拒絕
        if (!long.TryParse(timestamp, out var signatureUnixSeconds))
        {
            return false;
        }

        //時間戳偏移過大 拒絕避免 replay
        var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(nowUnixSeconds - signatureUnixSeconds) > MaxTimestampSkewSeconds)
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