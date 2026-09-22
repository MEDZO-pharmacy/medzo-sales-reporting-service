using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<SalesDbContext>(options => options.UseSqlite(builder.Configuration.GetConnectionString("Sales") ?? "Data Source=medzo-sales.db"));
builder.Services.AddScoped<ISaleService, SaleService>();
builder.Services.AddHttpClient<ISaleItemSearchService, SaleItemSearchService>(client =>
{
 client.BaseAddress = new Uri(builder.Configuration["Services:CatalogueInventory:BaseUrl"] ?? "http://localhost:5082");
});

var app = builder.Build();
using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<SalesDbContext>().Database.EnsureCreatedAsync();
app.UseHttpsRedirection();
app.MapControllers();
app.Run();

public partial class Program;
