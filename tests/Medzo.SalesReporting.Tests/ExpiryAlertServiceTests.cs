using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Domain;
using Medzo.SalesReporting.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Medzo.SalesReporting.Tests;

public sealed class ExpiryAlertServiceTests
{
    static async Task<(SalesDbContext Db, SqliteConnection Connection)> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return (db, connection);
    }

    [Fact]
    public async Task Returns_active_batches_in_expiry_order()
    {
        var (db, connection) = await CreateDbAsync(); await using var _ = connection;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.StockBatches.AddRange(
            new StockBatch { ProductId = "p1", BatchNumber = "LATER", ExpiryDate = today.AddDays(20), RemainingQuantity = 5 },
            new StockBatch { ProductId = "p2", BatchNumber = "SOON", ExpiryDate = today.AddDays(5), RemainingQuantity = 3 },
            new StockBatch { ProductId = "p3", BatchNumber = "EXPIRED", ExpiryDate = today.AddDays(-1), RemainingQuantity = 9 },
            new StockBatch { ProductId = "p4", BatchNumber = "EMPTY", ExpiryDate = today.AddDays(4), RemainingQuantity = 0 });
        await db.SaveChangesAsync();

        var result = await new ExpiryAlertService(db).GetNearExpiryAsync(30, 1, 20, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Collection(result.Items, first => Assert.Equal("SOON", first.BatchNumber), second => Assert.Equal("LATER", second.BatchNumber));
    }

    [Fact]
    public async Task Is_read_only_when_retried()
    {
        var (db, connection) = await CreateDbAsync(); await using var _ = connection;
        db.StockBatches.Add(new StockBatch { ProductId = "p1", BatchNumber = "LOT", ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), RemainingQuantity = 7 });
        await db.SaveChangesAsync();
        var service = new ExpiryAlertService(db);

        var first = await service.GetNearExpiryAsync(30, 1, 20, CancellationToken.None);
        var retry = await service.GetNearExpiryAsync(30, 1, 20, CancellationToken.None);

        Assert.Equal(first.Items.Single().BatchId, retry.Items.Single().BatchId);
        Assert.Equal(7, await db.StockBatches.Select(batch => batch.RemainingQuantity).SingleAsync(CancellationToken.None));
    }
}