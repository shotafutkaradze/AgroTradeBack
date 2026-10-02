namespace AgroTrade.Application.Abstractions;

public interface ILibertyPayRefundClient
{
    Task<LibertyPayRefundResult> RefundAsync(
        string orderCode,
        string refundCode,
        string description,
        int amount,
        CancellationToken cancellationToken);
}

public record LibertyPayRefundResult(
    string StatusCode,
    int Amount,
    string? TransactionCode,
    string RawResponse,
    string RequestUrl,
    string RequestBody)
{
    public bool IsSuccess => StatusCode == "0";
}

public class LibertyPayRefundException(
    string message,
    string requestUrl,
    string requestBody,
    string? responseBody = null,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public string RequestUrl { get; } = requestUrl;
    public string RequestBody { get; } = requestBody;
    public string? ResponseBody { get; } = responseBody;
}
