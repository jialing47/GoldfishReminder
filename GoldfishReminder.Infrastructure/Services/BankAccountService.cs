using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Domain.Entities;
using GoldfishReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldfishReminder.Infrastructure.Services;

//銀行帳戶資料服務
public class BankAccountService : IBankAccountService
{
    private readonly AppDbContext dbContext;

    public BankAccountService(AppDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    //新增或更新銀行帳戶
    public async Task<BankAccount> UpsertAsync(
        UpsertBankAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.UserId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required");
        }

        if (string.IsNullOrWhiteSpace(request.BankCode))
        {
            throw new ArgumentException("BankCode is required");
        }

        if (string.IsNullOrWhiteSpace(request.AccountName))
        {
            throw new ArgumentException("AccountName is required");
        }

        if (string.IsNullOrWhiteSpace(request.AccountType))
        {
            throw new ArgumentException("AccountType is required");
        }

        if (request.Balance is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Balance), "Balance cannot be negative");
        }

        var normalizedBankCode = request.BankCode.Trim();
        var accountName = request.AccountName.Trim();
        var accountType = request.AccountType.Trim().ToLowerInvariant();

        if (accountType != "digital" && accountType != "physical")
        {
            throw new ArgumentException("AccountType must be 'digital' or 'physical'");
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

        BankAccount bankAccount;
        var wasEnabled = false;

        if (request.Id.HasValue)
        {
            bankAccount = await GetTrackedByIdAsync(request.Id.Value, cancellationToken);

            // 驗帳戶屬於 request 的 user 防 IDOR
            if (bankAccount.UserId != request.UserId)
            {
                throw new UnauthorizedAccessException($"BankAccount does not belong to the user. AccountId:{request.Id.Value}");
            }

            wasEnabled = bankAccount.Enabled;
        }
        else
        {
            bankAccount = new BankAccount
            {
                Id = Guid.NewGuid(),
                Balance = 0
            };

            dbContext.BankAccounts.Add(bankAccount);
        }

        bankAccount.UserId = request.UserId;
        bankAccount.BankCode = normalizedBankCode;
        bankAccount.AccountName = accountName;
        bankAccount.AccountType = accountType;
        bankAccount.Enabled = request.Enabled;

        if (request.Balance.HasValue)
        {
            bankAccount.Balance = request.Balance.Value;

            if (request.BalanceUpdatedAt.HasValue)
            {
                bankAccount.BalanceUpdatedAt = request.BalanceUpdatedAt.Value;
            }
            else
            {
                bankAccount.BalanceUpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        else if (!request.Id.HasValue)
        {
            bankAccount.BalanceUpdatedAt = request.BalanceUpdatedAt;
        }

        // 帳戶從啟用轉為停用時 清除所有關聯的信用卡扣款設定 避免後續自動扣款誤判
        if (wasEnabled && !request.Enabled)
        {
            var linkedSettings = await dbContext.CreditSettings
                .Where(x => x.PaymentBankAccountId == bankAccount.Id)
                .ToListAsync(cancellationToken);

            foreach (var setting in linkedSettings)
            {
                setting.PaymentBankAccountId = null;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return bankAccount;
    }

    //更新指定帳戶的餘額 供 Discord 指令使用 檢查帳戶屬於指定 user 防止跨 user 修改
    public async Task<BankAccount> UpdateBalanceAsync(Guid accountId, Guid userId, int newBalance, CancellationToken cancellationToken = default)
    {
        if (newBalance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newBalance), "Balance cannot be negative");
        }

        var bankAccount = await GetTrackedByIdAsync(accountId, cancellationToken);

        if (bankAccount.UserId != userId)
        {
            throw new UnauthorizedAccessException($"BankAccount does not belong to the user. AccountId:{accountId}");
        }

        bankAccount.Balance = newBalance;
        bankAccount.BalanceUpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return bankAccount;
    }

    //依 id 取得可追蹤銀行帳戶
    private async Task<BankAccount> GetTrackedByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var bankAccount = await dbContext.BankAccounts
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (bankAccount == null)
        {
            throw new KeyNotFoundException($"BankAccount not found Id:{id}");
        }

        return bankAccount;
    }
}
