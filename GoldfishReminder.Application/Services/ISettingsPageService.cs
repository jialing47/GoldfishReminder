using GoldfishReminder.Application.Models;

namespace GoldfishReminder.Application.Services;

// 設定頁面服務介面
public interface ISettingsPageService
{
    Task<SettingsPageData> GetPageDataAsync(Guid userId, int? historyYear = null, int? historyMonth = null, CancellationToken cancellationToken = default);
    Task SaveBankAccountAsync(Guid userId, BankAccountInputModel input, CancellationToken cancellationToken = default);
    Task SaveCreditSettingAsync(Guid userId, CreditSettingInputModel input, CancellationToken cancellationToken = default);
    Task UpdateBillAmountAsync(Guid userId, UpdateBillAmountInput input, CancellationToken cancellationToken = default);
}
