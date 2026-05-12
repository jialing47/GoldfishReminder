namespace GoldfishReminder.Application.Models;

// 設定頁面資料
public class SettingsPageData
{
    public List<BankItem> Banks { get; set; } = new();
    public List<BankAccountItem> BankAccounts { get; set; } = new();
    public List<CreditSettingItem> CreditSettings { get; set; } = new();
    public Dictionary<string, string> BankNameByCode { get; set; } = new();
    public Dictionary<Guid, string> PaymentAccountNameById { get; set; } = new();
    public List<PaymentAccountOptionItem> PaymentAccountOptions { get; set; } = new();
    public List<CurrentBillItem> CurrentBills { get; set; } = new();
    public List<HistoryBillItem> HistoryBills { get; set; } = new();
    public List<HistoryMonthOptionItem> HistoryMonthOptions { get; set; } = new();
    public List<AccountImpactSummary> BankAccountImpacts { get; set; } = new();
    public int HistoryYear { get; set; }
    public int HistoryMonth { get; set; }
}

// 銀行資料
public class BankItem
{
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
}

// 銀行帳戶資料
public class BankAccountItem
{
    public Guid Id { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int Balance { get; set; }
    public DateTimeOffset? BalanceUpdatedAt { get; set; }
}

// 信用卡設定資料
public class CreditSettingItem
{
    public Guid Id { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public int StatementDay { get; set; }
    public int PaymentDueDay { get; set; }
    public Guid? PaymentBankAccountId { get; set; }
    public bool Enabled { get; set; }
}

// 扣款帳戶選項
public class PaymentAccountOptionItem
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

// 本月待繳帳單資料
public class CurrentBillItem
{
    public Guid Id { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public int BillYear { get; set; }
    public int BillMonth { get; set; }
    public int PaymentDueDay { get; set; }
    public int? BillAmount { get; set; }
    public bool AmountConfirmed { get; set; }
    public bool Paid { get; set; }
    public string PaymentAccountName { get; set; } = string.Empty;
}

// 歷史帳單資料
public class HistoryBillItem
{
    public Guid Id { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public int BillYear { get; set; }
    public int BillMonth { get; set; }
    public int? BillAmount { get; set; }
    public bool Paid { get; set; }
    public DateTime DueDate { get; set; }
}

// 歷史帳單年月選項
public class HistoryMonthOptionItem
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

// 帳戶自動扣款影響摘要
public class AccountImpactSummary
{
    public Guid BankAccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public int ConfirmedTotalAmount { get; set; }
    public int UnconfirmedCount { get; set; }
    public List<AccountImpactDetail> Details { get; set; } = new();
}

// 帳戶自動扣款影響明細
public class AccountImpactDetail
{
    public Guid CreditBillId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public int BillYear { get; set; }
    public int BillMonth { get; set; }
    public int PaymentDueDay { get; set; }
    public int? BillAmount { get; set; }
    public bool AmountConfirmed { get; set; }
}
