using GoldfishReminder.Application.Models;
using GoldfishReminder.Domain.Entities;

namespace GoldfishReminder.Application.Services;

//銀行帳戶資料服務介面
public interface IBankAccountService
{
    Task<BankAccount> UpsertAsync(UpsertBankAccountRequest request, CancellationToken cancellationToken = default);
    Task<BankAccount> UpdateBalanceAsync(Guid accountId, Guid userId, int newBalance, CancellationToken cancellationToken = default);
}