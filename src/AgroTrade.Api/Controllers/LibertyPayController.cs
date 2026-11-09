using AgroTrade.Application.Services;
using AgroTrade.Contracts.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AgroTrade.Api;

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
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "გადახდა ახლდება. სცადეთ ხელახლა." }); }
    }

    [HttpGet("orders/{orderId:int}/status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Status(int orderId, [FromHeader(Name = "X-Payment-Token")] string? token, CancellationToken cancellationToken)
    {
        try
        {
            var result = await paymentService.GetLibertyStatusAsync(orderId, token, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
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
        try
        {
            var result = await paymentService.HandleLibertyCallbackAsync(request, cancellationToken);
            return Content(result.Xml, "application/xml");
        }
        catch (DbUpdateConcurrencyException) { return StatusCode(503); }
    }

    [HttpPost("orders/{orderId:int}/refund")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrManager)]
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
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "დაბრუნების მოთხოვნა უკვე მუშავდება." }); }
    }

    private async Task<IActionResult> HandleReturnAsync(
        string paymentResult,
        string? paymentOrderCode,
        Func<string?, CancellationToken, Task<PaymentStatusDto?>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler(paymentOrderCode, cancellationToken);
        var confirmedResult = result?.PaymentStatus switch
        {
            "paid" => "success",
            "failed" or "cancelled" or "refunded" or "partially_refunded" => "error",
            _ => "pending"
        };
        return Redirect(BuildFrontendReturnUrl(result is null ? "error" : confirmedResult, result?.OrderNumber ?? paymentOrderCode));
    }

    private string BuildFrontendReturnUrl(string paymentResult, string? orderNumber)
    {
        var baseUrl = string.IsNullOrWhiteSpace(libertyPayOptions.FrontendReturnUrl)
            ? "http://localhost:5173/"
            : libertyPayOptions.FrontendReturnUrl.Trim();
        var separator = baseUrl.Contains('?') ? '&' : '?';
        var orderPart = string.IsNullOrWhiteSpace(orderNumber)
            ? string.Empty
            : $"&order={Uri.EscapeDataString(orderNumber)}";
        return $"{baseUrl}{separator}provider=liberty&payment={Uri.EscapeDataString(paymentResult)}{orderPart}";
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


