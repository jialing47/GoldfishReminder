using GoldfishReminder.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GoldfishReminder.Api.Pages;

// 首頁 負責 landing 行銷頁與 token consume 後跳轉 Settings
public class IndexModel : PageModel
{
    private readonly IWebLinkTokenService webLinkTokenService;

    // 初始化頁面模型
    public IndexModel(IWebLinkTokenService webLinkTokenService)
    {
        this.webLinkTokenService = webLinkTokenService;
    }

    public bool ShowLanding { get; private set; }                       // 沒 token 沒 cookie 時顯示 landing
    public string? ErrorMessage { get; private set; }                   // Token consume 失敗時顯示

    // 載入首頁 token 處理 + cookie 判斷 + 跳轉 Settings
    public async Task<IActionResult> OnGetAsync(string? token, string? tab, CancellationToken cancellationToken)
    {
        // 帶 token 進來 嘗試 consume 後跳轉 Settings
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                var result = await webLinkTokenService.ConsumeAsync(token, cancellationToken);
                Response.Cookies.Append(
                    "gr_uid",
                    result.UserId.ToString(),
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Expires = DateTimeOffset.UtcNow.AddHours(1)
                    });
                return Redirect(BuildSettingsUrl(tab));
            }
            catch
            {
                // Consume 失敗 清掉殘留 cookie 並顯示錯誤
                Response.Cookies.Append(
                    "gr_uid",
                    string.Empty,
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Expires = DateTimeOffset.UtcNow.AddDays(-1)
                    });
                ErrorMessage = "連結已失效或無效 請回 Discord 重新取得網頁連結";
                return Page();
            }
        }

        // 沒 token 但已有 cookie 直接跳 Settings
        if (HasValidCookie())
        {
            return Redirect(BuildSettingsUrl(tab));
        }

        // 沒 token 沒 cookie 顯示 landing
        ShowLanding = true;
        return Page();
    }

    // 檢查 cookie 是否包含有效 userId
    private bool HasValidCookie()
    {
        if (!Request.Cookies.TryGetValue("gr_uid", out var userIdText))
        {
            return false;
        }
        return Guid.TryParse(userIdText, out _);
    }

    // 組成 Settings 頁網址 有指定 tab 就帶上
    private static string BuildSettingsUrl(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab))
        {
            return "/Settings";
        }
        return "/Settings?tab=" + tab;
    }
}
