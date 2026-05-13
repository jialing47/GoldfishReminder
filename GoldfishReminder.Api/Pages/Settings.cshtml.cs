using System.Security.Claims;
using GoldfishReminder.Application;
using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Application.Workflows;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GoldfishReminder.Api.Pages;

public class SettingsModel : PageModel
{
    private readonly ISettingsPageService settingsPageService;
    private readonly CreditBillWorkflow creditBillWorkflow;

    // 初始化頁面模型
    public SettingsModel(ISettingsPageService settingsPageService, CreditBillWorkflow creditBillWorkflow)
    {
        this.settingsPageService = settingsPageService;
        this.creditBillWorkflow = creditBillWorkflow;
    }

    public string? ErrorMessage { get; private set; }
    public string? AlertMessage { get; private set; }
    public string ActiveTab { get; private set; } = "bankAccounts";

    public List<BankItem> Banks { get; private set; } = new();
    public List<BankAccountItem> BankAccounts { get; private set; } = new();
    public Dictionary<string, string> BankNameByCode { get; private set; } = new();
    public List<CreditSettingItem> CreditSettings { get; private set; } = new();
    public List<PaymentAccountOptionItem> PaymentAccountOptions { get; private set; } = new();
    public List<CurrentBillItem> CurrentBills { get; private set; } = new();
    public List<HistoryBillItem> HistoryBills { get; private set; } = new();
    public List<HistoryMonthOptionItem> HistoryMonthOptions { get; private set; } = new();
    public List<AccountImpactSummary> BankAccountImpacts { get; private set; } = new();
    public int HistoryYear { get; private set; }
    public int HistoryMonth { get; private set; }

    // 台灣今日日期 給歷史帳單三態判斷使用
    public DateTime TodayInTaipei { get; private set; } = TaiwanClock.GetToday();

    [BindProperty]
    public BankAccountFormModel BankAccountForm { get; set; } = new();

    [BindProperty]
    public CreditSettingFormModel CreditSettingForm { get; set; } = new();

    [BindProperty]
    public BillAmountFormModel CurrentBillAmountForm { get; set; } = new();

    // === 設定中心 dashboard 用到的衍生屬性 ===

    // 啟用中銀行帳戶
    public List<BankAccountItem> EnabledBankAccounts
    {
        get { return BankAccounts.Where(x => x.Enabled).ToList(); }
    }

    // 停用中銀行帳戶
    public List<BankAccountItem> DisabledBankAccounts
    {
        get { return BankAccounts.Where(x => !x.Enabled).ToList(); }
    }

    // 啟用中信用卡設定
    public List<CreditSettingItem> EnabledCreditSettings
    {
        get { return CreditSettings.Where(x => x.Enabled).ToList(); }
    }

    // 停用中信用卡設定
    public List<CreditSettingItem> DisabledCreditSettings
    {
        get { return CreditSettings.Where(x => !x.Enabled).ToList(); }
    }

    // 啟用帳戶餘額總和 給 KPI 顯示
    public int TotalEnabledBalance
    {
        get { return EnabledBankAccounts.Sum(x => x.Balance); }
    }

    // 本月待繳已確認金額總和 給 KPI 顯示
    public int CurrentMonthDueAmount
    {
        get
        {
            var total = 0;
            foreach (var bill in CurrentBills)
            {
                if (bill.AmountConfirmed && bill.BillAmount.HasValue)
                {
                    total += bill.BillAmount.Value;
                }
            }
            return total;
        }
    }

    // 帳戶待扣金額 lookup BankAccountId 對應 ConfirmedTotalAmount
    public Dictionary<Guid, int> PendingAmountByAccountId
    {
        get
        {
            var map = new Dictionary<Guid, int>();
            foreach (var impact in BankAccountImpacts)
            {
                map[impact.BankAccountId] = impact.ConfirmedTotalAmount;
            }
            return map;
        }
    }

    // 載入設定頁
    public async Task<IActionResult> OnGetAsync(string? tab, int? historyYear, int? historyMonth, CancellationToken cancellationToken)
    {
        ActiveTab = NormalizeTab(tab);

        // 沒 cookie 跳轉回首頁讓使用者重新從 Discord 進入
        if (!TryGetUserIdFromCookie(out var userId))
        {
            return Redirect("/");
        }

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
                BankCode = TrimOrEmpty(BankAccountForm.BankCode),
                AccountName = TrimOrEmpty(BankAccountForm.AccountName),
                AccountType = TrimOrEmpty(BankAccountForm.AccountType),
                Balance = BankAccountForm.Balance,
                Enabled = Request.Form.ContainsKey("BankAccountForm.Enabled")
            };

