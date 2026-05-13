using GoldfishReminder.Application;
using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Domain.Entities;
using GoldfishReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldfishReminder.Infrastructure.Services;

// 信用卡帳單流程查詢服務
public class CreditBillDataService : ICreditBillDataService
{
    private readonly AppDbContext dbContext;

    public CreditBillDataService(AppDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    // 取得今日要補建帳單的候選 同月卡在結帳日到繳款日之間補建當月 跨月卡額外補建上個月
    public async Task<IReadOnlyList<TodayBillCandidate>> GetTodaySettingsAsync(DateTime today, CancellationToken cancellationToken = default)
    {
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var isLastDayOfMonth = today.Day == daysInMonth;

        // 撈所有啟用中的信用卡設定 資料量通常幾十筆 client 端做日期判斷
        var enabledSettings = await dbContext.CreditSettings
            .AsNoTracking()
            .Where(x => x.Enabled)
            .ToListAsync(cancellationToken);

        // 上個月年月 跨月卡若今日在繳款日前 本月帳單尚未補建 要補上個月那張
        int prevYear;
        int prevMonth;
        if (today.Month == 1)
        {
            prevYear = today.Year - 1;
            prevMonth = 12;
        }
        else
        {
            prevYear = today.Year;
            prevMonth = today.Month - 1;
        }
        var prevDaysInMonth = DateTime.DaysInMonth(prevYear, prevMonth);

        var candidates = new List<TodayBillCandidate>();

        foreach (var setting in enabledSettings)
        {
            var clampedStatement = Math.Min(setting.StatementDay, daysInMonth);

            if (setting.PaymentDueDay >= setting.StatementDay)
            {
                // 同月卡 今日在結帳日到繳款日之間視為有效
                var clampedDue = Math.Min(setting.PaymentDueDay, daysInMonth);

                if (clampedStatement <= today.Day && today.Day <= clampedDue)
                {
                    candidates.Add(new TodayBillCandidate
                    {
                        Setting = setting,
                        TargetYear = today.Year,
                        TargetMonth = today.Month
                    });
                }
                else if (isLastDayOfMonth && setting.StatementDay > daysInMonth)
                {
                    // Gap G 小月最後一天 結帳日號超過當月天數的卡也要觸發
                    candidates.Add(new TodayBillCandidate
                    {
                        Setting = setting,
                        TargetYear = today.Year,
                        TargetMonth = today.Month
                    });
                }
            }
            else
            {
                // 跨月卡 本月結帳日起就可以補當月帳單 繳款日在下個月
                if (clampedStatement <= today.Day)
                {
                    candidates.Add(new TodayBillCandidate
                    {
                        Setting = setting,
                        TargetYear = today.Year,
                        TargetMonth = today.Month
                    });
                }

                // 跨月卡 今日在本月繳款日前 若上個月帳單尚未建 要 catch-up 上個月
                var clampedDuePrev = Math.Min(setting.PaymentDueDay, daysInMonth);

                if (today.Day <= clampedDuePrev)
                {
                    candidates.Add(new TodayBillCandidate
                    {
                        Setting = setting,
                        TargetYear = prevYear,
                        TargetMonth = prevMonth
                    });
                }
            }
        }

        return candidates;
    }

    // 批次檢查 (userId, bankCode, year, month) 組合已存在的帳單 key
    public async Task<HashSet<(Guid userId, string bankCode, int billYear, int billMonth)>> GetExistingKeysAsync(IReadOnlyCollection<(Guid userId, string bankCode, int billYear, int billMonth)> keys, CancellationToken cancellationToken = default)
    {
        var result = new HashSet<(Guid userId, string bankCode, int billYear, int billMonth)>();

        if (keys.Count == 0)
        {
            return result;
        }

        // 用粗過濾 撈可能的候選 最後在 C# 端比對 tuple 避免多 OR 翻譯
        var userIds = keys.Select(k => k.userId).Distinct().ToList();
        var bankCodes = keys.Select(k => k.bankCode).Distinct().ToList();
        var months = keys.Select(k => new { k.billYear, k.billMonth }).Distinct().ToList();
        var years = months.Select(m => m.billYear).Distinct().ToList();
        var monthNums = months.Select(m => m.billMonth).Distinct().ToList();

        var rows = await dbContext.CreditBills
            .AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .Where(x => bankCodes.Contains(x.BankCode))
            .Where(x => years.Contains(x.BillYear))
            .Where(x => monthNums.Contains(x.BillMonth))
            .Select(x => new { x.UserId, x.BankCode, x.BillYear, x.BillMonth })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var key = (row.UserId, row.BankCode, row.BillYear, row.BillMonth);

            if (keys.Contains(key))
            {
                result.Add(key);
            }
        }

        return result;
    }

