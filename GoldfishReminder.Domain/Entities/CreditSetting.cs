namespace GoldfishReminder.Domain.Entities;

//信用卡設定
public class CreditSetting
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public int StatementDay { get; set; }
    public int PaymentDueDay { get; set; }
    public Guid? PaymentBankAccountId { get; set; }
    public BankAccount? PaymentBankAccount { get; set; }
    public bool Enabled { get; set; } = true;

}