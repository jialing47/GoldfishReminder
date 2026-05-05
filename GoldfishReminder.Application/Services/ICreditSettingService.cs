using GoldfishReminder.Application.Models;
using GoldfishReminder.Domain.Entities;

namespace GoldfishReminder.Application.Services;

//信用卡設定資料服務介面
public interface ICreditSettingService
{
    Task<CreditSetting> UpsertAsync(UpsertCreditSettingRequest request, CancellationToken cancellationToken = default);
}