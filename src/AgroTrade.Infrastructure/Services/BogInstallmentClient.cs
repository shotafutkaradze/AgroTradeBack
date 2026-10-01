using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgroTrade.Application.Abstractions;
using AgroTrade.Application.Services;
using AgroTrade.Domain.Models;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.Services;

public class BogInstallmentClient(HttpClient httpClient, IOptions<BogInstallmentOptions> options) : IBogInstallmentClient
{
    private readonly BogInstallmentOptions options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BogInstallmentOrderResult> CreateOrderAsync(
        Order order,
        int installmentMonth,
        string installmentType,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var token = await GetTokenAsync(cancellationToken);
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var frontendUrl = options.FrontendReturnUrl.Trim();
        var separator = frontendUrl.Contains('?') ? '&' : '?';
        var successUrl = $"{frontendUrl}{separator}payment=success&order={Uri.EscapeDataString(order.OrderNumber)}";
        var failUrl = $"{frontendUrl}{separator}payment=error&order={Uri.EscapeDataString(order.OrderNumber)}";
        var rejectUrl = $"{frontendUrl}{separator}payment=cancel&order={Uri.EscapeDataString(order.OrderNumber)}";

        var body = new
        {
            intent = "LOAN",
            installment_month = installmentMonth,
            installment_type = installmentType,
            shop_order_id = order.OrderNumber,
            success_redirect_url = successUrl,
            fail_redirect_url = failUrl,
            reject_redirect_url = rejectUrl,
            validate_items = true,
            locale = options.Locale,
            purchase_units = new[]
            {
                new
                {
                    amount = new
                    {
                        currency_code = options.Currency,
                        value = order.Total.ToString("0.00")
                    }
                }
            },
            cart_items = order.Items.Select(item => new
            {
                total_item_amount = item.LineTotal.ToString("0.00"),
                item_description = item.ProductName,
                total_item_qty = item.Quantity,
                item_vendor_code = item.ProductId?.ToString() ?? item.ProductSku ?? item.Id.ToString(),
                product_image_url = item.ProductImageUrl,
                item_site_detail_url = (string?)null
            }).ToArray()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/installment/checkout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"BOG installment order failed: {response.StatusCode} {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var providerOrderId = root.GetProperty("order_id").GetString() ?? string.Empty;
        var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() ?? "CREATED" : "CREATED";
        var redirectUrl = root.GetProperty("links").EnumerateArray()
            .FirstOrDefault(link => string.Equals(link.GetProperty("rel").GetString(), "target", StringComparison.OrdinalIgnoreCase))
            .GetProperty("href")
            .GetString();

        if (string.IsNullOrWhiteSpace(providerOrderId) || string.IsNullOrWhiteSpace(redirectUrl))
        {
            throw new InvalidOperationException("BOG installment response did not include order id or redirect URL.");
        }

        return new BogInstallmentOrderResult(providerOrderId, status, redirectUrl);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId.Trim()}:{options.SecretKey.Trim()}"));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"BOG installment authentication failed: {response.StatusCode} {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("BOG installment token was not returned.");
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl) ||
            string.IsNullOrWhiteSpace(options.ClientId) ||
            string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new InvalidOperationException("BOG installment base URL, client id and secret key must be configured.");
        }
    }
}
