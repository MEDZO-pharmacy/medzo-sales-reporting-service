using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Domain;
using Microsoft.EntityFrameworkCore;

var productIds = args.Where(arg => !string.IsNullOrWhiteSpace(arg)).Distinct(StringComparer.Ordinal).ToList();
if (productIds.Count == 0)
    throw new InvalidOperationException("Pass at least one catalogue medicine ID as an argument.");

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Sales")
    ?? throw new InvalidOperationException("ConnectionStrings__Sales is not configured.");
var provider = Environment.GetEnvironmentVariable("Database__Provider") ?? "SqlServer";

var optionsBuilder = new DbContextOptionsBuilder<SalesDbContext>();
if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    optionsBuilder.UseSqlite(connectionString);
else if (provider.Equals("MySql", StringComparison.OrdinalIgnoreCase))
    optionsBuilder.UseMySQL(connectionString);
else
    optionsBuilder.UseSqlServer(connectionString, sqlServer => sqlServer.EnableRetryOnFailure());

await using var database = new SalesDbContext(optionsBuilder.Options);
await database.Database.EnsureCreatedAsync();
await database.EnsureReceiptColumnsAsync();

var now = DateTime.UtcNow;
foreach (var productId in productIds)
{
    var batchNumber = $"DEMO-SALES-{ShortId(productId)}";
    var exists = await database.StockBatches.AnyAsync(batch => batch.ProductId == productId && batch.BatchNumber == batchNumber);
    if (exists) continue;
    await database.StockBatches.AddAsync(new StockBatch
    {
        Id = Guid.NewGuid(),
        ProductId = productId,
        BatchNumber = batchNumber,
        ExpiryDate = DateOnly.FromDateTime(now.AddMonths(18)),
        RemainingQuantity = 60,
        CreatedAtUtc = now,
    });
}

await database.SaveChangesAsync();

var firstProductId = productIds[0];
if (!await database.Sales.AnyAsync(sale => sale.IdempotencyKey == "demo-sale-001"))
{
    var batch = await database.StockBatches
        .Where(item => item.ProductId == firstProductId && item.RemainingQuantity >= 2)
        .OrderBy(item => item.ExpiryDate)
        .FirstAsync();
    batch.RemainingQuantity -= 2;
    var saleItem = new SaleItem
    {
        Id = Guid.NewGuid(),
        ProductId = firstProductId,
        Quantity = 2,
        UnitPrice = 10m,
        DiscountAmount = 0m,
    };
    saleItem.Allocations.Add(new BatchAllocation
    {
        Id = Guid.NewGuid(),
        StockBatchId = batch.Id,
        Quantity = 2,
    });
    await database.Sales.AddAsync(new Sale
    {
        Id = Guid.NewGuid(),
        IdempotencyKey = "demo-sale-001",
        ReceiptNumber = "RCT-DEMO-001",
        PharmacyName = "Medzo Pharmacy",
        PharmacistName = "Demo Pharmacist",
        TaxRate = 0m,
        CreatedAtUtc = now,
        Items = [saleItem],
    });
    await database.SaveChangesAsync();
}

Console.WriteLine($"Sales sample data was seeded for {productIds.Count} product(s).");

static string ShortId(string value)
{
    var clean = new string(value.Where(char.IsLetterOrDigit).Take(8).ToArray());
    return clean.Length == 0 ? "ITEM" : clean.ToUpperInvariant();
}
