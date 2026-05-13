namespace GoldfishReminder.Application.Models;

//更新信用卡帳單請求 只接受可變欄位 (BillAmount / AmountConfirmed / Paid)
public class UpdateCreditBillRequest
{
    public Guid BillId { get; set; }
    public int? BillAmount { get; set; }
    public bool AmountConfirmed { get; set; }
    public bool Paid { get; set; }
}
