namespace GoldfishReminder.Application.Models;

// 通知類型常數 對應 notification_logs.notification_type 值
public static class NotificationTypes
{
    public const string BillAmountPrompt = "credit_bill_amount_prompt";
    public const string BillManualPay = "credit_bill_manual_pay";
    public const string CreditSettingAutoDisabled = "credit_setting_auto_disabled";
    public const string AccountInsufficientBalance = "account_insufficient_balance";
}
