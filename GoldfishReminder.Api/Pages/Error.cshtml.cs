using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GoldfishReminder.Api.Pages;

// 自訂錯誤頁 處理 404 與其他 status code 與 unhandled exception
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ErrorModel : PageModel
{
    // 是否為找不到頁面 影響顯示文案
    public bool IsNotFound { get; private set; }

    // 顯示給使用者看的 status code 改名避免與 base PageModel.StatusCode(int) 方法衝突
    public int DisplayStatusCode { get; private set; }

    // 根據 status code 顯示 整數轉路由用
    public IActionResult OnGet(int? code)
    {
        if (code.HasValue)
        {
            DisplayStatusCode = code.Value;
        }
        else
        {
            // 沒帶 code 表示走 UseExceptionHandler 路徑 預設 500
            DisplayStatusCode = 500;
        }

        IsNotFound = DisplayStatusCode == 404;
        Response.StatusCode = DisplayStatusCode;
        return Page();
    }
}
