using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GoldfishReminder.Application.Workflows;

// 信用卡帳單流程
public partial class CreditBillWorkflow
{
    private readonly ICreditBillService billService;
    private readonly ICreditBillDataService dataService;
    private readonly NotificationWorkflow notificationWorkflow;
    private readonly ILogger<CreditBillWorkflow> logger;

    // 初始化
    public CreditBillWorkflow(ICreditBillService billService, ICreditBillDataService dataService, NotificationWorkflow notificationWorkflow, ILogger<CreditBillWorkflow> logger)
    {
        this.billService = billService;
        this.dataService = dataService;
        this.notificationWorkflow = notificationWorkflow;
        this.logger = logger;
    }

    // 每日提醒入口
    public async Task RunDailyReminderAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var today = TaiwanClock.GetToday(nowUtc);
        await EnsureBillsAsync(today, cancellationToken);

        var dailyContext = await dataService.GetDailyContextAsync(nowUtc, cancellationToken);

        if (dailyContext.CreditBills.Count == 0)
        {
            return;
        }

        notificationWorkflow.PrimeCache(dailyContext.Users.Keys.ToList(), dailyContext.SentTodayKeys, nowUtc);
        await SendAccountAlertsAsync(dailyContext, today, cancellationToken);

        var disabledUserIds = new HashSet<Guid>();

