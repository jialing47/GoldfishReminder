using GoldfishReminder.Application.Models;
using GoldfishReminder.Domain.Entities;

namespace GoldfishReminder.Application.Services;

// 信用卡帳單資料服務介面
public interface ICreditBillService
{
    Task UpdateAsync(UpdateCreditBillRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CreditBill>> BatchInsertAsync(IReadOnlyCollection<InsertCreditBillRequest> requests, CancellationToken cancellationToken = default);
}