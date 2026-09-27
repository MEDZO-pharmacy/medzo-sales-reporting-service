namespace Medzo.SalesReporting.Contracts;

public sealed record CreateSaleRequest(
 string IdempotencyKey,
 IReadOnlyList<CreateSaleLine> Items,
 string? PharmacistName = null);

public sealed record CreateSaleLine(
 string ProductId,
 int Quantity,
 decimal UnitPrice = 0m,
 decimal DiscountAmount = 0m);

public sealed record SaleResponse(Guid SaleId, bool AlreadyProcessed, IReadOnlyList<SaleItemResponse> Items, SaleReceiptResponse Receipt);
public sealed record SaleItemResponse(string ProductId, int Quantity, IReadOnlyList<BatchAllocationResponse> BatchAllocations);
public sealed record BatchAllocationResponse(Guid BatchId, string BatchNumber, DateOnly ExpiryDate, int Quantity);

public sealed record SaleReceiptResponse(
 Guid SaleId,
 string ReceiptNumber,
 DateTime CompletedAtUtc,
 string PharmacyName,
 string PharmacistName,
 decimal TaxRate,
 decimal Subtotal,
 decimal Discount,
 decimal Tax,
 decimal GrandTotal,
 IReadOnlyList<ReceiptItemResponse> Items);

public sealed record ReceiptItemResponse(
 string ProductId,
 int Quantity,
 decimal UnitPrice,
 decimal Discount,
 decimal LineTotal,
 IReadOnlyList<BatchAllocationResponse> BatchAllocations);

public sealed record SaleItemSearchResponse(IReadOnlyList<SaleItemSearchResult> Items, int Page, int PageSize, int TotalCount);
public sealed record SaleItemSearchResult(Guid Id, string Name);