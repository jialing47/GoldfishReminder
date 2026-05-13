using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Domain.Entities;

namespace GoldfishReminder.Application.Workflows;

// 通知流程
public class NotificationWorkflow
{
    private readonly INotificationSender sender;
    private readonly INotificationLogService logService;
    private readonly HashSet<string> cache;
    private readonly HashSet<Guid> preloadedUserIds;
    private DateTime? cacheDate;

    // 初始化
    public NotificationWorkflow(INotificationSender sender, INotificationLogService logService)
    {
        this.sender = sender;
        this.logService = logService;
        cache = new HashSet<string>(StringComparer.Ordinal);
        preloadedUserIds = new HashSet<Guid>();
    }

    // 預載今天已送通知
    public void PrimeCache(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<SentNotificationKey> sentTodayKeys, DateTimeOffset nowUtc)
    {
        var localDate = TaiwanClock.GetToday(nowUtc);
        ResetCache(localDate);

        foreach (var userId in userIds)
        {
            preloadedUserIds.Add(userId);
        }

        foreach (var key in sentTodayKeys)
        {
            cache.Add(BuildKey(key.UserId, key.NotificationType, key.TargetId, localDate));
        }
    }

    // 依 action 發送 bypassDedup 為 true 時強制發送不受當日去重限制（用於餘額即時變更觸發）
    public async Task SendByActionAsync(Guid userId, string channelId, BillActionType action, CreditBill bill, Bank bank, AccountContext? accountContext, PaymentAccountSnapshot? account, CancellationToken cancellationToken = default, bool bypassDedup = false)
    {
        var dispatch = BuildDispatch(userId, action, bill, bank, accountContext, account);

        if (dispatch == null)
        {
            return;
        }

        await SendOnceAsync(userId, channelId, dispatch.NotificationType, dispatch.TargetId, dispatch.Message, dispatch.Components, bypassDedup, cancellationToken);
    }

    // 去重發送 bypassDedup 為 true 時不檢查 cache 與 log 但仍會寫入保留稽核紀錄
    private async Task SendOnceAsync(Guid userId, string channelId, string notificationType, Guid targetId, string message, object[]? components, bool bypassDedup, CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var localDate = TaiwanClock.GetToday(nowUtc);
        ResetCache(localDate);

        var cacheKey = BuildKey(userId, notificationType, targetId, localDate);

        if (!bypassDedup)
        {
            if (cache.Contains(cacheKey))
            {
                return;
            }

            var exists = false;

            if (!preloadedUserIds.Contains(userId))
            {
                exists = await logService.HasSentTodayAsync(userId, notificationType, targetId, nowUtc, cancellationToken);
            }

            if (exists)
            {
                cache.Add(cacheKey);
                return;
            }
        }

        // 發送失敗也要寫 fail log 並 rethrow 給外層 try 紀錄 與 cache 不能在失敗時加入 否則下次 daily 不會 retry
        try
        {
            await sender.SendAsync(channelId, message, components, cancellationToken);
        }
        catch (Exception ex)
        {
            await logService.AddFailureAsync(userId, notificationType, targetId, message, ex.Message, nowUtc, cancellationToken);
            throw;
        }

        await logService.AddAsync(userId, notificationType, targetId, message, nowUtc, cancellationToken);
        cache.Add(cacheKey);
    }

    // 建立發送內容
    private static NotificationDispatch? BuildDispatch(Guid userId, BillActionType action, CreditBill bill, Bank bank, AccountContext? accountContext, PaymentAccountSnapshot? account)
    {
        switch (action)
        {
            case BillActionType.PromptAmountInput:
                return new NotificationDispatch
                {
                    NotificationType = NotificationTypes.BillAmountPrompt,
                    TargetId = bill.Id,
                    Message = MessageBuilder.PromptAmountText(bill, bank),
                    Components = MessageBuilder.PromptAmountButton(bill.Id)
                };

            case BillActionType.PromptManualPay:
                return new NotificationDispatch
                {
                    NotificationType = NotificationTypes.BillManualPay,
                    TargetId = bill.Id,
                    Message = MessageBuilder.PromptManualPayText(bill, bank),
                    Components = MessageBuilder.PromptManualPayButton(bill.Id)
                };

            case BillActionType.AccountInsufficientBalance:
                if (accountContext == null || account == null)
                {
                    return null;
                }

                // 帳戶層級通知以扣款帳戶 id 作為 targetId，避免分組順序造成同日重送
                return new NotificationDispatch
                {
                    NotificationType = NotificationTypes.AccountInsufficientBalance,
                    TargetId = account.Id,
                    Message = MessageBuilder.AccountInsufficientText(accountContext, account)
                };

            case BillActionType.DisableReminder:
                return new NotificationDispatch
                {
                    NotificationType = NotificationTypes.CreditSettingAutoDisabled,
                    TargetId = userId,
                    Message = MessageBuilder.DisabledText()
                };

            default:
                return null;
        }
    }

    // 重設快取日期
    private void ResetCache(DateTime localDate)
    {
        if (cacheDate.HasValue && cacheDate.Value == localDate)
        {
            return;
        }

        cache.Clear();
        preloadedUserIds.Clear();
        cacheDate = localDate;
    }

    // 建立快取 key
    private static string BuildKey(Guid userId, string notificationType, Guid targetId, DateTime localDate)
    {
        return userId + ":" + notificationType + ":" + targetId + ":" + localDate.ToString("yyyyMMdd");
    }

    // 單次發送內容
    private sealed class NotificationDispatch
    {
        public string NotificationType { get; set; } = string.Empty;
        public Guid TargetId { get; set; }
        public string Message { get; set; } = string.Empty;
        public object[]? Components { get; set; }
    }
}
