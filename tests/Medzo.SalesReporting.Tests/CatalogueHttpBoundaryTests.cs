using System.Net;
using System.Net.Http.Json;
using Medzo.SalesReporting.Services;
using Microsoft.AspNetCore.Http;
using Moq;
using Moq.Protected;

namespace Medzo.SalesReporting.Tests;

public sealed class CatalogueHttpBoundaryTests
{
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Unsuccessful_catalogue_responses_return_safe_error_without_retrying(int status)
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage((HttpStatusCode)status));
        using var client = new HttpClient(handler.Object) { BaseAddress = new Uri("https://catalogue.test/") };
        var service = new SaleItemSearchService(client, Mock.Of<IHttpContextAccessor>());

        var error = await Assert.ThrowsAsync<SaleItemSearchUnavailableException>(() => service.SearchAsync("medicine", 1, 20, CancellationToken.None));

        Assert.Equal("Medicine search is temporarily unavailable. Please try again.", error.Message);
        handler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Catalogue_results_preserve_identity_and_server_pagination()
    {
        var id = Guid.NewGuid();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { items = new[] { new { id, name = "Paracetamol" } }, page = 2, pageSize = 10, totalCount = 25 })
            });
        using var client = new HttpClient(handler.Object) { BaseAddress = new Uri("https://catalogue.test/") };

        var result = await new SaleItemSearchService(client, Mock.Of<IHttpContextAccessor>()).SearchAsync("Para", 2, 10, CancellationToken.None);

        Assert.Equal(id, Assert.Single(result.Items).Id);
        Assert.Equal("Paracetamol", result.Items[0].Name);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(25, result.TotalCount);
    }

    [Fact]
    public async Task Json_null_is_reported_as_catalogue_unavailable()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json") });
        using var client = new HttpClient(handler.Object) { BaseAddress = new Uri("https://catalogue.test/") };

        await Assert.ThrowsAsync<SaleItemSearchUnavailableException>(() =>
            new SaleItemSearchService(client, Mock.Of<IHttpContextAccessor>()).SearchAsync(null, 1, 20, CancellationToken.None));
    }
}
