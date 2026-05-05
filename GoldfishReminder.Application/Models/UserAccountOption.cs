namespace GoldfishReminder.Application.Models;

// 供 Discord 指令 autocomplete 選項使用的帳戶資訊
public class UserAccountOption
{
    public Guid Id { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int Balance { get; set; }
}
