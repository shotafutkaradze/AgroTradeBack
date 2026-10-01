using AgroTrade.Application.Services;
using AgroTrade.Contracts.Payments;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/bog-pay")]
public class BogPayController(IPaymentService paymentService) : ControllerBase
{
    [HttpPost("orders/{orderId:int}/start")]
    public async Task<IActionResult> StartPayment(
        int orderId,
        BogInstallmentPaymentStartRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await paymentService.StartBogInstallmentAsync(orderId, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("callback")]
    public async Task<IActionResult> Callback(CancellationToken cancellationToken)
    {
        var values = Request.Query.ToDictionary(item => item.Key, item => item.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            foreach (var item in form)
            {
                values[item.Key] = item.Value.ToString();
            }
        }

        string? Get(string name) => values.TryGetValue(name, out var value) ? value : null;

        var result = await paymentService.HandleBogInstallmentCallbackAsync(
            new BogInstallmentCallbackRequest(
                Get("status"),
                Get("order_id"),
                Get("shop_order_id"),
                Get("payment_method")),
            cancellationToken);

        return result is null ? NotFound() : Ok(new { Http_Status = "200 OK" });
    }
}

