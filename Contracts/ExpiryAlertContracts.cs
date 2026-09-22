namespace Medzo.SalesReporting.Contracts;

public sealed record NearExpiryBatchResponse(Guid BatchId, string ProductId, string BatchNumber, DateOnly ExpiryDate, int DaysUntilExpiry, int RemainingQuantity);
public sealed record NearExpiryAlertResponse(IReadOnlyList<NearExpiryBatchResponse> Items, int Page, int PageSize, int TotalCount);