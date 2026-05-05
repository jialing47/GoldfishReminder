using GoldfishReminder.Domain.Entities;

namespace GoldfishReminder.Application.Models;

// 餘額檢查結果
public class BalanceCheckResult
{
    public bool HasPaymentAccount { get; set; }
    public bool PaymentAccountEnabled { get; set; }
    public bool IsSufficient { get; set; }
    public int CurrentBalance { get; set; }
    public Guid? PaymentBankAccountId { get; set; }
    public string? PaymentAccountName { get; set; }
}

// 扣款帳戶快照
public class PaymentAccountSnapshot
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public bool Enabled { get; set; }
    public int Balance { get; set; }
    public string AccountName { get; set; } = string.Empty;
}

// 已發送通知快照
public class SentNotificationKey
{
    public Guid UserId { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
}

// 每日提醒流程資料
public class DailyContext
{
    // 信用卡設定 key 為 userId + bankCode
    public Dictionary<(Guid userId, string bankCode), CreditSetting> CreditSettings { get; set; } = new Dictionary<(Guid userId, string bankCode), CreditSetting>();
    // 使用者資料
    public Dictionary<Guid, User> Users { get; set; } = new Dictionary<Guid, User>();
    // 銀行資料
    public Dictionary<string, Bank> Banks { get; set; } = new Dictionary<string, Bank>(StringComparer.Ordinal);
    // 未繳帳單
    public IReadOnlyList<CreditBill> CreditBills { get; set; } = Array.Empty<CreditBill>();
    // 扣款帳戶資料
    public Dictionary<Guid, PaymentAccountSnapshot> PaymentAccounts { get; set; } = new Dictionary<Guid, PaymentAccountSnapshot>();
    // 今日已發送通知 去重用
    public IReadOnlyList<SentNotificationKey> SentTodayKeys { get; set; } = Array.Empty<SentNotificationKey>();
}

// 單張帳單流程資料
public class BillContext
{
    public CreditBill? CreditBill { get; set; }
    public CreditSetting? CreditSetting { get; set; }
    public User? User { get; set; }
    public Bank? Bank { get; set; }
    public PaymentAccountSnapshot? PaymentAccount { get; set; }
}

// 扣款帳戶餘額變更流程資料
public class AccountContext
{
    public IReadOnlyList<CreditBill> CreditBills { get; set; } = Array.Empty<CreditBill>();
    public Dictionary<(Guid userId, string bankCode), CreditSetting> CreditSettings { get; set; } = new();
    public Dictionary<Guid, User> Users { get; set; } = new();
    public Dictionary<string, Bank> Banks { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, PaymentAccountSnapshot> PaymentAccounts { get; set; } = new();
    public IReadOnlyList<SentNotificationKey> SentTodayKeys { get; set; } = Array.Empty<SentNotificationKey>();
}

// 今日需要建帳單的候選 夾帶目標年月 供跨月卡 catch-up 指定上一個月
public class TodayBillCandidate
{
    public CreditSetting Setting { get; set; } = null!;
    public int TargetYear { get; set; }
    public int TargetMonth { get; set; }
}
