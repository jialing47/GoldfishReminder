using GoldfishReminder.Application.Models;
using GoldfishReminder.Domain.Entities;

namespace GoldfishReminder.Application.Workflows;

// CreditBillWorkflow 的私有 helper 與內部型別拆檔 邏輯完全不變
public partial class CreditBillWorkflow
{
    // 建立帳戶群組
    private static List<AccountGroup> BuildGroups(IReadOnlyList<CreditBill> creditBills, IReadOnlyDictionary<(Guid userId, string bankCode), CreditSetting> creditSettings, IReadOnlyDictionary<string, Bank> banks, IReadOnlyDictionary<Guid, PaymentAccountSnapshot> paymentAccounts)
    {
        var groupMap = new Dictionary<Guid, AccountGroup>();

        foreach (var creditBill in creditBills)
        {

            if (creditBill.Paid)
            {
                continue;
            }

            if (!creditSettings.TryGetValue((creditBill.UserId, creditBill.BankCode), out var creditSetting))
            {
                continue;
            }

            if (!creditSetting.PaymentBankAccountId.HasValue)
            {
                continue;
            }

            var paymentBankAccountId = creditSetting.PaymentBankAccountId.Value;

            if (!paymentAccounts.TryGetValue(paymentBankAccountId, out var paymentAccount))
            {
                continue;
            }

            if (!banks.TryGetValue(creditBill.BankCode, out var bank))
            {
                continue;
            }

            if (!groupMap.TryGetValue(paymentBankAccountId, out var accountGroup))
            {
                accountGroup = new AccountGroup
                {
                    UserId = creditBill.UserId,
                    PaymentAccount = paymentAccount,
                    FirstBill = creditBill,
                    FirstBank = bank
                };

                groupMap[paymentBankAccountId] = accountGroup;
            }

            accountGroup.CreditBills.Add(creditBill);
            accountGroup.CreditSettings[(creditBill.UserId, creditBill.BankCode)] = creditSetting;
            accountGroup.Banks[creditBill.BankCode] = bank;
        }

        return groupMap.Values.ToList();
    }

    // 建立單一帳戶群組
    private static AccountGroup? BuildGroup(IReadOnlyList<CreditBill> creditBills, IReadOnlyDictionary<(Guid userId, string bankCode), CreditSetting> creditSettings, IReadOnlyDictionary<string, Bank> banks, IReadOnlyDictionary<Guid, PaymentAccountSnapshot> paymentAccounts, Guid paymentBankAccountId)
    {
        if (!paymentAccounts.TryGetValue(paymentBankAccountId, out var paymentAccount))
        {
            return null;
        }

        AccountGroup? accountGroup = null;

        foreach (var creditBill in creditBills)
        {
            if (creditBill.Paid)
            {
                continue;
            }

            if (!creditSettings.TryGetValue((creditBill.UserId, creditBill.BankCode), out var creditSetting))
            {
                continue;
            }

            if (!creditSetting.PaymentBankAccountId.HasValue || creditSetting.PaymentBankAccountId.Value != paymentBankAccountId)
            {
                continue;
            }

            if (!banks.TryGetValue(creditBill.BankCode, out var bank))
            {
                continue;
            }

            if (accountGroup == null)
            {
                accountGroup = new AccountGroup
                {
                    UserId = creditBill.UserId,
                    PaymentAccount = paymentAccount,
                    FirstBill = creditBill,
                    FirstBank = bank
                };
            }

            accountGroup.CreditBills.Add(creditBill);
            accountGroup.CreditSettings[(creditBill.UserId, creditBill.BankCode)] = creditSetting;
            accountGroup.Banks[creditBill.BankCode] = bank;
        }

        return accountGroup;
    }

    // 判斷帳戶群組是否不足額
    private static bool IsInsufficient(AccountGroup accountGroup)
    {
        if (!accountGroup.PaymentAccount.Enabled)
        {
            return false;
        }

        var totalAmount = 0;

        foreach (var creditBill in accountGroup.CreditBills)
        {
            if (creditBill.AmountConfirmed && creditBill.BillAmount.HasValue)
            {
                totalAmount += creditBill.BillAmount.Value;
            }
        }

        return totalAmount > accountGroup.PaymentAccount.Balance;
    }

    // 取得流程所需物件
    private static bool TryGetBillContext(DailyContext dailyContext, CreditBill creditBill, out CreditSetting creditSetting, out User user, out Bank bank)
    {
        var hasSetting = dailyContext.CreditSettings.TryGetValue((creditBill.UserId, creditBill.BankCode), out creditSetting);
        var hasUser = dailyContext.Users.TryGetValue(creditBill.UserId, out user);
        var hasBank = dailyContext.Banks.TryGetValue(creditBill.BankCode, out bank);
        return hasSetting && hasUser && hasBank;
    }

    // 取得流程所需物件
    private static bool TryGetBillContext(AccountContext accountContext, CreditBill creditBill, out CreditSetting creditSetting, out User user, out Bank bank)
    {
        var hasSetting = accountContext.CreditSettings.TryGetValue((creditBill.UserId, creditBill.BankCode), out creditSetting);
        var hasUser = accountContext.Users.TryGetValue(creditBill.UserId, out user);
        var hasBank = accountContext.Banks.TryGetValue(creditBill.BankCode, out bank);
        return hasSetting && hasUser && hasBank;
    }

