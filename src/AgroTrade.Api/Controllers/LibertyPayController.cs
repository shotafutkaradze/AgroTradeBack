using AgroTrade.Application.Services;
using AgroTrade.Contracts.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/liberty-pay")]
public class LibertyPayController(
    IPaymentService paymentService,
    IOptions<LibertyPayOptions> libertyPayOptions) : ControllerBase
{
    private readonly LibertyPayOptions libertyPayOptions = libertyPayOptions.Value;

    [HttpPost("orders/{orderId:int}/start")]
    public async Task<IActionResult> StartPayment(
        int orderId,
        LibertyPayPaymentStartRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await paymentService.StartLibertyPaymentAsync(orderId, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("approve")]
    public Task<IActionResult> Approve(
        [FromQuery] string? id,
        [FromQuery] string? ordercode,
        [FromQuery] string? transactioncode,
        CancellationToken cancellationToken)
    {
        return HandleReturnAsync(
            "success",
            ordercode ?? id,
            (paymentOrderCode, token) => paymentService.HandleLibertyApproveAsync(paymentOrderCode, transactioncode, token),
            cancellationToken);
    }

    [HttpGet("decline")]
    public Task<IActionResult> Decline([FromQuery] string? id, [FromQuery] string? ordercode, CancellationToken cancellationToken)
    {
        return HandleReturnAsync("error", ordercode ?? id, paymentService.HandleLibertyDeclineAsync, cancellationToken);
    }

    [HttpGet("cancel")]
    public Task<IActionResult> Cancel([FromQuery] string? id, [FromQuery] string? ordercode, CancellationToken cancellationToken)
    {
        return HandleReturnAsync("cancel", ordercode ?? id, paymentService.HandleLibertyCancelAsync, cancellationToken);
    }

    [HttpGet("callback")]
    [HttpPost("callback")]
    public async Task<IActionResult> Callback(CancellationToken cancellationToken)
    {
        var request = await BuildCallbackRequestAsync();
        var result = await paymentService.HandleLibertyCallbackAsync(request, cancellationToken);
        return Content(result.Xml, "application/xml");
    }

    [HttpPost("orders/{orderId:int}/refund")]
    public async Task<IActionResult> RefundPayment(
        int orderId,
        RefundPaymentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await paymentService.RefundLibertyPaymentAsync(orderId, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private async Task<IActionResult> HandleReturnAsync(
        string paymentResult,
        string? paymentOrderCode,
        Func<string?, CancellationToken, Task<PaymentStatusDto?>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler(paymentOrderCode, cancellationToken);
        return Redirect(BuildFrontendReturnUrl(result is null ? "error" : paymentResult, result?.OrderNumber ?? paymentOrderCode));
    }

    private string BuildFrontendReturnUrl(string paymentResult, string? orderNumber)
    {
        var baseUrl = string.IsNullOrWhiteSpace(libertyPayOptions.FrontendReturnUrl)
            ? "http://localhost:5173/AgroTradefrontend/"
            : libertyPayOptions.FrontendReturnUrl.Trim();
        var separator = baseUrl.Contains('?') ? '&' : '?';
        var orderPart = string.IsNullOrWhiteSpace(orderNumber)
            ? string.Empty
            : $"&order={Uri.EscapeDataString(orderNumber)}";
        return $"{baseUrl}{separator}payment={Uri.EscapeDataString(paymentResult)}{orderPart}";
    }

    private async Task<LibertyPayCallbackRequest> BuildCallbackRequestAsync()
    {
        var values = Request.Query.ToDictionary(item => item.Key, item => item.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var item in form)
            {
                values[item.Key] = item.Value.ToString();
            }
        }

        string? Get(string name) => values.TryGetValue(name, out var value) ? value : null;

        return new LibertyPayCallbackRequest(
            Get("status"),
            Get("transactioncode"),
            Get("date"),
            Get("amount"),
            Get("currency"),
            Get("ordercode"),
            Get("paymethod"),
            Get("customdata"),
            Get("payedamount"),
            Get("testmode"),
            Get("check"));
    }
}


