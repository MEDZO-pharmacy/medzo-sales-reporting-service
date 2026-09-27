using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Domain;
using Medzo.SalesReporting.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Medzo.SalesReporting.Tests;

public sealed class BatchRemovalServiceTests
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

    static StockBatch Batch(DateOnly expiry, int quantity = 8) => new()
    {
        ProductId = "p1",
        BatchNumber = "LOT-001",
        ExpiryDate = expiry,
        RemainingQuantity = quantity,
        CreatedAtUtc = DateTime.UtcNow
    };

    static RemoveBatchRequest Request(string key = "remove-1") => new(key, "Expired", "I1001");

    [Fact]
    public async Task Removes_an_expired_batch_and_records_a_single_audit_entry()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        var batch = Batch(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        db.StockBatches.Add(batch);
        await db.SaveChangesAsync();

        var result = await new BatchRemovalService(db).RemoveAsync(batch.Id, Request(), CancellationToken.None);

        Assert.False(result.AlreadyRemoved);
        Assert.Equal(8, result.RemovedQuantity);
        db.ChangeTracker.Clear();
        var storedBatch = await db.StockBatches.SingleAsync(x => x.Id == batch.Id);
        var audit = await db.BatchRemovalAudits.SingleAsync(x => x.BatchId == batch.Id);
        Assert.True(storedBatch.IsRemoved);
        Assert.Equal(0, storedBatch.RemainingQuantity);
        Assert.Equal("Expired", audit.Reason);
        Assert.Equal("I1001", audit.RemovedBy);
    }

    [Fact]
    public async Task Keeps_historical_sale_allocations_after_soft_removal()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        var batch = Batch(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)));
        var sale = new Sale { IdempotencyKey = "past-sale" };
        var item = new SaleItem { Sale = sale, ProductId = "p1", Quantity = 2 };
        item.Allocations.Add(new BatchAllocation { StockBatch = batch, Quantity = 2 });
        sale.Items.Add(item);
        db.Sales.Add(sale);
        await db.SaveChangesAsync();

        await new BatchRemovalService(db).RemoveAsync(batch.Id, Request(), CancellationToken.None);

        db.ChangeTracker.Clear();
        var allocation = await db.BatchAllocations.Include(x => x.StockBatch).SingleAsync();
        Assert.Equal("LOT-001", allocation.StockBatch!.BatchNumber);
        Assert.True(allocation.StockBatch.IsRemoved);
    }

    [Fact]
    public async Task Rejects_a_batch_outside_the_near_expiry_window_without_changing_stock()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        var batch = Batch(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(31)));
        db.StockBatches.Add(batch);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<BatchRemovalValidationException>(() =>
            new BatchRemovalService(db).RemoveAsync(batch.Id, Request(), CancellationToken.None));

        Assert.Contains("next 30 days", exception.Message);
        db.ChangeTracker.Clear();
        Assert.Equal(8, await db.StockBatches.Where(x => x.Id == batch.Id).Select(x => x.RemainingQuantity).SingleAsync());
        Assert.Empty(await db.BatchRemovalAudits.ToListAsync());
    }

    [Fact]
    public async Task Repeated_removal_returns_the_original_audit_without_duplicate_deduction()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        var batch = Batch(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));
        db.StockBatches.Add(batch);
        await db.SaveChangesAsync();
        var service = new BatchRemovalService(db);

        var first = await service.RemoveAsync(batch.Id, Request("remove-once"), CancellationToken.None);
        var retry = await service.RemoveAsync(batch.Id, Request("remove-once"), CancellationToken.None);

        Assert.False(first.AlreadyRemoved);
        Assert.True(retry.AlreadyRemoved);
        Assert.Equal(first.RemovedAtUtc, retry.RemovedAtUtc);
        db.ChangeTracker.Clear();
        Assert.Single(await db.BatchRemovalAudits.ToListAsync());
        Assert.Equal(0, await db.StockBatches.Where(x => x.Id == batch.Id).Select(x => x.RemainingQuantity).SingleAsync());
    }
}