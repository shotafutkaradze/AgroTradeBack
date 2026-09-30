using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AgroTrade.Api.Services;

public class WooCommerceService(HttpClient httpClient, IOptions<WooCommerceOptions> options) : IWooCommerceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WooCommerceOptions _options = options.Value;

    public async Task<IReadOnlyList<WooCommerceProduct>> GetProductsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConsumerKey) || string.IsNullOrWhiteSpace(_options.ConsumerSecret))
        {
            throw new InvalidOperationException("WooCommerce ConsumerKey and ConsumerSecret are not configured.");
        }

        httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");

        var auth = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.ConsumerKey}:{_options.ConsumerSecret}"));

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);

        var products = new List<WooCommerceProduct>();
        var page = 1;

        while (true)
        {
            var response = await httpClient.GetAsync(
                $"wp-json/wc/v3/products?per_page=100&page={page}",
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var pageProducts = JsonSerializer.Deserialize<List<WooCommerceProduct>>(json, JsonOptions) ?? [];

            if (pageProducts.Count == 0)
            {
                break;
            }

            products.AddRange(pageProducts);
            page++;
        }

        return products;
    }
}
