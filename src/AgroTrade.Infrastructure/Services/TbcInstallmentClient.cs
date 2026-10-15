using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgroTrade.Application.Abstractions;
using AgroTrade.Application.Services;
using AgroTrade.Domain.Models;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.Services;

public class TbcInstallmentClient(HttpClient httpClient, IOptions<TbcInstallmentOptions> options) : ITbcInstallmentClient
{
    private readonly TbcInstallmentOptions options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TbcInstallmentOrderResult> CreateOrderAsync(
        Order order,
        int? installmentMonth,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var requestUrl = $"{options.BaseUrl.TrimEnd('/')}/{options.CreateOrderPath.TrimStart('/')}";
        var body = new
        {
            merchant_id = options.MerchantId,
            shop_order_id = order.OrderNumber,
            amount = order.Total.ToString("0.00"),
            currency = options.Currency,
            installment_month = installmentMonth,
            success_url = options.SuccessUrl,
            fail_url = options.ErrorUrl,
            cancel_url = options.CancelUrl,
            callback_url = options.CallbackUrl,
            customer = new
            {
                first_name = order.CustomerFirstName,
                last_name = order.CustomerLastName,
                phone = order.CustomerPhone,
                email = order.CustomerEmail
            },
            items = order.Items.Select(item => new
            {
                name = item.ProductName,
                quantity = item.Quantity,
                unit_price = item.UnitPrice.ToString("0.00"),
                total_price = item.LineTotal.ToString("0.00"),
                sku = item.ProductSku,
                image_url = item.ProductImageUrl
            }).ToArray()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId.Trim()}:{options.ClientSecret.Trim()}")));
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"TBC installment order failed: {response.StatusCode} {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var providerOrderId = GetString(root, "order_id", "id", "payment_id") ?? string.Empty;
        var status = GetString(root, "status", "state") ?? "CREATED";
        var redirectUrl = GetString(root, "redirect_url", "payment_url", "url")
            ?? GetLinkUrl(root, "redirect", "target");

        if (string.IsNullOrWhiteSpace(providerOrderId) || string.IsNullOrWhiteSpace(redirectUrl))
        {
            throw new InvalidOperationException("TBC installment response did not include order id or redirect URL.");
        }

        return new TbcInstallmentOrderResult(providerOrderId, status, redirectUrl);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl) ||
            string.IsNullOrWhiteSpace(options.ClientId) ||
            string.IsNullOrWhiteSpace(options.ClientSecret) ||
            string.IsNullOrWhiteSpace(options.MerchantId))
        {
            throw new InvalidOperationException("TBC installment credentials are not configured.");
        }
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static string? GetLinkUrl(JsonElement element, params string[] rels)
    {
        if (!element.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var link in links.EnumerateArray())
        {
            var rel = GetString(link, "rel");
            if (rels.Any(value => string.Equals(value, rel, StringComparison.OrdinalIgnoreCase)))
            {
                var url = GetString(link, "href", "url");
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return url;
                }
            }
        }

        return null;
    }
}
