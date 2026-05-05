using GoldfishReminder.Application.Services;
using GoldfishReminder.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace GoldfishReminder.Infrastructure.Services;

//Web 網址提供實作
public class WebUrlProvider : IWebUrlProvider
{
    private readonly WebOptions webOptions;

    public WebUrlProvider(IOptions<WebOptions> webOptions)
    {
        this.webOptions = webOptions.Value;
    }

    //取得 BaseUrl
    public string GetBaseUrl()
    {
        return webOptions.BaseUrl;
    }
}