        foreach (var creditBill in dailyContext.CreditBills)
        {
            if (creditBill.Paid)
            {
                continue;
            }

            if (disabledUserIds.Contains(creditBill.UserId))
            {
                continue;
            }

            if (!TryGetBillContext(dailyContext, creditBill, out var creditSetting, out var user, out var bank))
            {
                continue;
            }

            // 單筆失敗不影響後續 bill 錯誤寫入 stderr 由 GCP Cloud Logging 收集
            try
            {
                var isDisabled = await ProcessBillAsync(creditBill, creditSetting, user, bank, dailyContext.PaymentAccounts, today, false, cancellationToken);

                if (isDisabled)
                {
                    disabledUserIds.Add(creditBill.UserId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RunDailyReminderAsync bill {BillId} failed", creditBill.Id);
            }
        }
    }

    // 帳戶餘額更新後重跑
    public async Task ProcessAccountAsync(Guid paymentBankAccountId, CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var today = TaiwanClock.GetToday(nowUtc);
        var accountContext = await dataService.GetAccountContextAsync(paymentBankAccountId, nowUtc, cancellationToken);

        if (accountContext.CreditBills.Count == 0)
        {
            return;
        }

        notificationWorkflow.PrimeCache(accountContext.Users.Keys.ToList(), accountContext.SentTodayKeys, nowUtc);

        foreach (var creditBill in accountContext.CreditBills)
        {
            if (creditBill.Paid)
            {
                continue;
            }

            if (!TryGetBillContext(accountContext, creditBill, out var creditSetting, out var user, out var bank))
            {
                continue;
            }

            var balance = CheckBalance(creditSetting, GetAmount(creditBill), accountContext.PaymentAccounts);
            var action = DecideAction(creditBill, balance, today);

            if (action == BillActionType.DisableReminder)
            {
                await ExecuteActionAsync(creditBill, user, bank, action, balance, false, cancellationToken);
                return;
            }
        }

        var activeBills = accountContext.CreditBills
            .Where(x => !x.Paid)
            .Where(x => today <= GetDueDate(x))
            .ToList();

        if (activeBills.Count == 0)
        {
            return;
        }

        var accountGroup = BuildGroup(activeBills, accountContext.CreditSettings, accountContext.Banks, accountContext.PaymentAccounts, paymentBankAccountId);

        if (accountGroup == null)
        {
            return;
        }

        if (IsInsufficient(accountGroup))
        {
            await SendAccountAlertAsync(accountContext, accountGroup, cancellationToken);
            return;
        }

        foreach (var creditBill in activeBills)
        {
            if (!TryGetBillContext(accountContext, creditBill, out var creditSetting, out var user, out var bank))
            {
                continue;
            }

            await ProcessBillAsync(creditBill, creditSetting, user, bank, accountContext.PaymentAccounts, today, false, cancellationToken);
        }
    }

    // 更新帳單金額並回傳互動結果
    public async Task<WorkflowAction> ConfirmBillAmountAsync(Guid billId, Guid userId, int billAmount, CancellationToken cancellationToken = default)
    {
        var billContext = await dataService.GetBillContextAsync(billId, cancellationToken);

        if (billContext.CreditBill == null)
        {
            throw new KeyNotFoundException("找不到帳單");
        }

        // 驗帳單屬於指定 user 防 IDOR
        if (billContext.CreditBill.UserId != userId)
        {
            throw new UnauthorizedAccessException($"CreditBill does not belong to the user. BillId:{billId}");
        }

        await billService.UpdateAsync(new UpdateCreditBillRequest
        {
            BillId = billContext.CreditBill.Id,
            BillAmount = billAmount,
            AmountConfirmed = true,
            Paid = billContext.CreditBill.Paid
        }, cancellationToken);

        billContext.CreditBill.BillAmount = billAmount;
        billContext.CreditBill.AmountConfirmed = true;

        if (billContext.CreditSetting == null || billContext.Bank == null)
        {
            return BuildUpdatedReply();
        }

        var balance = CheckBalance(billContext.CreditSetting, billAmount, BuildAccountMap(billContext.PaymentAccount));

        if (!balance.HasPaymentAccount)
        {
            return BuildManualReply(billContext.CreditBill.Id);
        }

        if (billContext.PaymentAccount == null)
        {
            return BuildUpdatedReply();
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var today = TaiwanClock.GetToday(nowUtc);
        var accountContext = await dataService.GetAccountContextAsync(billContext.PaymentAccount.Id, nowUtc, cancellationToken);

        var activeBills = accountContext.CreditBills
            .Where(x => !x.Paid)
            .Where(x => today <= GetDueDate(x))
            .ToList();

        var accountGroup = BuildGroup(activeBills, accountContext.CreditSettings, accountContext.Banks, accountContext.PaymentAccounts, billContext.PaymentAccount.Id);

        if (accountGroup != null && IsInsufficient(accountGroup))
        {
            return BuildInsufficientReply(accountContext, accountGroup.PaymentAccount);
        }

        if (billContext.User != null)
        {
            await ProcessBillAsync(
                billContext.CreditBill,
                billContext.CreditSetting,
                billContext.User,
                billContext.Bank,
                BuildAccountMap(billContext.PaymentAccount),
                today,
                true,
                cancellationToken);
        }

        return BuildUpdatedReply();
    }

    // 手動標記已繳費
    public async Task MarkBillPaidAsync(Guid billId, Guid userId, CancellationToken cancellationToken = default)
    {
        var billContext = await dataService.GetBillContextAsync(billId, cancellationToken);

        if (billContext.CreditBill == null)
        {
            throw new KeyNotFoundException("找不到帳單");
        }

        // 驗帳單屬於指定 user 防 IDOR
        if (billContext.CreditBill.UserId != userId)
        {
            throw new UnauthorizedAccessException($"CreditBill does not belong to the user. BillId:{billId}");
        }

        await billService.UpdateAsync(new UpdateCreditBillRequest
        {
            BillId = billContext.CreditBill.Id,
            BillAmount = billContext.CreditBill.BillAmount,
            AmountConfirmed = billContext.CreditBill.AmountConfirmed,
            Paid = true
        }, cancellationToken);
    }

    // 補建今日帳單
    private async Task<bool> EnsureBillsAsync(DateTime today, CancellationToken cancellationToken)
    {
        var candidates = await dataService.GetTodaySettingsAsync(today, cancellationToken);

        if (candidates.Count == 0)
        {
            return false;
        }

        // 組 (userId, bankCode, year, month) key 檢查哪幾張已存在
        var candidateKeys = candidates
            .Select(c => (c.Setting.UserId, c.Setting.BankCode, c.TargetYear, c.TargetMonth))
            .Distinct()
            .ToList();

        var existingKeys = await dataService.GetExistingKeysAsync(candidateKeys, cancellationToken);
        var createRequests = new List<InsertCreditBillRequest>();
        var enqueued = new HashSet<(Guid userId, string bankCode, int billYear, int billMonth)>();

        foreach (var candidate in candidates)
        {
            var setting = candidate.Setting;
            var key = (setting.UserId, setting.BankCode, candidate.TargetYear, candidate.TargetMonth);

            if (existingKeys.Contains(key))
            {
                continue;
            }

            if (enqueued.Contains(key))
            {
                continue;
            }

            createRequests.Add(new InsertCreditBillRequest
            {
                UserId = setting.UserId,
                BankCode = setting.BankCode,
                BillYear = candidate.TargetYear,
                BillMonth = candidate.TargetMonth,
                StatementDay = setting.StatementDay,
                PaymentDueDay = setting.PaymentDueDay
            });

            enqueued.Add(key);
        }

        if (createRequests.Count == 0)
        {
            return false;
        }

        var insertedBills = await billService.BatchInsertAsync(createRequests, cancellationToken);
        return insertedBills.Count > 0;
    }

    // 處理單張帳單
    private async Task<bool> ProcessBillAsync(CreditBill creditBill, CreditSetting creditSetting, User user, Bank bank, IReadOnlyDictionary<Guid, PaymentAccountSnapshot> paymentAccountMap, DateTime today, bool suppressNotification, CancellationToken cancellationToken)
    {
        if (!creditSetting.Enabled)
        {
            return false;
        }

        var balance = CheckBalance(creditSetting, GetAmount(creditBill), paymentAccountMap);
        var action = DecideAction(creditBill, balance, today);
        return await ExecuteActionAsync(creditBill, user, bank, action, balance, suppressNotification, cancellationToken);
    }

    // 執行帳單動作
    private async Task<bool> ExecuteActionAsync(CreditBill creditBill, User user, Bank bank, BillActionType action, BalanceCheckResult balance, bool suppressNotification, CancellationToken cancellationToken)
    {
        if (suppressNotification && action != BillActionType.AutoPay)
        {
            return false;
        }

        switch (action)
        {
            case BillActionType.None:
                return false;

            case BillActionType.PromptAmountInput:
            case BillActionType.PromptManualPay:
                await notificationWorkflow.SendByActionAsync(creditBill.UserId, GetChannel(user), action, creditBill, bank, null, null, cancellationToken);
                return false;

            case BillActionType.AutoPay:
                if (!creditBill.Paid && balance.PaymentBankAccountId.HasValue)
                {
                    await dataService.DeductBalanceAsync(balance.PaymentBankAccountId.Value, GetAmount(creditBill), cancellationToken);
                    creditBill.Paid = true;
                    await dataService.SaveChangesAsync(cancellationToken);
                }

                return false;

            case BillActionType.DisableReminder:
                // DisableAllSettingsAsync 內部用 ExecuteUpdateAsync 直接 bulk SQL commit
                // 不過 ChangeTracker 所以呼叫端不需要再 SaveChanges
                await dataService.DisableAllSettingsAsync(creditBill.UserId, cancellationToken);
                await notificationWorkflow.SendByActionAsync(creditBill.UserId, GetChannel(user), BillActionType.DisableReminder, creditBill, bank, null, null, cancellationToken);
                return true;

            default:
                return false;
        }
    }

    // 建立更新成功回覆
    private static WorkflowAction BuildUpdatedReply()
    {
        return new WorkflowAction
        {
            ActionType = BillActionType.None,
            Message = MessageBuilder.UpdatedText()
        };
    }

    // 建立手動繳費回覆
    private static WorkflowAction BuildManualReply(Guid billId)
    {
        return new WorkflowAction
        {
            ActionType = BillActionType.PromptManualPay,
            Message = MessageBuilder.UpdatedManualText(),
            Components = MessageBuilder.PromptManualPayButton(billId)
        };
    }

    // 建立不足額回覆
    private static WorkflowAction BuildInsufficientReply(AccountContext accountContext, PaymentAccountSnapshot paymentAccount)
    {
        return new WorkflowAction
        {
            ActionType = BillActionType.AccountInsufficientBalance,
            Message = MessageBuilder.UpdatedInsufficientText(accountContext, paymentAccount)
        };
    }

    // 傳送帳戶層級不足額提醒
    private async Task SendAccountAlertsAsync(DailyContext dailyContext, DateTime today, CancellationToken cancellationToken)
    {
        var activeBills = dailyContext.CreditBills
            .Where(x => !x.Paid)
            .Where(x => today <= GetDueDate(x))
            .ToList();

        if (activeBills.Count == 0)
        {
            return;
        }

        var accountGroups = BuildGroups(activeBills, dailyContext.CreditSettings, dailyContext.Banks, dailyContext.PaymentAccounts);

        foreach (var accountGroup in accountGroups)
        {
            if (!IsInsufficient(accountGroup))
            {
                continue;
            }

            if (!dailyContext.Users.TryGetValue(accountGroup.UserId, out var user))
            {
                continue;
            }

            var accountContext = new AccountContext
            {
                CreditBills = accountGroup.CreditBills,
                CreditSettings = accountGroup.CreditSettings,
                Users = new Dictionary<Guid, User> { [accountGroup.UserId] = user },
                Banks = accountGroup.Banks,
                PaymentAccounts = new Dictionary<Guid, PaymentAccountSnapshot> { [accountGroup.PaymentAccount.Id] = accountGroup.PaymentAccount },
                SentTodayKeys = dailyContext.SentTodayKeys
            };

            // 單一帳戶群組通知失敗不影響其他群組 錯誤寫入 stderr 由 GCP Cloud Logging 收集
            try
            {
                await notificationWorkflow.SendByActionAsync(
                    accountGroup.UserId,
                    GetChannel(user),
                    BillActionType.AccountInsufficientBalance,
                    accountGroup.FirstBill,
                    accountGroup.FirstBank,
                    accountContext,
                    accountGroup.PaymentAccount,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SendAccountAlertsAsync account {AccountId} failed", accountGroup.PaymentAccount.Id);
            }
        }
    }

    // 傳送單一帳戶不足額提醒
    private async Task SendAccountAlertAsync(AccountContext accountContext, AccountGroup accountGroup, CancellationToken cancellationToken)
    {
        if (!accountContext.Users.TryGetValue(accountGroup.UserId, out var user))
        {
            return;
        }

        var alertContext = new AccountContext
        {
            CreditBills = accountGroup.CreditBills,
            CreditSettings = accountGroup.CreditSettings,
            Users = new Dictionary<Guid, User> { [accountGroup.UserId] = user },
            Banks = accountGroup.Banks,
            PaymentAccounts = new Dictionary<Guid, PaymentAccountSnapshot> { [accountGroup.PaymentAccount.Id] = accountGroup.PaymentAccount },
            SentTodayKeys = accountContext.SentTodayKeys
        };

        // 餘額即時變更觸發 繞過當日去重直接發通知給使用者
        await notificationWorkflow.SendByActionAsync(
            accountGroup.UserId,
            GetChannel(user),
            BillActionType.AccountInsufficientBalance,
            accountGroup.FirstBill,
            accountGroup.FirstBank,
            alertContext,
            accountGroup.PaymentAccount,
            cancellationToken,
            bypassDedup: true);
    }
}
