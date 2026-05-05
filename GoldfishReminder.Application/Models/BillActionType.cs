namespace GoldfishReminder.Application.Models;

// 帳單流程動作類型
public enum BillActionType
{
    None = 0,                       // 不需要任何動作
    PromptAmountInput = 1,          // 提醒輸入帳單金額
    PromptManualPay = 2,            // 提醒手動繳費 無扣款帳戶或未啟用
    AutoPay = 3,                    // 自動扣款
    AccountInsufficientBalance = 4, // 帳戶餘額不足 account level
    DisableReminder = 5             // 停用提醒
}

// workflow 回傳的動作
public class WorkflowAction
{
    public BillActionType ActionType { get; set; }
    public string Message { get; set; } = string.Empty;
    public object[]? Components { get; set; }
}
