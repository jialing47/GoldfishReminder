namespace GoldfishReminder.Application.Models;

// 銀行帳戶輸入模型
public class BankAccountInputModel
{
    public Guid? Id { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = "digital";
    public string? Balance { get; set; }
    public bool Enabled { get; set; }
}

// 信用卡設定輸入模型
public class CreditSettingInputModel
{
    public Guid? Id { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public int StatementDay { get; set; }
    public int PaymentDueDay { get; set; }
    public Guid? PaymentBankAccountId { get; set; }
    public bool Enabled { get; set; }
}

// 本月帳單金額更新輸入
public class UpdateBillAmountInput
{
    public Guid BillId { get; set; }
    public int BillAmount { get; set; }
}
