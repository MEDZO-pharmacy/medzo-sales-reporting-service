namespace Medzo.SalesReporting.Contracts;

public sealed record BatchRemovalCandidateResponse(Guid BatchId, string ProductId, string BatchNumber, DateOnly ExpiryDate, int DaysUntilExpiry, int RemainingQuantity, bool IsExpired);
public sealed record BatchRemovalCandidatesResponse(IReadOnlyList<BatchRemovalCandidateResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record RemoveBatchRequest(string IdempotencyKey, string Reason, string RemovedBy);
public sealed record BatchRemovalResponse(Guid BatchId, string ProductId, string BatchNumber, int RemovedQuantity, string Reason, string RemovedBy, DateTime RemovedAtUtc, bool AlreadyRemoved);