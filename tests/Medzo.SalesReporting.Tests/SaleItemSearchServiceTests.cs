using System.Net;
using System.Net.Http.Headers;
using Medzo.SalesReporting.Services;
using Microsoft.AspNetCore.Http;

namespace Medzo.SalesReporting.Tests;

public sealed class SaleItemSearchServiceTests
{
 [Fact]
 public async Task Searches_catalogue_by_trimmed_partial_name_and_forwards_the_access_token()
 {
  HttpRequestMessage? capturedRequest = null;
  var handler = new StubHandler(request =>
  {
   capturedRequest = request;
   return JsonResponse("""{"items":[{"id":"a0e9beea-3d86-4cc8-b5b7-055fbeb64610","name":"Paracetamol 500mg"}],"page":1,"pageSize":100,"totalCount":1}""");
  });
  var context = new DefaultHttpContext();
  context.Request.Headers.Authorization = "Bearer test-token";
  var service = new SaleItemSearchService(new HttpClient(handler) { BaseAddress = new Uri("https://catalogue.test/") }, new HttpContextAccessor { HttpContext = context });

  var result = await service.SearchAsync(" Para ", 0, 150, CancellationToken.None);

  Assert.Single(result.Items);
  Assert.Equal("Paracetamol 500mg", result.Items[0].Name);
  Assert.Equal(1, result.Page);
  Assert.Equal(100, result.PageSize);
  Assert.Equal("?search=Para&page=1&pageSize=100", capturedRequest!.RequestUri!.Query);
  Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-token"), capturedRequest.Headers.Authorization);
 }

 [Fact]
 public async Task Returns_a_safe_error_when_catalogue_is_unavailable()
 {
  var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
  var service = new SaleItemSearchService(new HttpClient(handler) { BaseAddress = new Uri("https://catalogue.test/") }, new HttpContextAccessor());

  await Assert.ThrowsAsync<SaleItemSearchUnavailableException>(() => service.SearchAsync("Para", 1, 20, CancellationToken.None));
 }

 [Fact]
 public async Task Returns_a_safe_error_when_catalogue_connection_is_refused()
 {
  var handler = new ThrowingHandler();
  var service = new SaleItemSearchService(new HttpClient(handler) { BaseAddress = new Uri("https://catalogue.test/") }, new HttpContextAccessor());

  var exception = await Assert.ThrowsAsync<SaleItemSearchUnavailableException>(() => service.SearchAsync("amox", 1, 20, CancellationToken.None));

  Assert.Equal("Medicine search is temporarily unavailable. Please try again.", exception.Message);
 }
 private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
 private sealed class ThrowingHandler : HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new HttpRequestException("Connection refused");
 }

 private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
 }
}
