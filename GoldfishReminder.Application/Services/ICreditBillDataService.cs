using GoldfishReminder.Application.Models;
using GoldfishReminder.Domain.Entities;

// 信用卡帳單流程查詢服務介面
public interface ICreditBillDataService
{
    // 取得今日要補建帳單的候選 包含同月卡與跨月卡 catch-up
    Task<IReadOnlyList<TodayBillCandidate>> GetTodaySettingsAsync(DateTime today, CancellationToken cancellationToken = default);

    // 取得指定 (userId, bankCode, year, month) 集合中已存在帳單的 key
    Task<HashSet<(Guid userId, string bankCode, int billYear, int billMonth)>> GetExistingKeysAsync(IReadOnlyCollection<(Guid userId, string bankCode, int billYear, int billMonth)> keys, CancellationToken cancellationToken = default);

    // 取得每日提醒流程資料
    Task<DailyContext> GetDailyContextAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    // 取得單張帳單流程資料
    Task<BillContext> GetBillContextAsync(Guid billId, CancellationToken cancellationToken = default);

    // 取得扣款帳戶流程資料
    Task<AccountContext> GetAccountContextAsync(Guid paymentBankAccountId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    // 取得指定使用者所有啟用中的銀行帳戶 供 Discord 指令選擇
    Task<IReadOnlyList<UserAccountOption>> GetUserAccountsAsync(Guid userId, CancellationToken cancellationToken = default);

    // 停用指定使用者所有啟用中的信用卡設定
    Task DisableAllSettingsAsync(Guid userId, CancellationToken cancellationToken = default);

    // 扣除扣款帳戶餘額
    Task DeductBalanceAsync(Guid paymentBankAccountId, int billAmount, CancellationToken cancellationToken = default);

    // 儲存變更
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
