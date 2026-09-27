using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Medzo.SalesReporting.Services;

public sealed class SaleValidationException(string message) : Exception(message);
public sealed class SaleConflictException(string message) : Exception(message);
public interface ISaleService
{
 Task<SaleResponse> CreateAsync(CreateSaleRequest request, CancellationToken ct);
 Task<SaleReceiptResponse?> GetReceiptAsync(Guid saleId, CancellationToken ct);
}

public sealed class SaleService(SalesDbContext db, IOptions<ReceiptOptions>? receiptOptions = null) : ISaleService
{
 public async Task<SaleResponse> CreateAsync(CreateSaleRequest request, CancellationToken ct)
 {
  Validate(request);
  var receiptSettings = receiptOptions?.Value ?? new ReceiptOptions();
  ValidateReceiptSettings(receiptSettings);
  var strategy = db.Database.CreateExecutionStrategy();
  return await strategy.ExecuteAsync(async () =>
  {
   await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
   var existing = await FindByKeyAsync(request.IdempotencyKey, ct);
   if (existing is not null)
   {
    await transaction.CommitAsync(ct);
    return ToResponse(existing, true);
   }

   var sale = new Sale
   {
    IdempotencyKey = request.IdempotencyKey,
    ReceiptNumber = CreateReceiptNumber(),
    PharmacyName = receiptSettings.PharmacyName.Trim(),
    PharmacistName = string.IsNullOrWhiteSpace(request.PharmacistName) ? "Not recorded" : request.PharmacistName.Trim(),
    TaxRate = receiptSettings.TaxRate,
   };
   foreach (var line in request.Items)
   {
    var batches = await db.StockBatches
     .Where(x => x.ProductId == line.ProductId && x.RemainingQuantity > 0 && x.ExpiryDate >= DateOnly.FromDateTime(DateTime.UtcNow))
     .OrderBy(x => x.ExpiryDate).ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
     .ToListAsync(ct);

    if (batches.Sum(x => x.RemainingQuantity) < line.Quantity)
     throw new SaleValidationException($"Insufficient sellable stock for product '{line.ProductId}'.");

    var item = new SaleItem
    {
     ProductId = line.ProductId,
     Quantity = line.Quantity,
     UnitPrice = line.UnitPrice,
     DiscountAmount = line.DiscountAmount,
    };
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
  });
 }

 public async Task<SaleReceiptResponse?> GetReceiptAsync(Guid saleId, CancellationToken ct)
 {
  var sale = await FindByIdAsync(saleId, ct);
  return sale is null ? null : ToReceipt(sale);
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
  if (r.Items.Any(x => x.UnitPrice < 0 || x.DiscountAmount < 0 || x.DiscountAmount > x.UnitPrice * x.Quantity)) throw new SaleValidationException("A sale item cannot have a negative price or a discount greater than its line total.");
  if (r.Items.Select(x => x.ProductId).Distinct(StringComparer.Ordinal).Count() != r.Items.Count) throw new SaleValidationException("Each product may appear only once per sale.");
 }

 static void ValidateReceiptSettings(ReceiptOptions options)
 {
  if (string.IsNullOrWhiteSpace(options.PharmacyName)) throw new InvalidOperationException("Set Receipt:PharmacyName.");
  if (options.TaxRate < 0 || options.TaxRate > 1) throw new InvalidOperationException("Receipt:TaxRate must be between 0 and 1.");
 }

 static string CreateReceiptNumber() => $"RCT-{Guid.NewGuid():N}".ToUpperInvariant();
 Task<Sale?> FindByKeyAsync(string key, CancellationToken ct) => db.Sales.Include(x => x.Items).ThenInclude(x => x.Allocations).ThenInclude(x => x.StockBatch).SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
 Task<Sale?> FindByIdAsync(Guid saleId, CancellationToken ct) => db.Sales.Include(x => x.Items).ThenInclude(x => x.Allocations).ThenInclude(x => x.StockBatch).SingleOrDefaultAsync(x => x.Id == saleId, ct);
 async Task<SaleResponse> GetResponseAsync(Guid id, bool done, CancellationToken ct) => ToResponse(await FindByIdAsync(id, ct) ?? throw new InvalidOperationException("Completed sale was not found."), done);
 static SaleResponse ToResponse(Sale sale, bool done) => new(sale.Id, done, sale.Items.Select(ToSaleItem).ToList(), ToReceipt(sale));
 static SaleItemResponse ToSaleItem(SaleItem item) => new(item.ProductId, item.Quantity, ToAllocations(item));
 static SaleReceiptResponse ToReceipt(Sale sale)
 {
  var items = sale.Items.Select(item =>
  {
   var lineSubtotal = item.UnitPrice * item.Quantity;
   var lineTotal = RoundMoney(lineSubtotal - item.DiscountAmount);
   return new ReceiptItemResponse(item.ProductId, item.Quantity, item.UnitPrice, item.DiscountAmount, lineTotal, ToAllocations(item));
  }).ToList();
  var subtotal = RoundMoney(sale.Items.Sum(item => item.UnitPrice * item.Quantity));
  var discount = RoundMoney(sale.Items.Sum(item => item.DiscountAmount));
  var tax = RoundMoney((subtotal - discount) * sale.TaxRate);
  return new SaleReceiptResponse(
   sale.Id,
   sale.ReceiptNumber,
   sale.CreatedAtUtc,
   sale.PharmacyName,
   sale.PharmacistName,
   sale.TaxRate,
   subtotal,
   discount,
   tax,
   RoundMoney(subtotal - discount + tax),
   items);
 }

 static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
 static IReadOnlyList<BatchAllocationResponse> ToAllocations(SaleItem item) => item.Allocations.Select(a => new BatchAllocationResponse(a.StockBatchId, a.StockBatch?.BatchNumber ?? string.Empty, a.StockBatch?.ExpiryDate ?? default, a.Quantity)).ToList();
 static bool IsIdempotencyConflict(DbUpdateException e) => e.InnerException?.Message.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase) == true || e.Message.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase);
}