    // 取得每日提醒流程資料
    public async Task<DailyContext> GetDailyContextAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var today = TaiwanClock.GetToday(nowUtc);
        var todayYear = today.Year;
        var todayMonth = today.Month;

        var creditSettingList = await dbContext.CreditSettings
            .AsNoTracking()
            .Where(x => x.Enabled)
            .ToListAsync(cancellationToken);

        if (creditSettingList.Count == 0)
        {
            return new DailyContext();
        }

        CollectIds(creditSettingList, out var userIds, out var bankCodes, out var paymentAccountIds);

        var userMap = await GetUsersAsync(userIds, cancellationToken);
        var bankMap = await GetBanksAsync(bankCodes, cancellationToken);
        var paymentAccountMap = await GetAccountsAsync(paymentAccountIds, cancellationToken);
        var creditBillList = await GetBillsAsync(userIds, bankCodes, todayYear, todayMonth, cancellationToken);
        var sentTodayKeys = await GetSentKeysAsync(userIds, nowUtc, cancellationToken);

        return new DailyContext
        {
            CreditSettings = creditSettingList.ToDictionary(x => (x.UserId, x.BankCode)),
            Users = userMap,
            Banks = bankMap,
            PaymentAccounts = paymentAccountMap,
            CreditBills = creditBillList,
            SentTodayKeys = sentTodayKeys
        };
    }

    // 取得單張帳單流程資料
    public async Task<BillContext> GetBillContextAsync(Guid billId, CancellationToken cancellationToken = default)
    {
        var creditBill = await dbContext.CreditBills
            .FirstOrDefaultAsync(x => x.Id == billId, cancellationToken);

        if (creditBill == null)
        {
            return new BillContext();
        }

        var creditSetting = await dbContext.CreditSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == creditBill.UserId && x.BankCode == creditBill.BankCode, cancellationToken);

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == creditBill.UserId, cancellationToken);

        var bank = await dbContext.Banks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BankCode == creditBill.BankCode, cancellationToken);

        PaymentAccountSnapshot? paymentAccount = null;

        if (creditSetting != null && creditSetting.PaymentBankAccountId.HasValue)
        {
            var paymentAccountMap = await GetAccountsAsync(new[] { creditSetting.PaymentBankAccountId.Value }, cancellationToken);
            paymentAccountMap.TryGetValue(creditSetting.PaymentBankAccountId.Value, out paymentAccount);
        }

        return new BillContext
        {
            CreditBill = creditBill,
            CreditSetting = creditSetting,
            User = user,
            Bank = bank,
            PaymentAccount = paymentAccount
        };
    }

    // 取得扣款帳戶流程資料
    public async Task<AccountContext> GetAccountContextAsync(Guid paymentBankAccountId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var today = TaiwanClock.GetToday(nowUtc);
        var todayYear = today.Year;
        var todayMonth = today.Month;

        var creditSettingList = await dbContext.CreditSettings
            .AsNoTracking()
            .Where(x => x.Enabled && x.PaymentBankAccountId == paymentBankAccountId)
            .ToListAsync(cancellationToken);

        if (creditSettingList.Count == 0)
        {
            return new AccountContext();
        }

        CollectIds(creditSettingList, out var userIds, out var bankCodes, out var paymentAccountIds);

        var userMap = await GetUsersAsync(userIds, cancellationToken);
        var bankMap = await GetBanksAsync(bankCodes, cancellationToken);
        var paymentAccountMap = await GetAccountsAsync(paymentAccountIds, cancellationToken);
        var sentTodayKeys = await GetSentKeysAsync(userIds, nowUtc, cancellationToken);

        var creditBillList = await dbContext.CreditBills
            .Where(x => !x.Paid)
            .Where(x => x.BillYear < todayYear || (x.BillYear == todayYear && x.BillMonth <= todayMonth))
            .Join(
                dbContext.CreditSettings.AsNoTracking().Where(x => x.Enabled && x.PaymentBankAccountId == paymentBankAccountId),
                bill => new { bill.UserId, bill.BankCode },
                setting => new { setting.UserId, setting.BankCode },
                (bill, setting) => bill)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new AccountContext
        {
            CreditBills = creditBillList,
            CreditSettings = creditSettingList.ToDictionary(x => (x.UserId, x.BankCode)),
            Users = userMap,
            Banks = bankMap,
            PaymentAccounts = paymentAccountMap,
            SentTodayKeys = sentTodayKeys
        };
    }

    // 停用指定使用者所有啟用中的信用卡設定 直接在 DB 層 bulk update 不 load 到 client
    public async Task DisableAllSettingsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await dbContext.CreditSettings
            .Where(x => x.UserId == userId && x.Enabled)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Enabled, false), cancellationToken);
    }

    // 取得指定使用者所有啟用中的銀行帳戶 join Bank 拿中文名
    public async Task<IReadOnlyList<UserAccountOption>> GetUserAccountsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.BankAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Enabled)
            .Join(
                dbContext.Banks,
                account => account.BankCode,
                bank => bank.BankCode,
                (account, bank) => new UserAccountOption
                {
                    Id = account.Id,
                    BankCode = account.BankCode,
                    BankName = bank.BankName,
                    AccountName = account.AccountName,
                    Balance = account.Balance
                })
            .OrderBy(x => x.BankCode)
            .ThenBy(x => x.AccountName)
            .ToListAsync(cancellationToken);
    }

    // 扣除扣款帳戶餘額
    public async Task DeductBalanceAsync(Guid paymentBankAccountId, int billAmount, CancellationToken cancellationToken = default)
    {
        if (billAmount <= 0)
        {
            return;
        }

        var bankAccount = await dbContext.BankAccounts
            .FirstOrDefaultAsync(x => x.Id == paymentBankAccountId, cancellationToken);

        if (bankAccount == null)
        {
            throw new KeyNotFoundException($"BankAccount not found Id:{paymentBankAccountId}");
        }

        bankAccount.Balance -= billAmount;
        bankAccount.BalanceUpdatedAt = DateTimeOffset.UtcNow;
    }

    // 儲存變更
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // 收集查詢 id
    private static void CollectIds(IReadOnlyCollection<CreditSetting> creditSettingList, out List<Guid> userIds, out List<string> bankCodes, out List<Guid> paymentAccountIds)
    {
        userIds = creditSettingList.Select(x => x.UserId).Distinct().ToList();
        bankCodes = creditSettingList.Select(x => x.BankCode).Distinct().ToList();
        paymentAccountIds = creditSettingList
            .Where(x => x.PaymentBankAccountId.HasValue)
            .Select(x => x.PaymentBankAccountId.Value)
            .Distinct()
            .ToList();
    }

    // 批次取得使用者
    private async Task<Dictionary<Guid, User>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, User>();
        }

        return await dbContext.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
    }

    // 批次取得銀行
    private async Task<Dictionary<string, Bank>> GetBanksAsync(IReadOnlyCollection<string> bankCodes, CancellationToken cancellationToken)
    {
        if (bankCodes.Count == 0)
        {
            return new Dictionary<string, Bank>(StringComparer.Ordinal);
        }

        return await dbContext.Banks
            .AsNoTracking()
            .Where(x => bankCodes.Contains(x.BankCode))
            .ToDictionaryAsync(x => x.BankCode, StringComparer.Ordinal, cancellationToken);
    }

    // 批次取得未繳帳單
    private async Task<IReadOnlyList<CreditBill>> GetBillsAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<string> bankCodes, int billYear, int billMonth, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0 || bankCodes.Count == 0)
        {
            return Array.Empty<CreditBill>();
        }

        return await dbContext.CreditBills
            .Where(x => userIds.Contains(x.UserId))
            .Where(x => bankCodes.Contains(x.BankCode))
            .Where(x => !x.Paid)
            .Where(x => x.BillYear < billYear || (x.BillYear == billYear && x.BillMonth <= billMonth))
            .ToListAsync(cancellationToken);
    }

    // 批次取得扣款帳戶
    private async Task<Dictionary<Guid, PaymentAccountSnapshot>> GetAccountsAsync(IReadOnlyCollection<Guid> paymentAccountIds, CancellationToken cancellationToken)
    {
        if (paymentAccountIds.Count == 0)
        {
            return new Dictionary<Guid, PaymentAccountSnapshot>();
        }

        return await dbContext.BankAccounts
            .AsNoTracking()
            .Where(x => paymentAccountIds.Contains(x.Id))
            .Select(x => new PaymentAccountSnapshot
            {
                Id = x.Id,
                UserId = x.UserId,
                Enabled = x.Enabled,
                Balance = x.Balance,
                AccountName = x.AccountName
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
    }

    // 取得今天已送通知 key 去重交由 DB 做避免重複 row 回傳到記憶體
    private async Task<IReadOnlyList<SentNotificationKey>> GetSentKeysAsync(IReadOnlyCollection<Guid> userIds, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return Array.Empty<SentNotificationKey>();
        }

        TaiwanClock.GetDayRange(nowUtc, out var utcStart, out var utcEnd);

        // 先 Select 成 anonymous 再 Distinct 讓 EF Core 保證翻成 SQL 端 DISTINCT
        return await dbContext.NotificationLogs
            .AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .Where(x => x.Status == "success")
            .Where(x => x.TargetId.HasValue)
            .Where(x => x.SentAt >= utcStart && x.SentAt < utcEnd)
            .Select(x => new { x.UserId, x.NotificationType, TargetId = x.TargetId!.Value })
            .Distinct()
            .Select(x => new SentNotificationKey
            {
                UserId = x.UserId,
                NotificationType = x.NotificationType,
                TargetId = x.TargetId
            })
            .ToListAsync(cancellationToken);
    }
}
