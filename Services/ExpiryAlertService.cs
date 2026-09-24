using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Data;
using Microsoft.EntityFrameworkCore;

namespace Medzo.SalesReporting.Services;

public interface IExpiryAlertService
{
    Task<NearExpiryAlertResponse> GetNearExpiryAsync(int withinDays, int page, int pageSize, CancellationToken ct);
}

public sealed class ExpiryAlertService(SalesDbContext db) : IExpiryAlertService
{
    public async Task<NearExpiryAlertResponse> GetNearExpiryAsync(int withinDays, int page, int pageSize, CancellationToken ct)
    {
        withinDays = Math.Clamp(withinDays, 1, 365);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var deadline = today.AddDays(withinDays);
        var query = db.StockBatches.AsNoTracking().Where(batch => !batch.IsRemoved && batch.RemainingQuantity > 0 && batch.ExpiryDate >= today && batch.ExpiryDate <= deadline);
        var total = await query.CountAsync(ct);
        var batches = await query.OrderBy(batch => batch.ExpiryDate).ThenBy(batch => batch.CreatedAtUtc).ThenBy(batch => batch.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = batches.Select(batch => new NearExpiryBatchResponse(batch.Id, batch.ProductId, batch.BatchNumber, batch.ExpiryDate, batch.ExpiryDate.DayNumber - today.DayNumber, batch.RemainingQuantity)).ToList();
        return new NearExpiryAlertResponse(items, page, pageSize, total);
    }
}