using System.Net.Http.Headers;
using System.Text.Json;
using Medzo.SalesReporting.Contracts;

namespace Medzo.SalesReporting.Services;

public sealed class SaleItemSearchUnavailableException : Exception
{
 public SaleItemSearchUnavailableException() : base("Medicine search is temporarily unavailable. Please try again.") { }
}

public interface ISaleItemSearchService
{
 Task<SaleItemSearchResponse> SearchAsync(string? search, int page, int pageSize, CancellationToken ct);
}

public sealed class SaleItemSearchService(HttpClient catalogueClient, IHttpContextAccessor httpContextAccessor) : ISaleItemSearchService
{
 private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

 public async Task<SaleItemSearchResponse> SearchAsync(string? search, int page, int pageSize, CancellationToken ct)
 {
  var normalizedSearch = search?.Trim() ?? string.Empty;
  var normalizedPage = Math.Max(page, 1);
  var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
  var path = $"api/catalogue/medicines/public?search={Uri.EscapeDataString(normalizedSearch)}&page={normalizedPage}&pageSize={normalizedPageSize}";
  using var request = new HttpRequestMessage(HttpMethod.Get, path);

  if (httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString() is { Length: > 0 } authorization)
   request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);

  HttpResponseMessage response;
  try
  {
   response = await catalogueClient.SendAsync(request, ct);
  }
  catch (HttpRequestException)
  {
   throw new SaleItemSearchUnavailableException();
  }

  using (response)
  {
   if (!response.IsSuccessStatusCode) throw new SaleItemSearchUnavailableException();

  var catalogueResponse = await response.Content.ReadFromJsonAsync<CatalogueSearchResponse>(JsonOptions, ct) ?? throw new SaleItemSearchUnavailableException();
   return new SaleItemSearchResponse(
    catalogueResponse.Items.Select(item => new SaleItemSearchResult(item.Id, item.Name)).ToList(),
    catalogueResponse.Page,
    catalogueResponse.PageSize,
    catalogueResponse.TotalCount);
  }
 }

 private sealed record CatalogueSearchResponse(IReadOnlyList<CatalogueMedicine> Items, int Page, int PageSize, int TotalCount);
 private sealed record CatalogueMedicine(Guid Id, string Name);
}
