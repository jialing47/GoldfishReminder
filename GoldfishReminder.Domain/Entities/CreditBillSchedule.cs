namespace GoldfishReminder.Domain.Entities;

// 信用卡帳單時程計算 純函數 跟 CreditBill 強綁定
public static class CreditBillSchedule
{
    // 計算真正繳款日 繳費日號小於結帳日號代表跨月 落在帳單月份的下個月 超過月底會 clamp 到當月最後一天
    public static DateTime CalculateDueDate(int billYear, int billMonth, int statementDay, int paymentDueDay)
    {
        var year = billYear;
        var month = billMonth;

        if (paymentDueDay < statementDay)
        {
            month++;
            if (month > 12)
            {
                month = 1;
                year++;
            }
        }

        var daysInMonth = DateTime.DaysInMonth(year, month);
        var clampedDay = Math.Min(paymentDueDay, daysInMonth);
        return new DateTime(year, month, clampedDay);
    }

    // 計算真正繳款日 給 CreditBill entity 使用的便利重載
    public static DateTime CalculateDueDate(CreditBill creditBill)
    {
        return CalculateDueDate(creditBill.BillYear, creditBill.BillMonth, creditBill.StatementDay, creditBill.PaymentDueDay);
    }

    // 計算結帳日 在帳單月份內 結帳日號超過當月天數時 clamp 到當月最後一天
    public static DateTime CalculateStatementDate(CreditBill creditBill)
    {
        var daysInMonth = DateTime.DaysInMonth(creditBill.BillYear, creditBill.BillMonth);
        var clampedDay = Math.Min(creditBill.StatementDay, daysInMonth);
        return new DateTime(creditBill.BillYear, creditBill.BillMonth, clampedDay);
    }
}
