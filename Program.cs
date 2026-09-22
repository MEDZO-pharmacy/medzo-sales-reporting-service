using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var salesConnection = builder.Configuration.GetConnectionString("Sales");

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<SalesDbContext>(options =>
{
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
builder.Services.AddScoped<IExpiryAlertService, ExpiryAlertService>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<SalesDbContext>().Database.EnsureCreatedAsync();

app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();

public partial class Program;
