using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medzo.SalesReporting.Services;

public sealed class BatchRemovalValidationException(string message) : Exception(message);

public interface IBatchRemovalService
{
    Task<BatchRemovalCandidatesResponse> GetCandidatesAsync(int withinDays, int page, int pageSize, CancellationToken ct);
    Task<BatchRemovalResponse> RemoveAsync(Guid batchId, RemoveBatchRequest request, CancellationToken ct);
}

public sealed class BatchRemovalService(SalesDbContext db) : IBatchRemovalService
{
    public async Task<BatchRemovalCandidatesResponse> GetCandidatesAsync(int withinDays, int page, int pageSize, CancellationToken ct)
    {
        withinDays = Math.Clamp(withinDays, 1, 30);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var deadline = today.AddDays(withinDays);
        var query = db.StockBatches.AsNoTracking()
            .Where(batch => !batch.IsRemoved && batch.RemainingQuantity > 0 && batch.ExpiryDate <= deadline);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(batch => batch.ExpiryDate).ThenBy(batch => batch.CreatedAtUtc).ThenBy(batch => batch.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(batch => new BatchRemovalCandidateResponse(
                batch.Id, batch.ProductId, batch.BatchNumber, batch.ExpiryDate,
                batch.ExpiryDate.DayNumber - today.DayNumber, batch.RemainingQuantity,
                batch.ExpiryDate < today))
            .ToListAsync(ct);
        return new BatchRemovalCandidatesResponse(items, page, pageSize, total);
    }

    public async Task<BatchRemovalResponse> RemoveAsync(Guid batchId, RemoveBatchRequest request, CancellationToken ct)
    {
        Validate(request);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var batch = await db.StockBatches.SingleOrDefaultAsync(x => x.Id == batchId, ct)
            ?? throw new BatchRemovalValidationException("The stock batch was not found.");

        if (batch.IsRemoved)
        {
            var existingAudit = await db.BatchRemovalAudits.AsNoTracking().SingleAsync(x => x.BatchId == batchId, ct);
            await transaction.CommitAsync(ct);
            return ToResponse(existingAudit, true);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (batch.ExpiryDate > today.AddDays(30))
            throw new BatchRemovalValidationException("Only batches that are expired or within the next 30 days may be removed.");

        var quantity = batch.RemainingQuantity;
        if (quantity <= 0)
            throw new BatchRemovalValidationException("This batch has no remaining stock to remove.");

        var removedAt = DateTime.UtcNow;
        var audit = new BatchRemovalAudit
        {
            BatchId = batch.Id,
            ProductId = batch.ProductId,
            BatchNumber = batch.BatchNumber,
            RemovedQuantity = quantity,
            Reason = request.Reason.Trim(),
            RemovedBy = request.RemovedBy.Trim(),
            RemovedAtUtc = removedAt
        };
        batch.RemainingQuantity = 0;
        batch.IsRemoved = true;
        batch.RemovedAtUtc = removedAt;
        batch.RemovedBy = audit.RemovedBy;
        batch.RemovalReason = audit.Reason;
        db.BatchRemovalAudits.Add(audit);

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToResponse(audit, false);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var existingAudit = await db.BatchRemovalAudits.AsNoTracking().SingleOrDefaultAsync(x => x.BatchId == batchId, ct);
            if (existingAudit is null) throw;
            return ToResponse(existingAudit, true);
        }
    }

    static void Validate(RemoveBatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)) throw new BatchRemovalValidationException("An idempotency key is required.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new BatchRemovalValidationException("Choose or enter a reason for removal.");
        if (request.Reason.Trim().Length > 500) throw new BatchRemovalValidationException("The removal reason must be 500 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.RemovedBy)) throw new BatchRemovalValidationException("The Inventory Manager identity is required.");
    }

    static BatchRemovalResponse ToResponse(BatchRemovalAudit audit, bool alreadyRemoved) => new(
        audit.BatchId, audit.ProductId, audit.BatchNumber, audit.RemovedQuantity,
        audit.Reason, audit.RemovedBy, audit.RemovedAtUtc, alreadyRemoved);
}