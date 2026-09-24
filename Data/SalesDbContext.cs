using Medzo.SalesReporting.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Medzo.SalesReporting.Data;

public sealed class SalesDbContext(DbContextOptions<SalesDbContext> options) : DbContext(options)
{
 public DbSet<StockBatch> StockBatches => Set<StockBatch>();
 public DbSet<Sale> Sales => Set<Sale>();
 public DbSet<SaleItem> SaleItems => Set<SaleItem>();
 public DbSet<BatchAllocation> BatchAllocations => Set<BatchAllocation>();
 public DbSet<BatchRemovalAudit> BatchRemovalAudits => Set<BatchRemovalAudit>();

 protected override void OnModelCreating(ModelBuilder b)
 {
  var guidAsText = new GuidToStringConverter();
  var isMySql = Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;
  var guidColumnType = isMySql ? "char(36)" : "TEXT";

  b.Entity<StockBatch>(entity =>
  {
   entity.Property(x => x.Id).HasConversion(guidAsText).HasColumnType(guidColumnType);
   entity.ToTable(t => t.HasCheckConstraint("CK_StockBatch_RemainingQuantity", "RemainingQuantity >= 0"));
  });
  b.Entity<Sale>(entity =>
  {
   entity.Property(x => x.Id).HasConversion(guidAsText).HasColumnType(guidColumnType);
   entity.Property(x => x.IdempotencyKey).HasMaxLength(128);
   entity.HasIndex(x => x.IdempotencyKey).IsUnique();
   entity.HasMany(x => x.Items).WithOne(x => x.Sale).HasForeignKey(x => x.SaleId);
  });
  b.Entity<SaleItem>(entity =>
  {
   entity.Property(x => x.Id).HasConversion(guidAsText).HasColumnType(guidColumnType);
   entity.Property(x => x.SaleId).HasConversion(guidAsText).HasColumnType(guidColumnType);
   entity.HasMany(x => x.Allocations).WithOne(x => x.SaleItem).HasForeignKey(x => x.SaleItemId);
  });
  b.Entity<BatchAllocation>(entity =>
  {
   entity.Property(x => x.Id).HasConversion(guidAsText).HasColumnType(guidColumnType);
   entity.Property(x => x.SaleItemId).HasConversion(guidAsText).HasColumnType(guidColumnType);
   entity.Property(x => x.StockBatchId).HasConversion(guidAsText).HasColumnType(guidColumnType);
   entity.HasOne(x => x.StockBatch).WithMany().HasForeignKey(x => x.StockBatchId);
  });
  b.Entity<BatchRemovalAudit>(entity =>
  {
   entity.Property(x => x.Id).HasConversion(guidAsText).HasColumnType(guidColumnType);
   entity.Property(x => x.BatchId).HasConversion(guidAsText).HasColumnType(guidColumnType);
   entity.Property(x => x.Reason).HasMaxLength(500);
   entity.Property(x => x.RemovedBy).HasMaxLength(128);
   entity.HasIndex(x => x.BatchId).IsUnique();
  });
 }
}