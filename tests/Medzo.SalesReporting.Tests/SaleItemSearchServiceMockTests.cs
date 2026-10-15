using System.Net;
using System.Text;
using Medzo.SalesReporting.Services;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Medzo.SalesReporting.Tests;

public sealed class SaleItemSearchServiceMockTests
{
    [Theory]
    [InlineData(null, 0, 0, "?search=&page=1&pageSize=1")]
    [InlineData("  paracetamol  ", 2, 500, "?search=paracetamol&page=2&pageSize=100")]
    [InlineData("amoxicillin 500mg", -5, 20, "?search=amoxicillin%20500mg&page=1&pageSize=20")]
    public async Task SearchAsync_normalizes_query_and_paging_before_calling_catalogue(
        string? search,
        int page,
        int pageSize,
        string expectedQuery)
    {
        HttpRequestMessage? outgoingRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            outgoingRequest = request;
            return JsonResponse("""{"items":[],"page":1,"pageSize":1,"totalCount":0}""");
        });
        var httpContextAccessor = new Mock<IHttpContextAccessor>(MockBehavior.Strict);
        httpContextAccessor.SetupGet(accessor => accessor.HttpContext).Returns((HttpContext?)null);
        var service = CreateService(handler, httpContextAccessor.Object);

        await service.SearchAsync(search, page, pageSize, CancellationToken.None);

        Assert.NotNull(outgoingRequest);
        Assert.Equal(expectedQuery, outgoingRequest.RequestUri!.Query);
        Assert.Null(outgoingRequest.Headers.Authorization);
        httpContextAccessor.VerifyGet(accessor => accessor.HttpContext, Times.Once);
    }

    [Fact]
    public async Task SearchAsync_forwards_the_current_users_bearer_token()
    {
        HttpRequestMessage? outgoingRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            outgoingRequest = request;
            return JsonResponse("""{"items":[],"page":1,"pageSize":20,"totalCount":0}""");
        });
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer test-access-token";
        var httpContextAccessor = new Mock<IHttpContextAccessor>(MockBehavior.Strict);
        httpContextAccessor.SetupGet(accessor => accessor.HttpContext).Returns(context);
        var service = CreateService(handler, httpContextAccessor.Object);

        await service.SearchAsync("aspirin", 1, 20, CancellationToken.None);

        Assert.Equal("Bearer", outgoingRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("test-access-token", outgoingRequest.Headers.Authorization.Parameter);
        httpContextAccessor.VerifyGet(accessor => accessor.HttpContext, Times.Once);
    }

    [Fact]
    public async Task SearchAsync_converts_catalogue_failure_to_a_safe_domain_error()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.SetupGet(accessor => accessor.HttpContext).Returns((HttpContext?)null);
        var service = CreateService(handler, httpContextAccessor.Object);

        var exception = await Assert.ThrowsAsync<SaleItemSearchUnavailableException>(
            () => service.SearchAsync("aspirin", 1, 20, CancellationToken.None));

        Assert.Equal("Medicine search is temporarily unavailable. Please try again.", exception.Message);
    }

    private static SaleItemSearchService CreateService(HttpMessageHandler handler, IHttpContextAccessor httpContextAccessor) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://catalogue.test/") }, httpContextAccessor);

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
