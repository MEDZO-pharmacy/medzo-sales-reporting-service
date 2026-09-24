using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medzo.SalesReporting.Services;

public sealed class SaleValidationException(string message) : Exception(message);
public sealed class SaleConflictException(string message) : Exception(message);
public interface ISaleService
{
 Task<SaleResponse> CreateAsync(CreateSaleRequest request, CancellationToken ct);
 Task<SaleReceiptResponse?> GetReceiptAsync(Guid saleId, CancellationToken ct);
}

public sealed class SaleService(SalesDbContext db) : ISaleService
{
 public async Task<SaleResponse> CreateAsync(CreateSaleRequest request, CancellationToken ct)
 {
  Validate(request);
  await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
  var existing = await FindByKeyAsync(request.IdempotencyKey, ct);
  if (existing is not null)
  {
   await transaction.CommitAsync(ct);
   return ToResponse(existing, true);
  }

  var sale = new Sale { IdempotencyKey = request.IdempotencyKey };
  foreach (var line in request.Items)
  {
   var batches = await db.StockBatches
    .Where(x => x.ProductId == line.ProductId && !x.IsRemoved && x.RemainingQuantity > 0 && x.ExpiryDate >= DateOnly.FromDateTime(DateTime.UtcNow))
    .OrderBy(x => x.ExpiryDate).ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
    .ToListAsync(ct);

   if (batches.Sum(x => x.RemainingQuantity) < line.Quantity)
    throw new SaleValidationException($"Insufficient sellable stock for product '{line.ProductId}'.");

   var item = new SaleItem { ProductId = line.ProductId, Quantity = line.Quantity };
   var remaining = line.Quantity;
   foreach (var batch in batches)
   {
    if (remaining == 0) break;
    var allocated = Math.Min(batch.RemainingQuantity, remaining);
    batch.RemainingQuantity -= allocated;
    item.Allocations.Add(new BatchAllocation { StockBatchId = batch.Id, Quantity = allocated });
    remaining -= allocated;
   }
   sale.Items.Add(item);
  }

  db.Sales.Add(sale);
  try
  {
   await db.SaveChangesAsync(ct);
   await transaction.CommitAsync(ct);
  }
  catch (DbUpdateException e) when (IsIdempotencyConflict(e))
  {
   await transaction.RollbackAsync(ct);
   db.ChangeTracker.Clear();
   var duplicate = await FindByKeyAsync(request.IdempotencyKey, ct);
   if (duplicate is null) throw;
   return ToResponse(duplicate, true);
  }
  catch (DbUpdateConcurrencyException)
  {
   await transaction.RollbackAsync(ct);
   db.ChangeTracker.Clear();
   throw new SaleConflictException("Stock changed while this sale was being processed. No stock was deducted; retry the request with the same idempotency key.");
  }

  return await GetResponseAsync(sale.Id, false, ct);
 }

 public async Task<SaleReceiptResponse?> GetReceiptAsync(Guid saleId, CancellationToken ct)
 {
  var sale = await FindByIdAsync(saleId, ct);
  return sale is null ? null : ToReceipt(sale);
 }

 static void Validate(CreateSaleRequest r)
 {
  if (string.IsNullOrWhiteSpace(r.IdempotencyKey)) throw new SaleValidationException("An idempotency key is required.");
  if (r.Items is null || r.Items.Count == 0) throw new SaleValidationException("At least one sale item is required.");
  if (r.Items.Any(x => string.IsNullOrWhiteSpace(x.ProductId) || x.Quantity <= 0)) throw new SaleValidationException("Every sale item needs a product and a positive quantity.");
  if (r.Items.Select(x => x.ProductId).Distinct(StringComparer.Ordinal).Count() != r.Items.Count) throw new SaleValidationException("Each product may appear only once per sale.");
 }

 Task<Sale?> FindByKeyAsync(string key, CancellationToken ct) => db.Sales.Include(x => x.Items).ThenInclude(x => x.Allocations).ThenInclude(x => x.StockBatch).SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
 Task<Sale?> FindByIdAsync(Guid saleId, CancellationToken ct) => db.Sales.Include(x => x.Items).ThenInclude(x => x.Allocations).ThenInclude(x => x.StockBatch).SingleOrDefaultAsync(x => x.Id == saleId, ct);
 async Task<SaleResponse> GetResponseAsync(Guid id, bool done, CancellationToken ct) => ToResponse(await FindByIdAsync(id, ct) ?? throw new InvalidOperationException("Completed sale was not found."), done);
 static SaleResponse ToResponse(Sale sale, bool done) => new(sale.Id, done, sale.Items.Select(ToSaleItem).ToList(), ToReceipt(sale));
 static SaleItemResponse ToSaleItem(SaleItem item) => new(item.ProductId, item.Quantity, ToAllocations(item));
 static SaleReceiptResponse ToReceipt(Sale sale) => new(sale.Id, sale.CreatedAtUtc, sale.Items.Select(item => new ReceiptItemResponse(item.ProductId, item.Quantity, ToAllocations(item))).ToList());
 static IReadOnlyList<BatchAllocationResponse> ToAllocations(SaleItem item) => item.Allocations.Select(a => new BatchAllocationResponse(a.StockBatchId, a.StockBatch?.BatchNumber ?? string.Empty, a.StockBatch?.ExpiryDate ?? default, a.Quantity)).ToList();
 static bool IsIdempotencyConflict(DbUpdateException e) => e.InnerException?.Message.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase) == true || e.Message.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase);
}