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
    private readonly PublicKey? cachedPublicKey;                                  // 預先 import 一次的 Ed25519 公鑰 NSec Verify 允許多執行緒同時讀取此參考

    // 啟動時預先解 hex 並 import 公鑰 ctor 失敗代表 PublicKey config 格式錯 直接讓 app 起不來 fail-fast
    public DiscordSignatureVerifier(IOptions<DiscordOptions> discordOptions)
    {
        this.discordOptions = discordOptions.Value;

        if (string.IsNullOrWhiteSpace(this.discordOptions.PublicKey))
        {
            // 空值不視為錯誤 行為跟舊版一致 Verify 會直接 return false
            cachedPublicKey = null;
            return;
        }

        var publicKeyBytes = Convert.FromHexString(this.discordOptions.PublicKey);
        cachedPublicKey = PublicKey.Import(
            SignatureAlgorithm.Ed25519,
            publicKeyBytes,
            KeyBlobFormat.RawPublicKey);
    }

    // 驗證 Discord interaction 請求 包含時間戳防 replay 與 Ed25519 簽章兩段
    public bool Verify(HttpRequest request, string rawBody)
    {
        if (cachedPublicKey == null)
        {
            return false;
        }

        var sigHex = request.Headers["X-Signature-Ed25519"].ToString();
        var timestamp = request.Headers["X-Signature-Timestamp"].ToString();

        if (string.IsNullOrWhiteSpace(sigHex) || string.IsNullOrWhiteSpace(timestamp))
        {
            return false;
        }

        // 時間戳必須為 Unix 秒數字串 解析失敗即拒絕
        if (!long.TryParse(timestamp, out var signatureUnixSeconds))
        {
            return false;
        }

        // 時間戳偏移過大 拒絕避免 replay
        var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(nowUnixSeconds - signatureUnixSeconds) > MaxTimestampSkewSeconds)
        {
            return false;
        }

        byte[] signatureBytes;

        try
        {
            signatureBytes = Convert.FromHexString(sigHex);
        }
        catch
        {
            return false;
        }

        var messageBytes = Encoding.UTF8.GetBytes(timestamp + rawBody);

        return SignatureAlgorithm.Ed25519.Verify(cachedPublicKey, messageBytes, signatureBytes);
    }
}
