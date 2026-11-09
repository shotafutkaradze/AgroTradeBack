using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgroTrade.Application.Services;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Models;
using AgroTrade.Infrastructure.Services;
using Microsoft.Extensions.Options;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
foreach (var method in new[] { PaymentMethod.BogInstallmentPlan, PaymentMethod.BogPieceByPiece })
{
    var handler = new BankHandler();
    var client = new BogInstallmentClient(new HttpClient(handler), Options.Create(new BogInstallmentOptions
    {
        ClientId = "test", SecretKey = "test", CallbackUrl = "https://backend.example/api/bog-pay/callback"
    }));
    var result = await client.CreateOrderAsync(new Order
    {
        OrderNumber = "ORDER-1", PaymentMethod = method, Total = 25.50m, DeliveryPrice = 5,
        Items = [new OrderItem { ProductId = 7, ProductName = "Product", Quantity = 2, UnitPrice = 10.25m, LineTotal = 20.50m }]
    }, 4, method == PaymentMethod.BogPieceByPiece ? "ZERO" : "CampaignABC", CancellationToken.None, Guid.Parse("11111111-1111-4111-8111-111111111111"));
    Check(result.OrderId == "bank-id" && result.RedirectUrl == "https://payment.bog.ge/test", "Response mapping");
    using var body = JsonDocument.Parse(handler.OrderBody!);
    var root = body.RootElement;
    Check(root.GetProperty("payment_method")[0].GetString() == (method == PaymentMethod.BogPieceByPiece ? "bnpl" : "bog_loan"), "Method mapping");
    Check(root.GetProperty("config").GetProperty("loan").GetProperty("type").GetString() == (method == PaymentMethod.BogPieceByPiece ? "ZERO" : "CampaignABC"), "Discount code case");
    Check(root.GetProperty("purchase_units").GetProperty("total_amount").GetDecimal() == 25.50m, "Culture independent numeric amount");
    Check(root.GetProperty("callback_url").GetString() == "https://backend.example/api/bog-pay/callback", "Callback URL");
    var receipt = await client.GetPaymentDetailsAsync("bank-id", CancellationToken.None);
    Check(receipt.Status == "completed" && receipt.Amount == 25.50m && receipt.TransferredAmount == 25.50m && receipt.TransactionId == "transaction-id", "Payment Details endpoint/response mapping");
    Check(client.VerifyCallback(Encoding.UTF8.GetBytes("{}"), "") is null, "Missing signature rejected");
    Check(client.VerifyCallback(Encoding.UTF8.GetBytes("{}"), "invalid") is null, "Malformed signature rejected");
    using var rsa = RSA.Create(2048);
    var forged = rsa.SignData(Encoding.UTF8.GetBytes("{}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    Check(client.VerifyCallback(Encoding.UTF8.GetBytes("{}"), Convert.ToBase64String(forged)) is null, "Foreign signature rejected");
}
await UploadStorageChecks.RunAsync();
await ServiceChecks.RunAsync();
Console.WriteLine("BOG checks passed: both payment methods, payload, response, culture, and invalid signatures.");

sealed class BankHandler : HttpMessageHandler
{
    public string? OrderBody { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.Host == "oauth2.bog.ge")
        {
            if (request.Headers.Authorization?.Scheme != "Basic") throw new Exception("Basic auth missing");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"token\"}") };
        }
        if (request.Method == HttpMethod.Get && request.RequestUri.AbsoluteUri == "https://api.bog.ge/payments/v1/receipt/bank-id")
        {
            if (request.Headers.Authorization?.Parameter != "token") throw new Exception("Receipt authorization missing");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"order_id":"bank-id","external_order_id":"ORDER-1","order_status":{"key":"completed"},
                     "purchase_units":{"request_amount":"25.50","transfer_amount":"25.50","currency_code":"GEL"},
                     "payment_detail":{"transfer_method":{"key":"bog_loan"},"transaction_id":"transaction-id"}}
                    """)
            };
        }
        if (request.RequestUri.AbsoluteUri != "https://api.bog.ge/payments/v1/ecommerce/orders" || request.Headers.Authorization?.Parameter != "token")
            throw new Exception("Order endpoint or authorization incorrect");
        if (!request.Headers.TryGetValues("Idempotency-Key", out var keys) || keys.Single() != "11111111-1111-4111-8111-111111111111") throw new Exception("Idempotency key missing");
        OrderBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"id\":\"bank-id\",\"_links\":{\"redirect\":{\"href\":\"https://payment.bog.ge/test\"}}}")
        };
    }
}
