namespace GoldfishReminder.Domain.Entities;

//信用卡帳單
public class CreditBill
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public int BillYear { get; set; }
    public int BillMonth { get; set; }
    public int StatementDay { get; set; }
    public int PaymentDueDay { get; set; }
    public int? BillAmount { get; set; }
    public bool AmountConfirmed { get; set; }
    public bool Paid { get; set; }
}