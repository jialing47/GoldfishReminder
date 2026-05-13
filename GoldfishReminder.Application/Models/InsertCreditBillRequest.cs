namespace GoldfishReminder.Application.Models;

//新增信用卡帳單請求 目前僅 batch insert 使用 未來開放單筆 insert 不需動 DTO
public class InsertCreditBillRequest
{
    public Guid UserId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public int BillYear { get; set; }
    public int BillMonth { get; set; }
    public int StatementDay { get; set; }
    public int PaymentDueDay { get; set; }
    public int? BillAmount { get; set; } // 預留欄位 daily job 補建時為 null
}
