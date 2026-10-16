using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var salesConnection = builder.Configuration.GetConnectionString("Sales");
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

// Production must use the pre-provisioned Azure SQL database. Do not silently
// fall back to SQLite or attempt to create a database from the API container.
if (builder.Environment.IsProduction() &&
    !databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) &&
    !databaseProvider.Equals("AzureSql", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Production requires Database:Provider=SqlServer and the existing Azure SQL connection.");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Medzo Sales and Reporting API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter a Medzo JWT bearer token.", Name = "Authorization", In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        }] = Array.Empty<string>()
    });
});
builder.Services.AddHealthChecks();
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));
}
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<ReceiptOptions>(builder.Configuration.GetSection("Receipt"));
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
builder.Services.AddScoped<IExpiryAlertService, ExpiryAlertService>();
builder.Services.AddScoped<IBatchRemovalService, BatchRemovalService>();
builder.Services.AddHttpClient<ISaleItemSearchService, SaleItemSearchService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:CatalogueInventory:BaseUrl"] ?? "http://localhost:5082");
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var salesDb = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
    // EnsureCreated is intentionally limited to local SQLite development.
    // Never create an Azure SQL database/schema implicitly at container startup.
    if (salesDb.Database.IsSqlite() && !app.Environment.IsProduction())
    {
        await salesDb.Database.EnsureCreatedAsync();
        await salesDb.EnsureReceiptColumnsAsync();
    }
}

app.UseHttpsRedirection();
if (corsOrigins.Length > 0)
    app.UseCors("frontend");
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Medzo Sales and Reporting API v1"));
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();

public partial class Program;
