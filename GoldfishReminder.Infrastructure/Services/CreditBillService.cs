using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Domain.Entities;
using GoldfishReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldfishReminder.Infrastructure.Services;

// 信用卡帳單資料服務
public class CreditBillService : ICreditBillService
{
    private readonly AppDbContext dbContext;

    public CreditBillService(AppDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    // 新增或更新信用卡帳單
    public async Task<CreditBill> UpsertAsync(UpsertCreditBillRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedBankCode = await ValidateRequestAsync(request, cancellationToken);
        request.BankCode = normalizedBankCode;

        var billYear = request.BillYear!.Value;
        var billMonth = request.BillMonth!.Value;
        CreditBill creditBill;

        if (request.Id.HasValue)
        {
            creditBill = await GetTrackedByIdAsync(request.Id.Value, cancellationToken);
        }
        else
        {
            var existingBill = await dbContext.CreditBills
                .FirstOrDefaultAsync(x => x.UserId == request.UserId && x.BankCode == normalizedBankCode && x.BillYear == billYear && x.BillMonth == billMonth, cancellationToken);

            if (existingBill == null)
            {
                creditBill = new CreditBill
                {
                    Id = Guid.NewGuid()
                };

                dbContext.CreditBills.Add(creditBill);
            }
            else
            {
                creditBill = existingBill;
            }
        }

        ApplyBillValues(creditBill, request);
        await dbContext.SaveChangesAsync(cancellationToken);
        return creditBill;
    }

    // 批次新增信用卡帳單
    public async Task<IReadOnlyList<CreditBill>> BatchInsertAsync(IReadOnlyCollection<UpsertCreditBillRequest> requests, CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
        {
            return Array.Empty<CreditBill>();
        }

        var normalizedRequests = new List<UpsertCreditBillRequest>(requests.Count);
        var userIds = new HashSet<Guid>();
        var bankCodes = new HashSet<string>(StringComparer.Ordinal);
        var billYears = new HashSet<int>();
        var billMonths = new HashSet<int>();

        foreach (var request in requests)
        {
            if (request.Id.HasValue)
            {
                throw new ArgumentException("BatchInsertAsync only supports insert requests without Id");
            }

            var normalizedBankCode = await ValidateRequestAsync(request, cancellationToken);
            request.BankCode = normalizedBankCode;
            normalizedRequests.Add(request);
            userIds.Add(request.UserId);
            bankCodes.Add(normalizedBankCode);
            billYears.Add(request.BillYear!.Value);
            billMonths.Add(request.BillMonth!.Value);
        }

        var existingKeys = await dbContext.CreditBills
            .AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .Where(x => bankCodes.Contains(x.BankCode))
            .Where(x => billYears.Contains(x.BillYear))
            .Where(x => billMonths.Contains(x.BillMonth))
            .Select(x => new { x.UserId, x.BankCode, x.BillYear, x.BillMonth })
            .ToListAsync(cancellationToken);

        var keySet = new HashSet<(Guid userId, string bankCode, int billYear, int billMonth)>();

        foreach (var key in existingKeys)
        {
            keySet.Add((key.UserId, key.BankCode, key.BillYear, key.BillMonth));
        }

        var insertedBills = new List<CreditBill>();

        foreach (var request in normalizedRequests)
        {
            var billKey = (request.UserId, request.BankCode, request.BillYear!.Value, request.BillMonth!.Value);

            if (keySet.Contains(billKey))
            {
                continue;
            }

            var creditBill = new CreditBill
            {
                Id = Guid.NewGuid()
            };

            ApplyBillValues(creditBill, request);
            dbContext.CreditBills.Add(creditBill);
            insertedBills.Add(creditBill);
            keySet.Add(billKey);
        }

        if (insertedBills.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return insertedBills;
    }

    // 依 id 取得可追蹤帳單
    private async Task<CreditBill> GetTrackedByIdAsync(Guid billId, CancellationToken cancellationToken)
    {
        var creditBill = await dbContext.CreditBills.FirstOrDefaultAsync(x => x.Id == billId, cancellationToken);

        if (creditBill == null)
        {
            throw new KeyNotFoundException($"CreditBill not found Id:{billId}");
        }

        return creditBill;
    }

    // 套用帳單欄位
    private static void ApplyBillValues(CreditBill creditBill, UpsertCreditBillRequest request)
    {
        creditBill.UserId = request.UserId;
        creditBill.BankCode = request.BankCode.Trim();
        creditBill.BillYear = request.BillYear!.Value;
        creditBill.BillMonth = request.BillMonth!.Value;
        creditBill.StatementDay = request.StatementDay!.Value;
        creditBill.PaymentDueDay = request.PaymentDueDay!.Value;
        creditBill.BillAmount = request.BillAmount;
        creditBill.AmountConfirmed = request.AmountConfirmed;
        creditBill.Paid = request.Paid;
    }

    // 驗證 request 並回傳標準化 bankCode
    private async Task<string> ValidateRequestAsync(UpsertCreditBillRequest request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required");
        }

        if (string.IsNullOrWhiteSpace(request.BankCode))
        {
            throw new ArgumentException("BankCode is required");
        }

        if (!request.BillYear.HasValue)
        {
            throw new ArgumentException("BillYear is required");
        }

        if (!request.BillMonth.HasValue)
        {
            throw new ArgumentException("BillMonth is required");
        }

        if (!request.StatementDay.HasValue)
        {
            throw new ArgumentException("StatementDay is required");
        }

        if (!request.PaymentDueDay.HasValue)
        {
            throw new ArgumentException("PaymentDueDay is required");
        }

        if (request.BillYear.Value < 2000 || request.BillYear.Value > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(request.BillYear), "BillYear must be between 2000 and 9999");
        }

        if (request.BillMonth.Value < 1 || request.BillMonth.Value > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(request.BillMonth), "BillMonth must be between 1 and 12");
        }

        if (request.StatementDay.Value < 1 || request.StatementDay.Value > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(request.StatementDay), "StatementDay must be between 1 and 31");
        }

        if (request.PaymentDueDay.Value < 1 || request.PaymentDueDay.Value > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PaymentDueDay), "PaymentDueDay must be between 1 and 31");
        }

        if (request.BillAmount.HasValue && request.BillAmount.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.BillAmount), "BillAmount cannot be negative");
        }

        var normalizedBankCode = request.BankCode.Trim();
        var userExists = await dbContext.Users.AsNoTracking().AnyAsync(x => x.Id == request.UserId, cancellationToken);

        if (!userExists)
        {
            throw new ArgumentException($"User does not exist UserId:{request.UserId}");
        }

        var bankExists = await dbContext.Banks.AsNoTracking().AnyAsync(x => x.BankCode == normalizedBankCode, cancellationToken);

        if (!bankExists)
        {
            throw new ArgumentException($"Bank does not exist BankCode:{normalizedBankCode}");
        }

        return normalizedBankCode;
    }
}
