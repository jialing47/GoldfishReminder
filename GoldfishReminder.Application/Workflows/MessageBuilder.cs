using System.Text;
using GoldfishReminder.Application.Models;
using GoldfishReminder.Domain.Entities;

namespace GoldfishReminder.Application.Workflows;

// 專門負責組訊息與按鈕
public static class MessageBuilder
{
    // 建立輸入帳單金額訊息
    public static string PromptAmountText(CreditBill bill, Bank bank)
    {
        return "請輸入本期帳單金額\n" +
               "[" + bill.BankCode + " " + bank.BankName + "]\n" +
               "帳單月份：" + bill.BillYear + "/" + bill.BillMonth;
    }

    // 建立輸入帳單金額按鈕
    public static object[] PromptAmountButton(Guid billId)
    {
        return new object[]
        {
            new
            {
                type = 1,
                components = new object[]
                {
                    new
                    {
                        type = 2,
                        style = 1,
                        custom_id = "gr_bill_amount:" + billId,
                        label = "輸入帳單金額"
                    }
                }
            }
        };
    }

    // 建立手動繳費訊息
    public static string PromptManualPayText(CreditBill bill, Bank bank)
    {
        var amount = 0;

        if (bill.BillAmount.HasValue)
        {
            amount = bill.BillAmount.Value;
        }

        return "[" + bill.BankCode + " " + bank.BankName + "]\n" +
               "帳單金額：" + amount + "\n" +
               "未設定自動扣繳銀行，請自行繳費\n";
    }

    // 建立手動繳費按鈕
    public static object[] PromptManualPayButton(Guid billId)
    {
        return new object[]
        {
            new
            {
                type = 1,
                components = new object[]
                {
                    new
                    {
                        type = 2,
                        style = 3,
                        custom_id = "gr_mark_paid:" + billId,
                        label = "確認已繳費"
                    }
                }
            }
        };
    }

    // 建立帳戶不足訊息
    public static string AccountInsufficientText(AccountContext accountContext, PaymentAccountSnapshot paymentAccount)
    {
        var stringBuilder = new StringBuilder();

        stringBuilder.AppendLine("自動扣繳銀行餘額不足");
        stringBuilder.AppendLine("帳戶：" + paymentAccount.AccountName);
        stringBuilder.AppendLine("目前餘額：" + paymentAccount.Balance.ToString("N0"));

        var totalAmount = 0;

        foreach (var bill in accountContext.CreditBills)
        {
            if (bill.AmountConfirmed && !bill.Paid && bill.BillAmount.HasValue)
            {
                totalAmount += bill.BillAmount.Value;
            }
        }

        stringBuilder.AppendLine("已確認待扣總額：" + totalAmount.ToString("N0"));
        stringBuilder.AppendLine("明細：");

        foreach (var bill in accountContext.CreditBills)
        {
            if (bill.Paid)
            {
                continue;
            }

            var bank = accountContext.Banks[bill.BankCode];
            var amount = 0;

            if (bill.AmountConfirmed && bill.BillAmount.HasValue)
            {
                amount = bill.BillAmount.Value;
            }

            var status = bill.AmountConfirmed ? "已確認" : "未確認";
            stringBuilder.AppendLine("- " + bill.BillYear + "/" + bill.BillMonth + " " + bank.BankName + "：" + amount.ToString("N0") + "(" + status + ")");
        }

        return stringBuilder.ToString().TrimEnd();
    }

    // 建立帳單更新成功訊息
    public static string UpdatedText()
    {
        return "帳單已更新";
    }

    // 建立帳單更新後的手動繳費訊息
    public static string UpdatedManualText()
    {
        return "帳單已更新\n未設定自動扣繳銀行，請自行繳費\n";
    }

    // 建立帳單更新後的不足額訊息
    public static string UpdatedInsufficientText(AccountContext accountContext, PaymentAccountSnapshot paymentAccount)
    {
        return "帳單已更新\n" + AccountInsufficientText(accountContext, paymentAccount);
    }

    // 建立停用提醒訊息
    public static string DisabledText()
    {
        return "提醒已停用\n超過繳費日 14 天仍未處理，已停用此使用者全部信用卡提醒。";
    }
}
