namespace Medzo.SalesReporting.Contracts;

public sealed record CreateSaleRequest(string IdempotencyKey, IReadOnlyList<CreateSaleLine> Items);
public sealed record CreateSaleLine(string ProductId, int Quantity);
public sealed record SaleResponse(Guid SaleId, bool AlreadyProcessed, IReadOnlyList<SaleItemResponse> Items);
public sealed record SaleItemResponse(string ProductId, int Quantity, IReadOnlyList<BatchAllocationResponse> BatchAllocations);
public sealed record BatchAllocationResponse(Guid BatchId, string BatchNumber, DateOnly ExpiryDate, int Quantity);
public sealed record SaleItemSearchResponse(IReadOnlyList<SaleItemSearchResult> Items, int Page, int PageSize, int TotalCount);
public sealed record SaleItemSearchResult(Guid Id, string Name);
