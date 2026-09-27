using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Domain;
using Medzo.SalesReporting.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Medzo.SalesReporting.Tests;
public sealed class SaleServiceTests
{
 static async Task<(SalesDbContext db, SqliteConnection connection)> CreateDbAsync()
 {
  var connection=new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
  var options=new DbContextOptionsBuilder<SalesDbContext>().UseSqlite(connection).Options;
  var db=new SalesDbContext(options); await db.Database.EnsureCreatedAsync(); return(db,connection);
 }
 static StockBatch Batch(string product,string number,DateOnly expiry,int quantity,DateTime? created=null)=>new(){ProductId=product,BatchNumber=number,ExpiryDate=expiry,RemainingQuantity=quantity,CreatedAtUtc=created??DateTime.UtcNow};
 [Fact]
 public async Task Uses_earliest_expiry_batch_first()
 {
  var (db,connection)=await CreateDbAsync(); await using var _=connection;
  var early=Batch("p1","EARLY",DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)),20); var late=Batch("p1","LATE",DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(5)),50); db.StockBatches.AddRange(early,late);await db.SaveChangesAsync();
  var result=await new SaleService(db).CreateAsync(new("sale-1",[new("p1",10)]),CancellationToken.None);
  Assert.Single(result.Items.Single().BatchAllocations); Assert.Equal(early.Id,result.Items.Single().BatchAllocations.Single().BatchId); Assert.Equal(10,await db.StockBatches.Where(x=>x.Id==early.Id).Select(x=>x.RemainingQuantity).SingleAsync()); Assert.Equal(50,await db.StockBatches.Where(x=>x.Id==late.Id).Select(x=>x.RemainingQuantity).SingleAsync());
 }
 [Fact]
 public async Task Allocates_across_batches_in_expiry_order()
 {
  var (db,connection)=await CreateDbAsync(); await using var _=connection;
  var first=Batch("p1","A",DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)),10);var second=Batch("p1","B",DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),20);db.StockBatches.AddRange(first,second);await db.SaveChangesAsync();
  var result=await new SaleService(db).CreateAsync(new("sale-2",[new("p1",25)]),CancellationToken.None);
  var allocations=result.Items.Single().BatchAllocations; Assert.Collection(allocations,a=>{Assert.Equal(first.Id,a.BatchId);Assert.Equal(10,a.Quantity);},a=>{Assert.Equal(second.Id,a.BatchId);Assert.Equal(15,a.Quantity);}); Assert.Equal(0,await db.StockBatches.Where(x=>x.Id==first.Id).Select(x=>x.RemainingQuantity).SingleAsync());Assert.Equal(5,await db.StockBatches.Where(x=>x.Id==second.Id).Select(x=>x.RemainingQuantity).SingleAsync());
 }
 [Fact]
 public async Task Ignores_zero_and_expired_batches()
 {
  var (db,connection)=await CreateDbAsync(); await using var _=connection;
  var expired=Batch("p1","OLD",DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),30);var zero=Batch("p1","ZERO",DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),0);var valid=Batch("p1","VALID",DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)),10);db.StockBatches.AddRange(expired,zero,valid);await db.SaveChangesAsync();
  var result=await new SaleService(db).CreateAsync(new("sale-3",[new("p1",5)]),CancellationToken.None);
  Assert.Equal(valid.Id,result.Items.Single().BatchAllocations.Single().BatchId); Assert.Equal(30,await db.StockBatches.Where(x=>x.Id==expired.Id).Select(x=>x.RemainingQuantity).SingleAsync());
 }
 [Fact]
 public async Task Insufficient_stock_rolls_back_everything()
 {
  var (db,connection)=await CreateDbAsync(); await using var _=connection;
  var batch=Batch("p1","A",DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)),10);db.StockBatches.Add(batch);await db.SaveChangesAsync();
  await Assert.ThrowsAsync<SaleValidationException>(()=>new SaleService(db).CreateAsync(new("sale-4",[new("p1",11)]),CancellationToken.None));
  db.ChangeTracker.Clear(); Assert.Equal(10,await db.StockBatches.Where(x=>x.Id==batch.Id).Select(x=>x.RemainingQuantity).SingleAsync()); Assert.Empty(await db.Sales.ToListAsync()); Assert.Empty(await db.BatchAllocations.ToListAsync());
 }
 [Fact]
 public async Task Same_idempotency_key_does_not_deduct_stock_twice()
 {
  var (db,connection)=await CreateDbAsync(); await using var _=connection;
  var batch=Batch("p1","A",DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)),10);db.StockBatches.Add(batch);await db.SaveChangesAsync();var service=new SaleService(db);var request=new CreateSaleRequest("same-request",[new("p1",4)]);
  var first=await service.CreateAsync(request,CancellationToken.None);var retry=await service.CreateAsync(request,CancellationToken.None);
  db.ChangeTracker.Clear();Assert.Equal(first.SaleId,retry.SaleId);Assert.True(retry.AlreadyProcessed);Assert.Equal(6,await db.StockBatches.Where(x=>x.Id==batch.Id).Select(x=>x.RemainingQuantity).SingleAsync());Assert.Single(await db.Sales.ToListAsync());
 }

 [Fact]
 public async Task Stores_guid_keys_as_text_and_updates_batches_successfully()
 {
  var (db, connection) = await CreateDbAsync(); await using var _ = connection;
  var batch = Batch("p-text", "TEXT-ID", DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)), 8);
  db.StockBatches.Add(batch);
  await db.SaveChangesAsync();

  await using var command = db.Database.GetDbConnection().CreateCommand();
  command.CommandText = "SELECT typeof(\"Id\") FROM \"StockBatches\" LIMIT 1;";
  Assert.Equal("text", (string?)await command.ExecuteScalarAsync());

  var result = await new SaleService(db).CreateAsync(new("sale-text", [new("p-text", 3)]), CancellationToken.None);
  Assert.False(result.AlreadyProcessed);
  db.ChangeTracker.Clear();
  Assert.Equal(5, await db.StockBatches.Where(x => x.Id == batch.Id).Select(x => x.RemainingQuantity).SingleAsync());
 }
}
