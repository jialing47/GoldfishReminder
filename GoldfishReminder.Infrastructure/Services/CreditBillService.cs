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

    // 更新信用卡帳單 只覆寫可變欄位 不可變欄位 (UserId / BankCode / BillYear / BillMonth / StatementDay / PaymentDueDay) 永不被改
    public async Task UpdateAsync(UpdateCreditBillRequest request, CancellationToken cancellationToken = default)
    {
        if (request.BillId == Guid.Empty)
        {
            throw new ArgumentException("BillId is required");
        }

        if (request.BillAmount.HasValue && request.BillAmount.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.BillAmount), "BillAmount cannot be negative");
        }

        var creditBill = await dbContext.CreditBills.FirstOrDefaultAsync(x => x.Id == request.BillId, cancellationToken);

        if (creditBill == null)
        {
            throw new KeyNotFoundException($"CreditBill not found Id:{request.BillId}");
        }

        creditBill.BillAmount = request.BillAmount;
        creditBill.AmountConfirmed = request.AmountConfirmed;
        creditBill.Paid = request.Paid;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // 批次新增信用卡帳單 重複的 (UserId, BankCode, BillYear, BillMonth) 自動 skip
    public async Task<IReadOnlyList<CreditBill>> BatchInsertAsync(IReadOnlyCollection<InsertCreditBillRequest> requests, CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
        {
            return Array.Empty<CreditBill>();
        }

        // 第一段 純欄位驗證 + 標準化 BankCode + 收集要查的 id 集合
        var userIds = new HashSet<Guid>();
        var bankCodes = new HashSet<string>(StringComparer.Ordinal);
        var billYears = new HashSet<int>();
        var billMonths = new HashSet<int>();

        foreach (var request in requests)
        {
            ValidateInsertRequest(request);
            request.BankCode = request.BankCode.Trim();
            userIds.Add(request.UserId);
            bankCodes.Add(request.BankCode);
            billYears.Add(request.BillYear);
            billMonths.Add(request.BillMonth);
        }

        // 第二段 一次性查 user 存在性 避免 N+1
        var existingUserIds = await dbContext.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var userExistsSet = new HashSet<Guid>(existingUserIds);

        // 第三段 一次性查 bank 存在性 避免 N+1
        var existingBankCodes = await dbContext.Banks.AsNoTracking()
            .Where(x => bankCodes.Contains(x.BankCode))
            .Select(x => x.BankCode)
            .ToListAsync(cancellationToken);
        var bankExistsSet = new HashSet<string>(existingBankCodes, StringComparer.Ordinal);

        // 第四段 per-request 驗存在性 失敗整批不寫
        foreach (var request in requests)
        {
            if (!userExistsSet.Contains(request.UserId))
            {
                throw new ArgumentException($"User does not exist UserId:{request.UserId}");
            }

            if (!bankExistsSet.Contains(request.BankCode))
            {
                throw new ArgumentException($"Bank does not exist BankCode:{request.BankCode}");
            }
        }

        // 第五段 一次性查既存帳單組合
        var existingKeys = await dbContext.CreditBills.AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .Where(x => bankCodes.Contains(x.BankCode))
            .Where(x => billYears.Contains(x.BillYear))
            .Where(x => billMonths.Contains(x.BillMonth))
            .Select(x => new { x.UserId, x.BankCode, x.BillYear, x.BillMonth })
            .ToListAsync(cancellationToken);

        var existingKeySet = new HashSet<(Guid userId, string bankCode, int billYear, int billMonth)>();

        foreach (var key in existingKeys)
        {
            existingKeySet.Add((key.UserId, key.BankCode, key.BillYear, key.BillMonth));
        }

        // 第六段 insert 不在既存的帳單 同 batch 內重複 key 也只寫一次
        var insertedBills = new List<CreditBill>();

        foreach (var request in requests)
        {
            var billKey = (request.UserId, request.BankCode, request.BillYear, request.BillMonth);

            if (existingKeySet.Contains(billKey))
            {
                continue;
            }

            var creditBill = new CreditBill
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                BankCode = request.BankCode,
                BillYear = request.BillYear,
                BillMonth = request.BillMonth,
                StatementDay = request.StatementDay,
                PaymentDueDay = request.PaymentDueDay,
                BillAmount = request.BillAmount,
                AmountConfirmed = false,
                Paid = false
            };

            dbContext.CreditBills.Add(creditBill);
            insertedBills.Add(creditBill);
            existingKeySet.Add(billKey);
        }

        if (insertedBills.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return insertedBills;
    }

    // 驗證 insert request 欄位 (純記憶體不查 DB)
    private static void ValidateInsertRequest(InsertCreditBillRequest request)
    {
        if (request.UserId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required");
        }

        if (string.IsNullOrWhiteSpace(request.BankCode))
        {
            throw new ArgumentException("BankCode is required");
        }

        if (request.BillYear < 2000 || request.BillYear > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(request.BillYear), "BillYear must be between 2000 and 9999");
        }

        if (request.BillMonth < 1 || request.BillMonth > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(request.BillMonth), "BillMonth must be between 1 and 12");
        }

        if (request.StatementDay < 1 || request.StatementDay > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(request.StatementDay), "StatementDay must be between 1 and 31");
        }

        if (request.PaymentDueDay < 1 || request.PaymentDueDay > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PaymentDueDay), "PaymentDueDay must be between 1 and 31");
        }

        if (request.BillAmount.HasValue && request.BillAmount.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.BillAmount), "BillAmount cannot be negative");
        }
    }
}