            if (string.IsNullOrWhiteSpace(input.BankCode))
            {
                return await BankAccountBadRequestAsync("銀行代碼不可為空白");
            }

            if (string.IsNullOrWhiteSpace(input.AccountName))
            {
                return await BankAccountBadRequestAsync("帳戶名稱不可為空白");
            }

            if (!string.Equals(input.AccountType, "digital", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(input.AccountType, "physical", StringComparison.OrdinalIgnoreCase))
            {
                return await BankAccountBadRequestAsync("帳戶類型只接受 digital 或 physical");
            }

            await settingsPageService.SaveBankAccountAsync(userId, input, cancellationToken);

            if (isAjaxRequest)
            {
                return new JsonResult(new { ok = true });
            }

            return Redirect(BuildRedirectUrl("bankAccounts", historyYear, historyMonth));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return await BankAccountBadRequestAsync(MapServiceErrorMessage(ex, "儲存銀行帳戶失敗 請稍後再試"));
        }
        catch
        {
            return await BankAccountBadRequestAsync("儲存銀行帳戶失敗 請稍後再試");
        }

        // 回傳銀行帳戶表單錯誤
        async Task<IActionResult> BankAccountBadRequestAsync(string message)
        {
            if (isAjaxRequest)
            {
                Response.StatusCode = 400;
                return new JsonResult(new { ok = false, message });
            }

            AlertMessage = message;
            await LoadDataAsync(userId, historyYear, historyMonth, cancellationToken);
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
                BankCode = TrimOrEmpty(CreditSettingForm.BankCode),
                StatementDay = CreditSettingForm.StatementDay,
                PaymentDueDay = CreditSettingForm.PaymentDueDay,
                PaymentBankAccountId = CreditSettingForm.PaymentBankAccountId,
                Enabled = Request.Form.ContainsKey("CreditSettingForm.Enabled")
            };

            if (string.IsNullOrWhiteSpace(input.BankCode))
            {
                return await CreditSettingBadRequestAsync("銀行代碼不可為空白");
            }

            if (input.StatementDay < 1 || input.StatementDay > 31)
            {
                return await CreditSettingBadRequestAsync("結帳日需介於 1 到 31");
            }

            if (input.PaymentDueDay < 1 || input.PaymentDueDay > 31)
            {
                return await CreditSettingBadRequestAsync("繳費日需介於 1 到 31");
            }

            await settingsPageService.SaveCreditSettingAsync(userId, input, cancellationToken);

            if (isAjaxRequest)
            {
                return new JsonResult(new { ok = true });
            }

