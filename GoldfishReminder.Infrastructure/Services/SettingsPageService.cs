using GoldfishReminder.Application;
using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Application.Workflows;
using GoldfishReminder.Domain.Entities;
using GoldfishReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldfishReminder.Infrastructure.Services;

//設定頁面服務實作
public class SettingsPageService : ISettingsPageService
{
    private readonly AppDbContext dbContext; // EF DbContext
    private readonly IBankAccountService bankAccountService; // Bank account domain service
    private readonly ICreditSettingService creditSettingService; // Credit setting domain service
    private readonly CreditBillWorkflow creditBillWorkflow; // Bill workflow for unified decision/action
    private readonly INotificationSender notificationSender; // 餘額/手動繳費警示主動推送私人頻道

    //初始化設定頁面服務相依物件
    public SettingsPageService(
        AppDbContext dbContext,
        IBankAccountService bankAccountService,
        ICreditSettingService creditSettingService,
        CreditBillWorkflow creditBillWorkflow,
        INotificationSender notificationSender)
    {
        this.dbContext = dbContext;
        this.bankAccountService = bankAccountService;
        this.creditSettingService = creditSettingService;
        this.creditBillWorkflow = creditBillWorkflow;
        this.notificationSender = notificationSender;
    }

    //載入設定頁面資料
    public async Task<SettingsPageData> GetPageDataAsync(Guid userId, int? historyYear = null, int? historyMonth = null, CancellationToken cancellationToken = default)
    {
        var now = TaiwanClock.GetToday();

        var selectedHistoryYear = now.Year;
        if (historyYear.HasValue)
        {
            selectedHistoryYear = historyYear.Value;
        }

        var selectedHistoryMonth = now.Month;
        if (historyMonth.HasValue)
        {
            selectedHistoryMonth = historyMonth.Value;
        }

        var banks = await dbContext.Banks
            .AsNoTracking()
            .OrderBy(x => x.BankCode)
            .Select(x => new BankItem
            {
                BankCode = x.BankCode,
                BankName = x.BankName
            })
            .ToListAsync(cancellationToken);

        var bankAccounts = await dbContext.BankAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Enabled)
            .ThenBy(x => x.BankCode)
            .ThenBy(x => x.AccountName)
            .Select(x => new BankAccountItem
            {
                Id = x.Id,
                BankCode = x.BankCode,
                AccountName = x.AccountName,
                AccountType = x.AccountType,
                Enabled = x.Enabled,
                Balance = x.Balance,
                BalanceUpdatedAt = x.BalanceUpdatedAt
            })
            .ToListAsync(cancellationToken);

