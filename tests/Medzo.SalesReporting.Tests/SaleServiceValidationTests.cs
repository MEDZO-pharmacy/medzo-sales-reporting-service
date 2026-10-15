using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Services;
using Microsoft.EntityFrameworkCore;

namespace Medzo.SalesReporting.Tests;

public sealed class SaleServiceValidationTests
{
    public static TheoryData<CreateSaleRequest, string> InvalidRequests => new()
    {
        { new CreateSaleRequest("", [new CreateSaleLine("medicine-1", 1)]), "An idempotency key is required." },
        { new CreateSaleRequest("empty-items", []), "At least one sale item is required." },
        { new CreateSaleRequest("blank-product", [new CreateSaleLine(" ", 1)]), "Every sale item needs a product and a positive quantity." },
        { new CreateSaleRequest("zero-quantity", [new CreateSaleLine("medicine-1", 0)]), "Every sale item needs a product and a positive quantity." },
        { new CreateSaleRequest("negative-price", [new CreateSaleLine("medicine-1", 1, -1m)]), "A sale item cannot have a negative price or a discount greater than its line total." },
        { new CreateSaleRequest("excess-discount", [new CreateSaleLine("medicine-1", 2, 10m, 21m)]), "A sale item cannot have a negative price or a discount greater than its line total." },
        { new CreateSaleRequest("duplicate-product", [new CreateSaleLine("medicine-1", 1), new CreateSaleLine("medicine-1", 2)]), "Each product may appear only once per sale." }
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task CreateAsync_rejects_invalid_sale_requests_before_changing_stock(CreateSaleRequest request, string expectedMessage)
    {
        await using var db = CreateDb();
        var service = new SaleService(db);

        var exception = await Assert.ThrowsAsync<SaleValidationException>(
            () => service.CreateAsync(request, CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.Empty(await db.Sales.ToListAsync());
        Assert.Empty(await db.StockBatches.ToListAsync());
    }

    private static SalesDbContext CreateDb() => new(
        new DbContextOptionsBuilder<SalesDbContext>()
            .UseInMemoryDatabase($"sale-validation-{Guid.NewGuid()}")
            .Options);
}
