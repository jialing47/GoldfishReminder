using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Application.Workflows;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GoldfishReminder.Api.Pages;

public class IndexModel : PageModel
{
    private readonly IWebLinkTokenService webLinkTokenService;
    private readonly ISettingsPageService settingsPageService;
    private readonly CreditBillWorkflow creditBillWorkflow;

    // 初始化頁面模型
    public IndexModel(IWebLinkTokenService webLinkTokenService, ISettingsPageService settingsPageService, CreditBillWorkflow creditBillWorkflow)
    {
        this.webLinkTokenService = webLinkTokenService;
        this.settingsPageService = settingsPageService;
        this.creditBillWorkflow = creditBillWorkflow;
    }

    public Guid? UserId { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? AlertMessage { get; private set; }
    public bool ShowLanding { get; private set; } // 沒 token 沒 cookie 時顯示 landing
    public string ActiveTab { get; private set; } = "bankAccounts";

    public List<BankItem> Banks { get; private set; } = new();
    public List<BankAccountItem> BankAccounts { get; private set; } = new();
    public Dictionary<string, string> BankNameByCode { get; private set; } = new();
    public List<CreditSettingItem> CreditSettings { get; private set; } = new();
    public Dictionary<Guid, string> PaymentAccountNameById { get; private set; } = new();
    public List<PaymentAccountOptionItem> PaymentAccountOptions { get; private set; } = new();
    public List<CurrentBillItem> CurrentBills { get; private set; } = new();
    public List<HistoryBillItem> HistoryBills { get; private set; } = new();
    public List<HistoryMonthOptionItem> HistoryMonthOptions { get; private set; } = new();
    public List<AccountImpactSummary> BankAccountImpacts { get; private set; } = new();
    public int HistoryYear { get; private set; }
    public int HistoryMonth { get; private set; }

    [BindProperty]
    public BankAccountFormModel BankAccountForm { get; set; } = new();

    [BindProperty]
    public CreditSettingFormModel CreditSettingForm { get; set; } = new();

    [BindProperty]
    public BillAmountFormModel CurrentBillAmountForm { get; set; } = new();

    // 載入設定頁
    public async Task<IActionResult> OnGetAsync(string? token, string? tab, int? historyYear, int? historyMonth, CancellationToken cancellationToken)
    {
        ActiveTab = NormalizeTab(tab);

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

                return Redirect($"/?tab={ActiveTab}");
            }
            catch
            {
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

        if (!TryGetUserIdFromCookie(out var userId))
        {
            // 沒 token 沒 cookie 顯示 landing 頁 不顯示錯誤訊息
            ShowLanding = true;
            return Page();
        }

        UserId = userId;
        await LoadDataAsync(userId, historyYear, historyMonth, cancellationToken);
        return Page();
    }

    // 新增或更新銀行帳戶
    public async Task<IActionResult> OnPostUpsertBankAccountAsync(string? tab, int? historyYear, int? historyMonth, CancellationToken cancellationToken)
    {
        ActiveTab = "bankAccounts";
        var isAjaxRequest = IsAjaxRequest();

        if (!TryGetUserIdFromCookie(out var userId))
        {
            return HandleUnauthorized(isAjaxRequest);
        }

        try
        {
            var input = new BankAccountInputModel
            {
                Id = BankAccountForm.Id,
                BankCode = (BankAccountForm.BankCode ?? string.Empty).Trim(),
                AccountName = (BankAccountForm.AccountName ?? string.Empty).Trim(),
                AccountType = (BankAccountForm.AccountType ?? string.Empty).Trim(),
                Balance = BankAccountForm.Balance,
                Enabled = Request.Form.ContainsKey("BankAccountForm.Enabled")
            };

            if (string.IsNullOrWhiteSpace(input.BankCode))
            {
                return BankAccountBadRequest("銀行代碼不可為空白");
            }

            if (string.IsNullOrWhiteSpace(input.AccountName))
            {
                return BankAccountBadRequest("帳戶名稱不可為空白");
            }

            if (!string.Equals(input.AccountType, "digital", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(input.AccountType, "physical", StringComparison.OrdinalIgnoreCase))
            {
                return BankAccountBadRequest("帳戶類型只接受 digital 或 physical");
            }

            await settingsPageService.SaveBankAccountAsync(userId, input, cancellationToken);

            if (isAjaxRequest)
            {
                return new JsonResult(new { ok = true });
            }

            return Redirect(BuildRedirectUrl("bankAccounts", historyYear, historyMonth));
        }
        catch
        {
            return BankAccountBadRequest("儲存銀行帳戶失敗 請稍後再試");
        }

        // 回傳銀行帳戶表單錯誤
        IActionResult BankAccountBadRequest(string message)
        {
            if (isAjaxRequest)
            {
                Response.StatusCode = 400;
                return new JsonResult(new { ok = false, message });
            }

            AlertMessage = message;
            LoadDataAsync(userId, historyYear, historyMonth, cancellationToken).GetAwaiter().GetResult();
            return Page();
        }
    }

    // 新增或更新信用卡設定
    public async Task<IActionResult> OnPostUpsertCreditSettingAsync(string? tab, int? historyYear, int? historyMonth, CancellationToken cancellationToken)
    {
        ActiveTab = "creditSettings";
        var isAjaxRequest = IsAjaxRequest();

        if (!TryGetUserIdFromCookie(out var userId))
        {
            return HandleUnauthorized(isAjaxRequest);
        }

        try
        {
            var input = new CreditSettingInputModel
            {
                Id = CreditSettingForm.Id,
                BankCode = (CreditSettingForm.BankCode ?? string.Empty).Trim(),
                StatementDay = CreditSettingForm.StatementDay,
                PaymentDueDay = CreditSettingForm.PaymentDueDay,
                PaymentBankAccountId = CreditSettingForm.PaymentBankAccountId,
                Enabled = Request.Form.ContainsKey("CreditSettingForm.Enabled")
            };

            if (string.IsNullOrWhiteSpace(input.BankCode))
            {
                return CreditSettingBadRequest("銀行代碼不可為空白");
            }

            if (input.StatementDay < 1 || input.StatementDay > 31)
            {
                return CreditSettingBadRequest("結帳日需介於 1 到 31");
            }

            if (input.PaymentDueDay < 1 || input.PaymentDueDay > 31)
            {
                return CreditSettingBadRequest("繳費日需介於 1 到 31");
            }

            await settingsPageService.SaveCreditSettingAsync(userId, input, cancellationToken);

            if (isAjaxRequest)
            {
                return new JsonResult(new { ok = true });
            }

            return Redirect(BuildRedirectUrl("creditSettings", historyYear, historyMonth));
        }
        catch
        {
            return CreditSettingBadRequest("儲存信用卡設定失敗 請稍後再試");
        }

        // 回傳信用卡設定表單錯誤
        IActionResult CreditSettingBadRequest(string message)
        {
            if (isAjaxRequest)
            {
                Response.StatusCode = 400;
                return new JsonResult(new { ok = false, message });
            }

            AlertMessage = message;
            LoadDataAsync(userId, historyYear, historyMonth, cancellationToken).GetAwaiter().GetResult();
            return Page();
        }
    }

    // 更新本月帳單金額
    public async Task<IActionResult> OnPostUpdateBillAmountAsync(string? tab, int? historyYear, int? historyMonth, CancellationToken cancellationToken)
    {
        ActiveTab = "currentBills";
        var isAjaxRequest = IsAjaxRequest();

        if (!TryGetUserIdFromCookie(out var userId))
        {
            return HandleUnauthorized(isAjaxRequest);
        }

        try
        {
            if (!CurrentBillAmountForm.BillId.HasValue || CurrentBillAmountForm.BillId.Value == Guid.Empty)
            {
                return CurrentBillAmountBadRequest("帳單 Id 不可為空白");
            }

            if (CurrentBillAmountForm.BillAmount < 0)
            {
                return CurrentBillAmountBadRequest("帳單金額不可小於 0");
            }

            var input = new UpdateBillAmountInput
            {
                BillId = CurrentBillAmountForm.BillId.Value,
                BillAmount = CurrentBillAmountForm.BillAmount
            };

            await settingsPageService.UpdateBillAmountAsync(userId, input, cancellationToken);

            if (isAjaxRequest)
            {
                return new JsonResult(new { ok = true });
            }

            return Redirect(BuildRedirectUrl("currentBills", historyYear, historyMonth));
        }
        catch (ArgumentException ex)
        {
            return CurrentBillAmountBadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return CurrentBillAmountBadRequest(ex.Message);
        }
        catch
        {
            return CurrentBillAmountBadRequest("更新帳單金額失敗");
        }

        // 回傳本月帳單金額表單錯誤
        IActionResult CurrentBillAmountBadRequest(string message)
        {
            if (isAjaxRequest)
            {
                Response.StatusCode = 400;
                return new JsonResult(new { ok = false, message });
            }

            AlertMessage = message;
            LoadDataAsync(userId, historyYear, historyMonth, cancellationToken).GetAwaiter().GetResult();
            return Page();
        }
    }

    //標記本月帳單已繳費
    public async Task<IActionResult> OnPostMarkBillPaidAsync(Guid billId, int? historyYear, int? historyMonth, CancellationToken cancellationToken)
    {
        ActiveTab = "currentBills";
        var isAjaxRequest = IsAjaxRequest();

        if (!TryGetUserIdFromCookie(out var userId))
        {
            return HandleUnauthorized(isAjaxRequest);
        }

        try
        {
            if (billId == Guid.Empty)
            {
                return MarkPaidBadRequest("帳單資料錯誤");
            }

            await creditBillWorkflow.MarkBillPaidAsync(billId, userId, cancellationToken);

            if (isAjaxRequest)
            {
                return new JsonResult(new { ok = true });
            }

            return Redirect(BuildRedirectUrl("currentBills", historyYear, historyMonth));
        }
        catch
        {
            return MarkPaidBadRequest("設定已繳費失敗");
        }

        IActionResult MarkPaidBadRequest(string message)
        {
            if (isAjaxRequest)
            {
                Response.StatusCode = 400;
                return new JsonResult(new { ok = false, message });
            }

            AlertMessage = message;
            LoadDataAsync(userId, historyYear, historyMonth, cancellationToken).GetAwaiter().GetResult();
            return Page();
        }
    }

    // 載入頁面資料
    private async Task LoadDataAsync(Guid userId, int? historyYear, int? historyMonth, CancellationToken cancellationToken)
    {
        var pageData = await settingsPageService.GetPageDataAsync(userId, historyYear, historyMonth, cancellationToken);

        Banks = pageData.Banks;
        BankAccounts = pageData.BankAccounts;
        BankNameByCode = pageData.BankNameByCode;
        CreditSettings = pageData.CreditSettings;
        PaymentAccountNameById = pageData.PaymentAccountNameById;
        PaymentAccountOptions = pageData.PaymentAccountOptions;
        CurrentBills = pageData.CurrentBills;
        HistoryBills = pageData.HistoryBills;
        HistoryMonthOptions = pageData.HistoryMonthOptions;
        BankAccountImpacts = pageData.BankAccountImpacts;
        HistoryYear = pageData.HistoryYear;
        HistoryMonth = pageData.HistoryMonth;
    }

    // 未登入時回傳對應結果
    private IActionResult HandleUnauthorized(bool isAjaxRequest)
    {
        if (isAjaxRequest)
        {
            Response.StatusCode = 401;
            return new JsonResult(new { ok = false, message = "尚未登入 請回 Discord 重新開啟網頁連結" });
        }

        ErrorMessage = "尚未登入";
        return Page();
    }

    // 判斷是否為 Ajax 請求
    private bool IsAjaxRequest()
    {
        return string.Equals(Request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    }

    // 從 cookie 解析 userId
    private bool TryGetUserIdFromCookie(out Guid userId)
    {
        userId = Guid.Empty;

        if (!Request.Cookies.TryGetValue("gr_uid", out var userIdText))
        {
            return false;
        }

        return Guid.TryParse(userIdText, out userId);
    }

    // 正規化 tab 名稱
    private static string NormalizeTab(string? tab)
    {
        if (tab == "creditSettings")
        {
            return "creditSettings";
        }

        if (tab == "currentBills")
        {
            return "currentBills";
        }

        if (tab == "historyBills")
        {
            return "historyBills";
        }

        return "bankAccounts";
    }

    // 建立導頁網址
    private static string BuildRedirectUrl(string activeTab, int? historyYear, int? historyMonth)
    {
        if (historyYear.HasValue && historyMonth.HasValue)
        {
            return $"/?tab={activeTab}&historyYear={historyYear.Value}&historyMonth={historyMonth.Value}";
        }

        return $"/?tab={activeTab}";
    }

    public class BankAccountFormModel
    {
        public Guid? Id { get; set; }
        public string? BankCode { get; set; }
        public string? AccountName { get; set; }
        public string AccountType { get; set; } = "digital";
        public string? Balance { get; set; }
    }

    public class CreditSettingFormModel
    {
        public Guid? Id { get; set; }
        public string? BankCode { get; set; }
        public int StatementDay { get; set; } = 1;
        public int PaymentDueDay { get; set; } = 1;
        public Guid? PaymentBankAccountId { get; set; }
    }

    public class BillAmountFormModel
    {
        public Guid? BillId { get; set; }
        public int BillAmount { get; set; }
    }
}