        var creditSettings = await dbContext.CreditSettings
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.BankCode)
            .Select(x => new CreditSettingItem
            {
                Id = x.Id,
                BankCode = x.BankCode,
                StatementDay = x.StatementDay,
                PaymentDueDay = x.PaymentDueDay,
                PaymentBankAccountId = x.PaymentBankAccountId,
                Enabled = x.Enabled
            })
            .ToListAsync(cancellationToken);

        var bankNameByCode = banks.ToDictionary(x => x.BankCode, x => x.BankName);
        var paymentAccountNameById = bankAccounts.ToDictionary(x => x.Id, x => x.AccountName);

        var paymentAccountOptions = bankAccounts
            .Where(x => x.Enabled)
            .OrderBy(x => x.BankCode)
            .ThenBy(x => x.AccountName)
            .Select(x => new PaymentAccountOptionItem
            {
                Id = x.Id,
                DisplayName = $"{x.AccountName} ({x.BankCode}, {GetAccountTypeText(x.AccountType)})"
            })
            .ToList();

        var allBills = await dbContext.CreditBills
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.BillYear)
            .ThenByDescending(x => x.BillMonth)
            .ThenBy(x => x.PaymentDueDay)
            .Select(x => new BillSnapshot
            {
                Id = x.Id,
                BankCode = x.BankCode,
                BillYear = x.BillYear,
                BillMonth = x.BillMonth,
                StatementDay = x.StatementDay,
                PaymentDueDay = x.PaymentDueDay,
                BillAmount = x.BillAmount,
                AmountConfirmed = x.AmountConfirmed,
                Paid = x.Paid
            })
            .ToListAsync(cancellationToken);

        var paymentAccountByBankCode = creditSettings
            .GroupBy(x => x.BankCode)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(y => y.Enabled).Select(y => y.PaymentBankAccountId).FirstOrDefault());

        // 待繳帳單顯示條件 未付款 且 未逾期 DueDate 大於等於今天 跨月卡的窗口期也會被自然涵蓋
        var currentBills = allBills
            .Where(x => !x.Paid)
            .Select(x => new CurrentBillItem
            {
                Id = x.Id,
                BankCode = x.BankCode,
                BankName = bankNameByCode.GetValueOrDefault(x.BankCode, string.Empty),
                DueDate = CreditBillSchedule.CalculateDueDate(x.BillYear, x.BillMonth, x.StatementDay, x.PaymentDueDay),
                BillAmount = x.BillAmount,
                AmountConfirmed = x.AmountConfirmed,
                PaymentAccountName = BuildPaymentAccountName(x.BankCode, paymentAccountByBankCode, paymentAccountNameById)
            })
            .Where(x => x.DueDate.Date >= now.Date)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.BankCode)
            .ToList();

        var historyMonthOptions = allBills
            .Select(x => new { x.BillYear, x.BillMonth })
            .Distinct()
            .OrderByDescending(x => x.BillYear)
            .ThenByDescending(x => x.BillMonth)
            .Select(x => new HistoryMonthOptionItem
            {
                Year = x.BillYear,
                Month = x.BillMonth
            })
            .ToList();

        if (historyMonthOptions.Count > 0)
        {
            var selectedExists = historyMonthOptions.Any(x => x.Year == selectedHistoryYear && x.Month == selectedHistoryMonth);
            if (!selectedExists)
            {
                selectedHistoryYear = historyMonthOptions[0].Year;
                selectedHistoryMonth = historyMonthOptions[0].Month;
            }
        }

        // 歷史帳單依使用者選擇的 BillYear/BillMonth 篩 並帶 DueDate 給前端判斷三態
        var historyBills = allBills
            .Where(x => x.BillYear == selectedHistoryYear && x.BillMonth == selectedHistoryMonth)
            .Select(x => new HistoryBillItem
            {
                BankCode = x.BankCode,
                BankName = bankNameByCode.GetValueOrDefault(x.BankCode, string.Empty),
                BillAmount = x.BillAmount,
                Paid = x.Paid,
                DueDate = CreditBillSchedule.CalculateDueDate(x.BillYear, x.BillMonth, x.StatementDay, x.PaymentDueDay)
            })
            .OrderBy(x => x.BankCode)
            .ToList();

        var bankAccountImpacts = BuildBankAccountImpacts(bankAccounts, creditSettings, allBills, bankNameByCode);

        return new SettingsPageData
        {
            Banks = banks,
            BankAccounts = bankAccounts,
            CreditSettings = creditSettings,
            BankNameByCode = bankNameByCode,
            PaymentAccountOptions = paymentAccountOptions,
            PaymentAccountNameById = paymentAccountNameById,
            CurrentBills = currentBills,
            HistoryBills = historyBills,
            HistoryMonthOptions = historyMonthOptions,
            BankAccountImpacts = bankAccountImpacts,
            HistoryYear = selectedHistoryYear,
            HistoryMonth = selectedHistoryMonth
        };
    }

    //儲存銀行帳戶
    public async Task SaveBankAccountAsync(Guid userId, BankAccountInputModel input, CancellationToken cancellationToken = default)
    {
        var bankCode = input.BankCode.Trim();
        var accountName = input.AccountName.Trim();
        var accountType = input.AccountType.Trim().ToLowerInvariant();
        var parsedBalance = ParseNullableInt(input.Balance);

        var balanceToSave = 0;
        DateTimeOffset? balanceUpdatedAt = null;

        if (parsedBalance.HasValue)
        {
            balanceToSave = parsedBalance.Value;
            balanceUpdatedAt = DateTimeOffset.UtcNow;
        }
        else if (input.Id.HasValue)
        {
            var existingBankAccount = await dbContext.BankAccounts
                .AsNoTracking()
                .Where(x => x.Id == input.Id.Value && x.UserId == userId)
                .Select(x => new { x.Balance })
                .FirstOrDefaultAsync(cancellationToken);

            if (existingBankAccount == null)
            {
                throw new InvalidOperationException("找不到要編輯的銀行帳戶，請重新整理後再試。");
            }

            balanceToSave = existingBankAccount.Balance;
        }

        var request = new UpsertBankAccountRequest
        {
            Id = input.Id,
            UserId = userId,
            BankCode = bankCode,
            AccountName = accountName,
            AccountType = accountType,
            Enabled = input.Enabled,
            Balance = balanceToSave,
            BalanceUpdatedAt = balanceUpdatedAt
        };

        var savedBankAccount = await bankAccountService.UpsertAsync(request, cancellationToken);

        //只有在使用者這次有明確輸入餘額時，才視為餘額異動並重跑關聯帳單判斷
        if (parsedBalance.HasValue)
        {
            await creditBillWorkflow.ProcessAccountAsync(savedBankAccount.Id, cancellationToken);
        }
    }

    //儲存信用卡設定
    public async Task SaveCreditSettingAsync(Guid userId, CreditSettingInputModel input, CancellationToken cancellationToken = default)
    {
        var request = new UpsertCreditSettingRequest
        {
            Id = input.Id,
            UserId = userId,
            BankCode = input.BankCode.Trim(),
            StatementDay = input.StatementDay,
            PaymentDueDay = input.PaymentDueDay,
            PaymentBankAccountId = input.PaymentBankAccountId,
            Enabled = input.Enabled
        };

        await creditSettingService.UpsertAsync(request, cancellationToken);
    }

    //更新本月待繳帳單金額並立即重跑帳單流程
    public async Task UpdateBillAmountAsync(Guid userId, UpdateBillAmountInput input, CancellationToken cancellationToken = default)
    {
        if (input.BillId == Guid.Empty)
        {
            throw new ArgumentException("BillId is required");
        }

        if (input.BillAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input.BillAmount), "BillAmount cannot be negative");
        }

        var now = TaiwanClock.GetToday();
        var editableBill = await dbContext.CreditBills
            .AsNoTracking()
            .Where(x => x.Id == input.BillId && x.UserId == userId)
            .Where(x => x.BillYear == now.Year && x.BillMonth == now.Month)
            .Where(x => !x.Paid)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (editableBill == null)
        {
            throw new InvalidOperationException("找不到可更新的本月待繳帳單");
        }

        var action = await creditBillWorkflow.ConfirmBillAmountAsync(input.BillId, userId, input.BillAmount, cancellationToken);

        await DispatchActionMessageAsync(userId, action, cancellationToken);
    }

    //帳單金額確認後若 action 屬於警示類型 主動發訊息至使用者私人頻道 對齊 Discord modal 路徑體驗
    private async Task DispatchActionMessageAsync(Guid userId, WorkflowAction action, CancellationToken cancellationToken)
    {
        if (action.ActionType != BillActionType.PromptManualPay
            && action.ActionType != BillActionType.AccountInsufficientBalance)
        {
            return;
        }

        var channelId = await dbContext.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => x.DiscordPrivateChannelId)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(channelId))
        {
            return;
        }

        await notificationSender.SendAsync(channelId, action.Message, action.Components, cancellationToken);
    }

    //建立扣款帳戶名稱
    private static string BuildPaymentAccountName(string bankCode, Dictionary<string, Guid?> paymentAccountByBankCode, Dictionary<Guid, string> paymentAccountNameById)
    {
        if (!paymentAccountByBankCode.TryGetValue(bankCode, out var paymentAccountId) || !paymentAccountId.HasValue)
        {
            return string.Empty;
        }

        return paymentAccountNameById.GetValueOrDefault(paymentAccountId.Value, string.Empty);
    }

    //建立帳戶扣款影響資料
    private static List<AccountImpactSummary> BuildBankAccountImpacts(List<BankAccountItem> bankAccounts, List<CreditSettingItem> creditSettings, List<BillSnapshot> allBills, Dictionary<string, string> bankNameByCode)
    {
        var today = TaiwanClock.GetToday();

        var enabledSettings = creditSettings
            .Where(x => x.Enabled && x.PaymentBankAccountId.HasValue)
            .GroupBy(x => new { x.BankCode, PaymentBankAccountId = x.PaymentBankAccountId!.Value })
            .Select(x => x.First())
            .ToList();

        var impactList = new List<AccountImpactSummary>();

        foreach (var bankAccount in bankAccounts)
        {
            var relatedBankCodes = enabledSettings
                .Where(x => x.PaymentBankAccountId == bankAccount.Id)
                .Select(x => x.BankCode)
                .Distinct()
                .ToList();

            if (relatedBankCodes.Count == 0)
            {
                continue;
            }

            var relatedBills = allBills
                .Where(x => relatedBankCodes.Contains(x.BankCode) && !x.Paid)
                .Where(x => x.BillYear == today.Year && x.BillMonth == today.Month)
                .OrderBy(x => x.PaymentDueDay)
                .ThenBy(x => x.BankCode)
                .ToList();

            if (relatedBills.Count == 0)
            {
                continue;
            }

            var details = relatedBills
                .Select(x => new AccountImpactDetail
                {
                    CreditBillId = x.Id,
                    BankCode = x.BankCode,
                    BankName = bankNameByCode.GetValueOrDefault(x.BankCode, string.Empty),
                    BillYear = x.BillYear,
                    BillMonth = x.BillMonth,
                    PaymentDueDay = x.PaymentDueDay,
                    BillAmount = x.BillAmount,
                    AmountConfirmed = x.AmountConfirmed
                })
                .ToList();

            impactList.Add(new AccountImpactSummary
            {
                BankAccountId = bankAccount.Id,
                AccountName = bankAccount.AccountName,
                ConfirmedTotalAmount = relatedBills.Where(x => x.AmountConfirmed && x.BillAmount.HasValue).Sum(x => x.BillAmount!.Value),
                UnconfirmedCount = relatedBills.Count(x => !x.AmountConfirmed),
                Details = details
            });
        }

        return impactList;
    }


    //帳單快照
    private sealed class BillSnapshot
    {
        public Guid Id { get; set; }
        public string BankCode { get; set; } = string.Empty;
        public int BillYear { get; set; }
        public int BillMonth { get; set; }
        public int StatementDay { get; set; }
        public int PaymentDueDay { get; set; }
        public int? BillAmount { get; set; }
        public bool AmountConfirmed { get; set; }
        public bool Paid { get; set; }
    }

    //帳戶類型顯示文字
    private static string GetAccountTypeText(string? accountType)
    {
        var rawAccountType = accountType;
        if (rawAccountType == null)
        {
            rawAccountType = string.Empty;
        }
        var normalized = rawAccountType.Trim().ToLowerInvariant();

        if (normalized == "digital")
        {
            return "數位帳戶";
        }

        if (normalized == "physical")
        {
            return "實體帳戶";
        }

        return rawAccountType;
    }

    //解析可空整數
    private static int? ParseNullableInt(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var normalized = input
            .Trim()
            .Replace(",", string.Empty) //半形逗號
            .Replace("\uFF0C", string.Empty) //全形逗號
            .Replace(" ", string.Empty);

        if (int.TryParse(normalized, out var value))
        {
            return value;
        }

        return null;
    }

}
