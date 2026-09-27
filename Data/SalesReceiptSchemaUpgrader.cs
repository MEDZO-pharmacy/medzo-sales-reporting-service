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