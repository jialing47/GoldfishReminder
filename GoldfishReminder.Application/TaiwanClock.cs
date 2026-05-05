namespace GoldfishReminder.Application;

// 台灣時區時間工具 所有需要本地日期判斷的服務共用
public static class TaiwanClock
{
    private static readonly TimeZoneInfo timeZone = Resolve();

    // 取得台灣今日日期 以當下 UTC 為準
    public static DateTime GetToday()
    {
        return GetToday(DateTimeOffset.UtcNow);
    }

    // 取得台灣今日日期 以指定 UTC 時刻為準 供 workflow 內保持 now 一致
    public static DateTime GetToday(DateTimeOffset nowUtc)
    {
        return TimeZoneInfo.ConvertTime(nowUtc, timeZone).Date;
    }

    // 根據 UTC 時刻換算台灣當日起訖 回傳對應 UTC 區間供 DB 查詢使用
    public static void GetDayRange(DateTimeOffset nowUtc, out DateTimeOffset utcStart, out DateTimeOffset utcEnd)
    {
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var localStart = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, timeZone.GetUtcOffset(localNow));
        utcStart = localStart.ToUniversalTime();
        utcEnd = localStart.AddDays(1).ToUniversalTime();
    }

    // 解析台灣時區 Linux IANA 與 Windows Display id 都試
    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
        }
        catch
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
        }
    }
}
