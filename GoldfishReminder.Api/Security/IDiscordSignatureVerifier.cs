namespace GoldfishReminder.Api.Security;

public interface IDiscordSignatureVerifier
{
    bool Verify(Microsoft.AspNetCore.Http.HttpRequest request, string rawBody);
}