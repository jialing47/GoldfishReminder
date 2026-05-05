using System.ComponentModel.DataAnnotations;

namespace GoldfishReminder.Application.Models;

//新增或更新銀行帳戶請求
public class UpsertBankAccountRequest
{
    public Guid? Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [StringLength(10)]
    public string BankCode { get; set; } = string.Empty;

    [Required]
    public string AccountName { get; set; } = string.Empty;

    [Required]
    public string AccountType { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
    public int? Balance { get; set; }
    public DateTimeOffset? BalanceUpdatedAt { get; set; }
}