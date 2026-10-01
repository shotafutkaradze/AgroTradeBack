using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AgroTrade.Application.Abstractions;
using AgroTrade.Application.Services;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.Services;

public class LibertyPayRefundClient(HttpClient httpClient, IOptions<LibertyPayOptions> options) : ILibertyPayRefundClient
{
    private readonly LibertyPayOptions options = options.Value;

    public async Task<LibertyPayRefundResult> RefundAsync(
        string orderCode,
        string refundCode,
        string description,
        int amount,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var signature = Sha256(options.Merchant + orderCode + refundCode + description + amount + options.Key);
        var envelope = BuildRefundEnvelope(orderCode, refundCode, description, amount, signature);
        using var request = new HttpRequestMessage(HttpMethod.Post, options.ServiceUrl);
        request.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        request.Headers.TryAddWithoutValidation("SOAPAction", options.RefundSoapAction);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Liberty PAY refund failed with HTTP {(int)response.StatusCode}: {responseText}");
        }

        return ParseRefundResponse(responseText);
    }

    private string BuildRefundEnvelope(string orderCode, string refundCode, string description, int amount, string signature)
    {
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:tem="http://tempuri.org/">
              <soapenv:Header/>
              <soapenv:Body>
                <tem:Refund>
                  <tem:merchant>{Escape(options.Merchant)}</tem:merchant>
                  <tem:orderCode>{Escape(orderCode)}</tem:orderCode>
                  <tem:refundCode>{Escape(refundCode)}</tem:refundCode>
                  <tem:description>{Escape(description)}</tem:description>
                  <tem:amount>{amount}</tem:amount>
                  <tem:signature>{signature}</tem:signature>
                </tem:Refund>
              </soapenv:Body>
            </soapenv:Envelope>
            """;
    }

    private static LibertyPayRefundResult ParseRefundResponse(string responseText)
    {
        var document = XDocument.Parse(responseText);
        var resultElement = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName is "RefundResult" or "RefundInfo");

        var statusCode = ReadValue(resultElement, "RefundStatusCode")
            ?? ReadValue(resultElement, "OrderInfoStatusCode")
            ?? ReadValue(document.Root, "RefundStatusCode")
            ?? "-100";
        var amountText = ReadValue(resultElement, "Amount")
            ?? ReadValue(document.Root, "Amount")
            ?? "0";
        var transactionCode = ReadValue(resultElement, "TranCode")
            ?? ReadValue(document.Root, "TranCode");

        return new LibertyPayRefundResult(
            statusCode.Trim(),
            int.TryParse(amountText, out var amount) ? amount : 0,
            transactionCode,
            responseText);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.ServiceUrl) ||
            string.IsNullOrWhiteSpace(options.Merchant) ||
            string.IsNullOrWhiteSpace(options.Key))
        {
            throw new InvalidOperationException("Liberty PAY service URL, merchant and key must be configured.");
        }
    }

    private static string? ReadValue(XElement? root, string name)
    {
        return root?.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static string Escape(string value)
    {
        return System.Security.SecurityElement.Escape(value) ?? string.Empty;
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
