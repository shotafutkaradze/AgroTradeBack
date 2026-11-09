using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Globalization;
using AgroTrade.Contracts.Payments;
using AgroTrade.Domain.Enums;
using System.Text;
using System.Text.Json;
using AgroTrade.Application.Abstractions;
using AgroTrade.Application.Services;
using AgroTrade.Domain.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace AgroTrade.Infrastructure.Services;

public class BogInstallmentClient(HttpClient httpClient, IOptions<BogInstallmentOptions> options, ILogger<BogInstallmentClient>? logger = null) : IBogInstallmentClient
{
    private readonly BogInstallmentOptions options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BogInstallmentOrderResult> CreateOrderAsync(
        Order order,
        int installmentMonth,
        string installmentType,
        CancellationToken cancellationToken,
        Guid? idempotencyKey = null)
    {
        EnsureConfigured();
        if (installmentMonth <= 0 || string.IsNullOrWhiteSpace(installmentType))
            throw new InvalidOperationException("BOG loan month and discount code must be provided.");

        var token = await GetTokenAsync(cancellationToken);
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var frontendUrl = options.FrontendReturnUrl.Trim();
        var separator = frontendUrl.Contains('?') ? '&' : '?';
        var successUrl = $"{frontendUrl}{separator}provider=bog&payment=success&order={Uri.EscapeDataString(order.OrderNumber)}";
        var failUrl = $"{frontendUrl}{separator}provider=bog&payment=error&order={Uri.EscapeDataString(order.OrderNumber)}";
        var body = new
        {
            callback_url = options.CallbackUrl,
            external_order_id = order.OrderNumber,
            redirect_urls = new { success = successUrl, fail = failUrl },
            payment_method = new[] { order.PaymentMethod == PaymentMethod.BogPieceByPiece ? "bnpl" : "bog_loan" },
            config = new { loan = new { type = installmentType, month = installmentMonth } },
            purchase_units = new
            {
                currency = options.Currency,
                total_amount = order.Total,
                delivery = new { amount = order.DeliveryPrice },
                basket = order.Items.Select(item => new
                {
                    product_id = item.ProductId?.ToString() ?? item.ProductSku ?? item.Id.ToString(),
                    description = item.ProductName,
                    quantity = item.Quantity,
                    unit_price = item.UnitPrice,
                    total_price = item.LineTotal
                }).ToArray()
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/payments/v1/ecommerce/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.AcceptLanguage.ParseAdd(options.Locale);
        if (idempotencyKey.HasValue) request.Headers.Add("Idempotency-Key", idempotencyKey.Value.ToString());
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger?.LogWarning("BOG order request failed with HTTP {StatusCode}", (int)response.StatusCode);
            throw new HttpRequestException("BOG order request failed.", null, response.StatusCode);
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var providerOrderId = root.GetProperty("id").GetString() ?? string.Empty;
        var status = "created";
        var redirectUrl = root.GetProperty("_links").GetProperty("redirect").GetProperty("href").GetString();

        if (string.IsNullOrWhiteSpace(providerOrderId) || string.IsNullOrWhiteSpace(redirectUrl))
        {
            throw new InvalidOperationException("BOG installment response did not include order id or redirect URL.");
        }

        return new BogInstallmentOrderResult(providerOrderId, status, redirectUrl);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var tokenUrl = string.IsNullOrWhiteSpace(options.TokenUrl)
            ? "https://oauth2.bog.ge/auth/realms/bog/protocol/openid-connect/token"
            : options.TokenUrl.Trim();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId.Trim()}:{options.SecretKey.Trim()}"));

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger?.LogWarning("BOG authentication failed with HTTP {StatusCode}", (int)response.StatusCode);
            throw new HttpRequestException("BOG authentication failed.", null, response.StatusCode);
        }

        using var document = JsonDocument.Parse(responseText);
        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("BOG installment token was not returned.");
    }

