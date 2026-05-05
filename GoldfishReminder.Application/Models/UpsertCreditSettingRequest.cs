using System.ComponentModel.DataAnnotations;

namespace GoldfishReminder.Application.Models;

//新增或更新信用卡設定請求
public class UpsertCreditSettingRequest
{
    public Guid? Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [StringLength(10)]
    public string BankCode { get; set; } = string.Empty;

    [Range(1, 31)]
    public int StatementDay { get; set; }

    [Range(1, 31)]
    public int PaymentDueDay { get; set; }

    public Guid? PaymentBankAccountId { get; set; }
    public bool Enabled { get; set; } = true;
}