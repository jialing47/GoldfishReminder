namespace GoldfishReminder.Domain.Entities;

//銀行帳戶
public class BankAccount
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int Balance { get; set; }
    public DateTimeOffset? BalanceUpdatedAt { get; set; }
}