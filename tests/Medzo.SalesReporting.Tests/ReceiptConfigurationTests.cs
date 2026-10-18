using Medzo.SalesReporting.Contracts;
using Medzo.SalesReporting.Data;
using Medzo.SalesReporting.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace Medzo.SalesReporting.Tests;

public sealed class ReceiptConfigurationTests
{
    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public async Task Invalid_tax_rate_is_rejected_before_database_access(double rate)
    {
        // No database provider: any accidental database access makes this test fail.
        await using var db = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().Options);
        var settings = new Mock<IOptions<ReceiptOptions>>(MockBehavior.Strict);
        settings.SetupGet(o => o.Value).Returns(new ReceiptOptions { PharmacyName = "Medzo", TaxRate = (decimal)rate });
        var service = new SaleService(db, settings.Object);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new CreateSaleRequest("valid-key", [new("medicine-1", 1, 10m)]), CancellationToken.None));

        Assert.Equal("Receipt:TaxRate must be between 0 and 1.", error.Message);
        settings.VerifyGet(o => o.Value, Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Missing_pharmacy_name_prevents_sale_processing(string name)
    {
        await using var db = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().Options);
        var settings = new Mock<IOptions<ReceiptOptions>>();
        settings.SetupGet(o => o.Value).Returns(new ReceiptOptions { PharmacyName = name });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SaleService(db, settings.Object).CreateAsync(
            new CreateSaleRequest("valid-key", [new("medicine-1", 1)]), CancellationToken.None));

        Assert.Equal("Set Receipt:PharmacyName.", error.Message);
    }
}
