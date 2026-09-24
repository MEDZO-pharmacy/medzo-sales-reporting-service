using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Domain;
using Medzo.SalesReporting.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Medzo.SalesReporting.Tests;

public sealed class ExpiredBatchSaleTests
{
    static async Task<(SalesDbContext Db, SqliteConnection Connection)> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SalesDbContext>().UseSqlite(connection).Options;
        var db = new SalesDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (db, connection);
    }

    static StockBatch Batch(string productId, string batchNumber, DateOnly expiryDate, int quantity) => new()
    {
        ProductId = productId,
        BatchNumber = batchNumber,
        ExpiryDate = expiryDate,
        RemainingQuantity = quantity,
        CreatedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task Rejects_expired_only_stock_without_creating_or_deducting_a_sale()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        var expired = Batch("p1", "EXPIRED", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), 5);
        db.StockBatches.Add(expired);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<SaleValidationException>(() =>
            new SaleService(db).CreateAsync(new CreateSaleRequest("expired-only", [new CreateSaleLine("p1", 1)]), CancellationToken.None));

        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();
        Assert.Equal(5, await db.StockBatches.Where(x => x.Id == expired.Id).Select(x => x.RemainingQuantity).SingleAsync());
        Assert.Empty(await db.Sales.ToListAsync());
        Assert.Empty(await db.BatchAllocations.ToListAsync());
    }

    [Fact]
    public async Task Uses_sellable_stock_and_never_allocates_an_expired_batch()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        var expired = Batch("p1", "EXPIRED", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), 5);
        var sellable = Batch("p1", "SELLABLE", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), 5);
        db.StockBatches.AddRange(expired, sellable);
        await db.SaveChangesAsync();

        var result = await new SaleService(db).CreateAsync(
            new CreateSaleRequest("sellable-with-expired", [new CreateSaleLine("p1", 2)]), CancellationToken.None);

        var allocation = Assert.Single(Assert.Single(result.Items).BatchAllocations);
        Assert.Equal(sellable.Id, allocation.BatchId);
        Assert.Equal(2, allocation.Quantity);
        db.ChangeTracker.Clear();
        Assert.Equal(5, await db.StockBatches.Where(x => x.Id == expired.Id).Select(x => x.RemainingQuantity).SingleAsync());
        Assert.Equal(3, await db.StockBatches.Where(x => x.Id == sellable.Id).Select(x => x.RemainingQuantity).SingleAsync());
    }
}