            return Redirect(BuildRedirectUrl("creditSettings", historyYear, historyMonth));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return await CreditSettingBadRequestAsync(MapServiceErrorMessage(ex, "儲存信用卡設定失敗 請稍後再試"));
        }
        catch
        {
            return await CreditSettingBadRequestAsync("儲存信用卡設定失敗 請稍後再試");
        }

        // 回傳信用卡設定表單錯誤
        async Task<IActionResult> CreditSettingBadRequestAsync(string message)
        {
            if (isAjaxRequest)
            {
                Response.StatusCode = 400;
                return new JsonResult(new { ok = false, message });
            }

            AlertMessage = message;
            await LoadDataAsync(userId, historyYear, historyMonth, cancellationToken);
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
                return await CurrentBillAmountBadRequestAsync("帳單 Id 不可為空白");
            }

            if (CurrentBillAmountForm.BillAmount < 0)
            {
                return await CurrentBillAmountBadRequestAsync("帳單金額不可小於 0");
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
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return await CurrentBillAmountBadRequestAsync(MapServiceErrorMessage(ex, "更新帳單金額失敗"));
        }
        catch
        {
            return await CurrentBillAmountBadRequestAsync("更新帳單金額失敗");
        }

        // 回傳本月帳單金額表單錯誤
        async Task<IActionResult> CurrentBillAmountBadRequestAsync(string message)
        {
            if (isAjaxRequest)
            {
                Response.StatusCode = 400;
                return new JsonResult(new { ok = false, message });
            }

            AlertMessage = message;
            await LoadDataAsync(userId, historyYear, historyMonth, cancellationToken);
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
                return await MarkPaidBadRequestAsync("帳單資料錯誤");
            }

            await creditBillWorkflow.MarkBillPaidAsync(billId, userId, cancellationToken);

            if (isAjaxRequest)
            {
                return new JsonResult(new { ok = true });
            }

            return Redirect(BuildRedirectUrl("currentBills", historyYear, historyMonth));
        }
        catch (UnauthorizedAccessException)
        {
            // billId 不屬於目前登入 user 的 IDOR 嘗試 給明確訊息避免誤導為系統失敗
            return await MarkPaidBadRequestAsync("此帳單不屬於你");
        }
        catch
        {
            return await MarkPaidBadRequestAsync("設定已繳費失敗");
        }

        // 回傳標記繳費表單錯誤
        async Task<IActionResult> MarkPaidBadRequestAsync(string message)
        {
            if (isAjaxRequest)
            {
                Response.StatusCode = 400;
                return new JsonResult(new { ok = false, message });
            }

            AlertMessage = message;
            await LoadDataAsync(userId, historyYear, historyMonth, cancellationToken);
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
        PaymentAccountOptions = pageData.PaymentAccountOptions;
        CurrentBills = pageData.CurrentBills;
        HistoryBills = pageData.HistoryBills;
        HistoryMonthOptions = pageData.HistoryMonthOptions;
        BankAccountImpacts = pageData.BankAccountImpacts;
        HistoryYear = pageData.HistoryYear;
        HistoryMonth = pageData.HistoryMonth;
    }

    // 未登入時回傳對應結果 Ajax 回 401 JSON 一般 Post 導回首頁讓使用者重新從 Discord 進入
    private IActionResult HandleUnauthorized(bool isAjaxRequest)
    {
        if (isAjaxRequest)
        {
            Response.StatusCode = 401;
            return new JsonResult(new { ok = false, message = "尚未登入 請回 Discord 重新開啟網頁連結" });
        }

        return Redirect("/");
    }

    // 判斷是否為 Ajax 請求
    private bool IsAjaxRequest()
    {
        return string.Equals(Request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    }

    // 從已認證的 Claims 取得 userId
    private bool TryGetUserIdFromCookie(out Guid userId)
    {
        userId = Guid.Empty;

        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdText == null)
        {
            return false;
        }

        return Guid.TryParse(userIdText, out userId);
    }

    // nullable string 轉成非 null 並 trim 給 form binding 共用
    private static string TrimOrEmpty(string? value)
    {
        if (value == null)
        {
            return string.Empty;
        }
        return value.Trim();
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
            return $"/Settings?tab={activeTab}&historyYear={historyYear.Value}&historyMonth={historyMonth.Value}";
        }

        return $"/Settings?tab={activeTab}";
    }

    // 將服務層拋出的英文錯誤訊息對應到中文 user-friendly 訊息 找不到對應就用預設訊息
    private static string MapServiceErrorMessage(Exception ex, string defaultMessage)
    {
        var message = ex.Message;
        if (message == null)
        {
            message = string.Empty;
        }

        if (message.Contains("Bank does not exist"))
        {
            return "找不到此銀行代碼 請確認後重試";
        }

        if (message.Contains("Balance cannot be negative"))
        {
            return "餘額不可為負數";
        }

        if (message.Contains("AccountType must be"))
        {
            return "帳戶類型只接受 digital 或 physical";
        }

        if (message.Contains("BankCode is required"))
        {
            return "銀行代碼不可為空白";
        }

        if (message.Contains("AccountName is required"))
        {
            return "帳戶名稱不可為空白";
        }

        if (message.Contains("StatementDay") && message.Contains("PaymentDueDay"))
        {
            return "結帳日與繳費日不可相同";
        }

        // InvalidOperationException 通常已是中文 直接顯示
        if (ex is InvalidOperationException && ContainsChineseCharacter(message))
        {
            return message;
        }

        return defaultMessage;
    }

    // 簡易判斷字串是否含中文字元
    private static bool ContainsChineseCharacter(string text)
    {
        foreach (var ch in text)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF)
            {
                return true;
            }
        }
        return false;
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