    // Verify the original bytes before parsing JSON.
    public BogInstallmentCallbackRequest? VerifyCallback(byte[] body, string signature)
    {
        const string publicKey = """
            -----BEGIN PUBLIC KEY-----
            MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAu4RUyAw3+CdkS3ZNILQh
            zHI9Hemo+vKB9U2BSabppkKjzjjkf+0Sm76hSMiu/HFtYhqWOESryoCDJoqffY0Q
            1VNt25aTxbj068QNUtnxQ7KQVLA+pG0smf+EBWlS1vBEAFbIas9d8c9b9sSEkTrr
            TYQ90WIM8bGB6S/KLVoT1a7SnzabjoLc5Qf/SLDG5fu8dH8zckyeYKdRKSBJKvhx
            tcBuHV4f7qsynQT+f2UYbESX/TLHwT5qFWZDHZ0YUOUIvb8n7JujVSGZO9/+ll/g
            4ZIWhC1MlJgPObDwRkRd8NFOopgxMcMsDIZIoLbWKhHVq67hdbwpAq9K9WMmEhPn
            PwIDAQAB
            -----END PUBLIC KEY-----
            """;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKey);
            if (!rsa.VerifyData(body, Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return null;
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.GetProperty("event").GetString() != "order_payment") return null;
            var payment = root.GetProperty("body");
            return new BogInstallmentCallbackRequest(null, payment.GetProperty("order_id").GetString(),
                payment.GetProperty("external_order_id").GetString(), null);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException or InvalidOperationException or KeyNotFoundException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    public async Task<BogInstallmentCallbackRequest> GetPaymentDetailsAsync(string orderId, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{options.BaseUrl.TrimEnd('/')}/payments/v1/receipt/{Uri.EscapeDataString(orderId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger?.LogWarning("BOG receipt request failed with HTTP {StatusCode}", (int)response.StatusCode);
            throw new HttpRequestException("BOG receipt request failed.", null, response.StatusCode);
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return ParsePaymentDetails(document.RootElement);
    }

    private static BogInstallmentCallbackRequest ParsePaymentDetails(JsonElement payment)
    {
        var units = payment.GetProperty("purchase_units");
        decimal? ReadAmount(string name)
        {
            if (!units.TryGetProperty(name, out var amount) || amount.ValueKind == JsonValueKind.Null) return null;
            return amount.ValueKind == JsonValueKind.Number ? amount.GetDecimal()
                : decimal.Parse(amount.GetString()!, CultureInfo.InvariantCulture);
        }
        string? method = null;
        string? transactionId = null;
        if (payment.TryGetProperty("payment_detail", out var detail) && detail.ValueKind == JsonValueKind.Object)
        {
            if (detail.TryGetProperty("transfer_method", out var transfer) && transfer.ValueKind == JsonValueKind.Object &&
                transfer.TryGetProperty("key", out var methodKey)) method = methodKey.GetString();
            if (detail.TryGetProperty("transaction_id", out var transaction)) transactionId = transaction.GetString();
        }
        return new BogInstallmentCallbackRequest(
            payment.GetProperty("order_status").GetProperty("key").GetString(),
            payment.GetProperty("order_id").GetString(), payment.GetProperty("external_order_id").GetString(),
            method, ReadAmount("request_amount"), units.GetProperty("currency_code").GetString(),
            ReadAmount("transfer_amount"), ReadAmount("refund_amount"), transactionId);
    }

    private void EnsureConfigured()
    {
        if (!Uri.TryCreate(options.CallbackUrl, UriKind.Absolute, out var callback) || callback.Scheme != "https")
            throw new InvalidOperationException("BOG CallbackUrl must be a public HTTPS URL.");
        if (string.IsNullOrWhiteSpace(options.BaseUrl) ||
            string.IsNullOrWhiteSpace(options.ClientId) ||
            string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new InvalidOperationException("BOG installment base URL, client id and secret key must be configured.");
        }
    }
}
