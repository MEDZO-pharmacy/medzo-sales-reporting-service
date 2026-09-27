using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Medzo.SalesReporting.Data;

public static class SalesReceiptSchemaUpgrader
{
 public static async Task EnsureReceiptColumnsAsync(this SalesDbContext db, CancellationToken ct = default)
 {
  var provider = db.Database.ProviderName ?? string.Empty;
  if (!IsSqlite(provider) && !IsMySql(provider) && !IsSqlServer(provider)) return;

  var connection = db.Database.GetDbConnection();
  if (connection.State != System.Data.ConnectionState.Open)
   await connection.OpenAsync(ct);

  await AddMissingColumnsAsync(connection, "Sales", new Dictionary<string, string>
  {
   ["ReceiptNumber"] = IsMySql(provider) ? "varchar(64) NOT NULL DEFAULT ''" : IsSqlServer(provider) ? "nvarchar(64) NOT NULL DEFAULT ''" : "TEXT NOT NULL DEFAULT ''",
   ["PharmacyName"] = IsMySql(provider) ? "varchar(200) NOT NULL DEFAULT ''" : IsSqlServer(provider) ? "nvarchar(200) NOT NULL DEFAULT ''" : "TEXT NOT NULL DEFAULT ''",
   ["PharmacistName"] = IsMySql(provider) ? "varchar(200) NOT NULL DEFAULT ''" : IsSqlServer(provider) ? "nvarchar(200) NOT NULL DEFAULT ''" : "TEXT NOT NULL DEFAULT ''",
   ["TaxRate"] = IsSqlite(provider) ? "TEXT NOT NULL DEFAULT '0'" : "decimal(8,4) NOT NULL DEFAULT 0",
  }, ct);

  await AddMissingColumnsAsync(connection, "SaleItems", new Dictionary<string, string>
  {
   ["UnitPrice"] = IsSqlite(provider) ? "TEXT NOT NULL DEFAULT '0'" : "decimal(18,2) NOT NULL DEFAULT 0",
   ["DiscountAmount"] = IsSqlite(provider) ? "TEXT NOT NULL DEFAULT '0'" : "decimal(18,2) NOT NULL DEFAULT 0",
  }, ct);

  await AddMissingColumnsAsync(connection, "StockBatches", new Dictionary<string, string>
  {
   ["IsRemoved"] = IsMySql(provider) ? "tinyint(1) NOT NULL DEFAULT 0" : IsSqlServer(provider) ? "bit NOT NULL DEFAULT 0" : "INTEGER NOT NULL DEFAULT 0",
   ["RemovedAtUtc"] = IsMySql(provider) ? "datetime(6) NULL" : IsSqlServer(provider) ? "datetime2 NULL" : "TEXT NULL",
   ["RemovedBy"] = IsMySql(provider) ? "varchar(128) NULL" : IsSqlServer(provider) ? "nvarchar(128) NULL" : "TEXT NULL",
   ["RemovalReason"] = IsMySql(provider) ? "varchar(500) NULL" : IsSqlServer(provider) ? "nvarchar(500) NULL" : "TEXT NULL",
  }, ct);

  await EnsureBatchRemovalAuditTableAsync(connection, provider, ct);
 }

