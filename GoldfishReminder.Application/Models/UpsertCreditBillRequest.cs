using System.ComponentModel.DataAnnotations;

namespace GoldfishReminder.Application.Models;

//新增或更新信用卡帳單請求
public class UpsertCreditBillRequest
{
    public Guid? Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [StringLength(10)]
    public string BankCode { get; set; } = string.Empty;

    [Range(2000, 9999)]
    public int? BillYear { get; set; }

    [Range(1, 12)]
    public int? BillMonth { get; set; }

    [Range(1, 31)]
    public int? StatementDay { get; set; }

    [Range(1, 31)]
    public int? PaymentDueDay { get; set; }

    public int? BillAmount { get; set; }
    public bool AmountConfirmed { get; set; } = false;
    public bool Paid { get; set; } = false;
}