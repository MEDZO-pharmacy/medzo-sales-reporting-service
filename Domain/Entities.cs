namespace Medzo.SalesReporting.Domain;

public sealed class StockBatch
{
 public Guid Id { get; set; } = Guid.NewGuid();
 public required string ProductId { get; set; }
 public required string BatchNumber { get; set; }
 public DateOnly ExpiryDate { get; set; }
 public int RemainingQuantity { get; set; }
 public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Sale
{
 public Guid Id { get; set; } = Guid.NewGuid();
 public required string IdempotencyKey { get; set; }
 public required string ReceiptNumber { get; set; }
 public required string PharmacyName { get; set; }
 public required string PharmacistName { get; set; }
 public decimal TaxRate { get; set; }
 public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
 public List<SaleItem> Items { get; set; } = [];
}

public sealed class SaleItem
{
 public Guid Id { get; set; } = Guid.NewGuid();
 public Guid SaleId { get; set; }
 public Sale? Sale { get; set; }
 public required string ProductId { get; set; }
 public int Quantity { get; set; }
 public decimal UnitPrice { get; set; }
 public decimal DiscountAmount { get; set; }
 public List<BatchAllocation> Allocations { get; set; } = [];
}

public sealed class BatchAllocation
{
 public Guid Id { get; set; } = Guid.NewGuid();
 public Guid SaleItemId { get; set; }
 public SaleItem? SaleItem { get; set; }
 public Guid StockBatchId { get; set; }
 public StockBatch? StockBatch { get; set; }
 public int Quantity { get; set; }
}