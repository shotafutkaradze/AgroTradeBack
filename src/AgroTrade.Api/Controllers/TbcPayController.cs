using AgroTrade.Application.Services;
using AgroTrade.Contracts.Payments;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/tbc-pay")]
public class TbcPayController(IPaymentService paymentService) : ControllerBase
{
    [HttpPost("orders/{orderId:int}/start")]
    public async Task<IActionResult> StartPayment(
        int orderId,
        TbcInstallmentPaymentStartRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await paymentService.StartTbcInstallmentAsync(orderId, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("callback")]
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

        var result = await paymentService.HandleTbcInstallmentCallbackAsync(
            new TbcInstallmentCallbackRequest(
                Get("status"),
                Get("order_id") ?? Get("orderId"),
                Get("shop_order_id") ?? Get("shopOrderId") ?? Get("ordercode"),
                Get("transaction_id") ?? Get("transactionId")),
            cancellationToken);

        return result is null ? NotFound() : Ok(new { Http_Status = "200 OK" });
    }
}
