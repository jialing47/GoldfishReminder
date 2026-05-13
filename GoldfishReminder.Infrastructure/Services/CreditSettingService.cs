using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Domain.Entities;
using GoldfishReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldfishReminder.Infrastructure.Services;

//信用卡設定服務實作
public class CreditSettingService : ICreditSettingService
{
    private readonly AppDbContext dbContext;

    public CreditSettingService(AppDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    //新增或更新信用卡設定
    public async Task<CreditSetting> UpsertAsync(
        UpsertCreditSettingRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedBankCode = await ValidateRequestAsync(request, cancellationToken);

        CreditSetting creditSetting;

        if (request.Id.HasValue)
        {
            creditSetting = await GetTrackedByIdAsync(request.Id.Value, cancellationToken);

            // 驗設定屬於 request 的 user 防 IDOR
            if (creditSetting.UserId != request.UserId)
            {
                throw new UnauthorizedAccessException($"CreditSetting does not belong to the user. SettingId:{request.Id.Value}");
            }

            var conflictExists = await dbContext.CreditSettings
                .AsNoTracking()
                .AnyAsync(
                    x => x.UserId == request.UserId
                         && x.BankCode == normalizedBankCode
                         && x.Id != creditSetting.Id,
                    cancellationToken);

            if (conflictExists)
            {
                throw new InvalidOperationException("A credit setting with the same user and bank already exists");
            }
        }
        else
        {
            var existingSetting = await dbContext.CreditSettings
                .FirstOrDefaultAsync(
                    x => x.UserId == request.UserId && x.BankCode == normalizedBankCode,
                    cancellationToken);

            if (existingSetting == null)
            {
                creditSetting = new CreditSetting
                {
                    Id = Guid.NewGuid()
                };

                dbContext.CreditSettings.Add(creditSetting);
            }
            else
            {
                creditSetting = existingSetting;
            }
        }

        creditSetting.UserId = request.UserId;
        creditSetting.BankCode = normalizedBankCode;
        creditSetting.StatementDay = request.StatementDay;
        creditSetting.PaymentDueDay = request.PaymentDueDay;
        creditSetting.PaymentBankAccountId = request.PaymentBankAccountId;
        creditSetting.Enabled = request.Enabled;

        await dbContext.SaveChangesAsync(cancellationToken);

        return creditSetting;
    }

    //依 id 取得可追蹤信用卡設定
    private async Task<CreditSetting> GetTrackedByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var creditSetting = await dbContext.CreditSettings
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (creditSetting == null)
        {
            throw new KeyNotFoundException($"CreditSetting not found Id:{id}");
        }

        return creditSetting;
    }

    //驗證request並回傳標準化bankCode
    private async Task<string> ValidateRequestAsync(
        UpsertCreditSettingRequest request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required");
        }

        if (string.IsNullOrWhiteSpace(request.BankCode))
        {
            throw new ArgumentException("BankCode is required");
        }

        var normalizedBankCode = request.BankCode.Trim();

        if (request.StatementDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(request.StatementDay), "StatementDay must be between 1 and 31");
        }

        if (request.PaymentDueDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PaymentDueDay), "PaymentDueDay must be between 1 and 31");
        }

        // 結帳日與繳款日同一天會造成跨月卡計算錯亂 直接擋下
        // 訊息含 StatementDay 與 PaymentDueDay 字串 對齊 MapServiceErrorMessage 的中文 fallback
        if (request.StatementDay == request.PaymentDueDay)
        {
            throw new ArgumentException("StatementDay and PaymentDueDay cannot be the same");
        }

        var userExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(x => x.Id == request.UserId, cancellationToken);

        if (!userExists)
        {
            throw new ArgumentException($"User does not exist UserId:{request.UserId}");
        }

        var bankExists = await dbContext.Banks
            .AsNoTracking()
            .AnyAsync(x => x.BankCode == normalizedBankCode, cancellationToken);

        if (!bankExists)
        {
            throw new ArgumentException($"Bank does not exist BankCode:{normalizedBankCode}");
        }

        if (request.PaymentBankAccountId.HasValue)
        {
            var paymentBankAccountId = request.PaymentBankAccountId.Value;

            var paymentBankAccountInfo = await dbContext.BankAccounts
                .AsNoTracking()
                .Where(x => x.Id == paymentBankAccountId)
                .Select(x => new
                {
                    x.UserId,
                    x.Enabled
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (paymentBankAccountInfo == null)
            {
                throw new ArgumentException($"PaymentBankAccountId not found Id:{paymentBankAccountId}");
            }

            if (paymentBankAccountInfo.UserId != request.UserId)
            {
                throw new ArgumentException("PaymentBankAccountId does not belong to the same user");
            }

            if (!paymentBankAccountInfo.Enabled)
            {
                throw new ArgumentException("PaymentBankAccountId 指向的帳戶已停用");
            }
        }

        return normalizedBankCode;
    }
}