 private static async Task EnsureBatchRemovalAuditTableAsync(DbConnection connection, string provider, CancellationToken ct)
 {
  var sql = IsSqlServer(provider)
   ? "IF OBJECT_ID(N'[BatchRemovalAudits]', N'U') IS NULL CREATE TABLE [BatchRemovalAudits] ([Id] nvarchar(36) NOT NULL PRIMARY KEY, [BatchId] nvarchar(36) NOT NULL, [ProductId] nvarchar(max) NOT NULL, [BatchNumber] nvarchar(max) NOT NULL, [RemovedQuantity] int NOT NULL, [Reason] nvarchar(500) NOT NULL, [RemovedBy] nvarchar(128) NOT NULL, [RemovedAtUtc] datetime2 NOT NULL); IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BatchRemovalAudits_BatchId' AND object_id = OBJECT_ID(N'[BatchRemovalAudits]')) CREATE UNIQUE INDEX [IX_BatchRemovalAudits_BatchId] ON [BatchRemovalAudits] ([BatchId]);"
   : IsMySql(provider)
    ? "CREATE TABLE IF NOT EXISTS `BatchRemovalAudits` (`Id` char(36) NOT NULL PRIMARY KEY, `BatchId` char(36) NOT NULL, `ProductId` longtext NOT NULL, `BatchNumber` longtext NOT NULL, `RemovedQuantity` int NOT NULL, `Reason` varchar(500) NOT NULL, `RemovedBy` varchar(128) NOT NULL, `RemovedAtUtc` datetime(6) NOT NULL, UNIQUE KEY `IX_BatchRemovalAudits_BatchId` (`BatchId`));"
    : "CREATE TABLE IF NOT EXISTS \"BatchRemovalAudits\" (\"Id\" TEXT NOT NULL PRIMARY KEY, \"BatchId\" TEXT NOT NULL, \"ProductId\" TEXT NOT NULL, \"BatchNumber\" TEXT NOT NULL, \"RemovedQuantity\" INTEGER NOT NULL, \"Reason\" TEXT NOT NULL, \"RemovedBy\" TEXT NOT NULL, \"RemovedAtUtc\" TEXT NOT NULL); CREATE UNIQUE INDEX IF NOT EXISTS \"IX_BatchRemovalAudits_BatchId\" ON \"BatchRemovalAudits\" (\"BatchId\");";
  await using var command = connection.CreateCommand();
  command.CommandText = sql;
  await command.ExecuteNonQueryAsync(ct);
 }

 private static async Task AddMissingColumnsAsync(DbConnection connection, string table, IReadOnlyDictionary<string, string> columns, CancellationToken ct)
 {
  var existing = await GetColumnNamesAsync(connection, table, ct);
  foreach (var (name, typeDefinition) in columns.Where(column => !existing.Contains(column.Key)))
  {
   await using var command = connection.CreateCommand();
   command.CommandText = $"ALTER TABLE {QuoteIdentifier(connection, table)} ADD COLUMN {QuoteIdentifier(connection, name)} {typeDefinition};";
   await command.ExecuteNonQueryAsync(ct);
  }
 }

 private static async Task<HashSet<string>> GetColumnNamesAsync(DbConnection connection, string table, CancellationToken ct)
 {
  await using var command = connection.CreateCommand();
  command.CommandText = IsMySql(connection)
   ? "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table;"
   : IsSqlServer(connection)
    ? "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @table;"
    : $"PRAGMA table_info({QuoteIdentifier(connection, table)});";

  if (IsMySql(connection) || IsSqlServer(connection))
  {
   var parameter = command.CreateParameter();
   parameter.ParameterName = "@table";
   parameter.Value = table;
   command.Parameters.Add(parameter);
  }

  var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  await using var reader = await command.ExecuteReaderAsync(ct);
  while (await reader.ReadAsync(ct))
   names.Add(reader.GetString(IsMySql(connection) || IsSqlServer(connection) ? 0 : 1));
  return names;
 }

 private static bool IsSqlite(string provider) => provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
 private static bool IsMySql(string provider) => provider.Contains("MySql", StringComparison.OrdinalIgnoreCase);
 private static bool IsSqlServer(string provider) => provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
 private static bool IsMySql(DbConnection connection) => connection.GetType().Namespace?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;
 private static bool IsSqlServer(DbConnection connection) => connection.GetType().Namespace?.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) == true;
 private static string QuoteIdentifier(DbConnection connection, string identifier) => IsMySql(connection) ? $"`{identifier}`" : IsSqlServer(connection) ? $"[{identifier}]" : $"\"{identifier}\"";
}
