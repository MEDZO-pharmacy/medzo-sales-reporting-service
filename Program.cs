using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var salesConnection = builder.Configuration.GetConnectionString("Sales");

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<ReceiptOptions>(builder.Configuration.GetSection("Receipt"));
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<SalesDbContext>(options =>
{
    if (databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) || databaseProvider.Equals("AzureSql", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(salesConnection))
            throw new InvalidOperationException("Set ConnectionStrings__Sales when Database__Provider is SqlServer.");

        options.UseSqlServer(salesConnection, sqlServer => sqlServer.EnableRetryOnFailure());
        return;
    }

    if (databaseProvider.Equals("MySql", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(salesConnection))
            throw new InvalidOperationException("Set ConnectionStrings__Sales when Database__Provider is MySql.");

        options.UseMySQL(salesConnection, mysql => mysql.EnableRetryOnFailure());
        return;
    }

    options.UseSqlite(salesConnection ?? "Data Source=medzo-sales.db");
});
builder.Services.AddScoped<ISaleService, SaleService>();
builder.Services.AddHttpClient<ISaleItemSearchService, SaleItemSearchService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:CatalogueInventory:BaseUrl"] ?? "http://localhost:5082");
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var salesDb = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
    await salesDb.Database.EnsureCreatedAsync();
    await salesDb.EnsureReceiptColumnsAsync();
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();

public partial class Program;