    // 判斷帳單動作
    private static BillActionType DecideAction(CreditBill creditBill, BalanceCheckResult balance, DateTime today)
    {
        var dueDate = GetDueDate(creditBill);
        var statementDate = GetStatementDate(creditBill);

        if (today > dueDate.AddDays(14))
        {
            return BillActionType.DisableReminder;
        }

        // 到繳費日或過繳費日且足額都要自動扣款 允許 dueDate 後 14 天內補扣
        if (today >= dueDate
            && creditBill.AmountConfirmed
            && balance.HasPaymentAccount
            && balance.PaymentAccountEnabled
            && balance.IsSufficient)
        {
            return BillActionType.AutoPay;
        }

        // 過繳費日就停止所有提醒 但扣款條件已在上面處理過
        if (today > dueDate)
        {
            return BillActionType.None;
        }

        if (!creditBill.AmountConfirmed)
        {
            if (today >= statementDate)
            {
                return BillActionType.PromptAmountInput;
            }

            return BillActionType.None;
        }

        if (!balance.HasPaymentAccount || !balance.PaymentAccountEnabled)
        {
            return BillActionType.PromptManualPay;
        }

        if (!balance.IsSufficient)
        {
            return BillActionType.None;
        }

        return BillActionType.None;
    }

    // 判斷餘額
    private static BalanceCheckResult CheckBalance(CreditSetting creditSetting, int amount, IReadOnlyDictionary<Guid, PaymentAccountSnapshot> paymentAccounts)
    {
        var balance = new BalanceCheckResult();

        if (!creditSetting.PaymentBankAccountId.HasValue)
        {
            return balance;
        }

        var paymentBankAccountId = creditSetting.PaymentBankAccountId.Value;

        if (!paymentAccounts.TryGetValue(paymentBankAccountId, out var paymentAccount))
        {
            return balance;
        }

        balance.HasPaymentAccount = true;
        balance.PaymentAccountEnabled = paymentAccount.Enabled;
        balance.CurrentBalance = paymentAccount.Balance;
        balance.PaymentBankAccountId = paymentAccount.Id;
        balance.PaymentAccountName = paymentAccount.AccountName;

        if (paymentAccount.Enabled && paymentAccount.Balance >= amount)
        {
            balance.IsSufficient = true;
        }

        return balance;
    }

    // 建立單一帳戶 map
    private static IReadOnlyDictionary<Guid, PaymentAccountSnapshot> BuildAccountMap(PaymentAccountSnapshot? paymentAccount)
    {
        var paymentAccounts = new Dictionary<Guid, PaymentAccountSnapshot>();

        if (paymentAccount != null)
        {
            paymentAccounts[paymentAccount.Id] = paymentAccount;
        }

        return paymentAccounts;
    }

    // 計算結帳日 在帳單月份內 超過當月天數 clamp 到最後一天
    private static DateTime GetStatementDate(CreditBill creditBill)
    {
        var daysInMonth = DateTime.DaysInMonth(creditBill.BillYear, creditBill.BillMonth);
        var clampedDay = Math.Min(creditBill.StatementDay, daysInMonth);
        return new DateTime(creditBill.BillYear, creditBill.BillMonth, clampedDay);
    }

    // 計算繳款日 繳款日號小於結帳日號視為跨月 落在帳單月份的下個月
    private static DateTime GetDueDate(CreditBill creditBill)
    {
        var year = creditBill.BillYear;
        var month = creditBill.BillMonth;

        if (creditBill.PaymentDueDay < creditBill.StatementDay)
        {
            month++;
            if (month > 12)
            {
                month = 1;
                year++;
            }
        }

        var daysInMonth = DateTime.DaysInMonth(year, month);
        var clampedDay = Math.Min(creditBill.PaymentDueDay, daysInMonth);
        return new DateTime(year, month, clampedDay);
    }

    // 取得帳單金額
    private static int GetAmount(CreditBill creditBill)
    {
        if (creditBill.BillAmount.HasValue)
        {
            return creditBill.BillAmount.Value;
        }

        return 0;
    }

    // 取得通知頻道
    private static string GetChannel(User user)
    {
        if (string.IsNullOrWhiteSpace(user.DiscordPrivateChannelId))
        {
            throw new InvalidOperationException("找不到頻道");
        }

        return user.DiscordPrivateChannelId;
    }

    // 帳戶群組
    private sealed class AccountGroup
    {
        public Guid UserId { get; set; }
        public PaymentAccountSnapshot PaymentAccount { get; set; } = new PaymentAccountSnapshot();
        public CreditBill FirstBill { get; set; } = new CreditBill();
        public Bank FirstBank { get; set; } = new Bank();
        public List<CreditBill> CreditBills { get; } = new List<CreditBill>();
        public Dictionary<(Guid userId, string bankCode), CreditSetting> CreditSettings { get; } = new Dictionary<(Guid userId, string bankCode), CreditSetting>();
        public Dictionary<string, Bank> Banks { get; } = new Dictionary<string, Bank>(StringComparer.Ordinal);
    }
}
