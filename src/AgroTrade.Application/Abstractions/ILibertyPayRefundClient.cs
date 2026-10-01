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
    string RawResponse)
{
    public bool IsSuccess => StatusCode == "0";
}
