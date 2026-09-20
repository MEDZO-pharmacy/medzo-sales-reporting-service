using Medzo.SalesReporting.Domain;
using Microsoft.EntityFrameworkCore;
namespace Medzo.SalesReporting.Data;
public sealed class SalesDbContext(DbContextOptions<SalesDbContext> options) : DbContext(options)
{
 public DbSet<StockBatch> StockBatches => Set<StockBatch>(); public DbSet<Sale> Sales => Set<Sale>(); public DbSet<SaleItem> SaleItems => Set<SaleItem>(); public DbSet<BatchAllocation> BatchAllocations => Set<BatchAllocation>();
 protected override void OnModelCreating(ModelBuilder b) { b.Entity<Sale>().HasIndex(x => x.IdempotencyKey).IsUnique(); b.Entity<StockBatch>().ToTable(t => t.HasCheckConstraint("CK_StockBatch_RemainingQuantity", "RemainingQuantity >= 0")); b.Entity<SaleItem>().HasMany(x => x.Allocations).WithOne(x => x.SaleItem).HasForeignKey(x => x.SaleItemId); b.Entity<Sale>().HasMany(x => x.Items).WithOne(x => x.Sale).HasForeignKey(x => x.SaleId); }
}