using AgroTrade.Application.Abstractions;
using AgroTrade.Application.Services;
using AgroTrade.Contracts.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/bog-pay")]
public class BogPayController(IPaymentService paymentService, IBogInstallmentClient bogClient, ILogger<BogPayController> logger) : ControllerBase
{
    [HttpPost("orders/{orderId:int}/start")]
    public Task<IActionResult> StartPayment(int orderId, BogInstallmentPaymentStartRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var result = await paymentService.StartBogInstallmentAsync(orderId, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }, false, cancellationToken);

    [HttpGet("orders/{orderId:int}/status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IActionResult> Status(int orderId, [FromHeader(Name = "X-Payment-Token")] string? token, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var result = await paymentService.GetBogStatusAsync(orderId, token, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }, false, cancellationToken);

    [HttpPost("callback")]
    [RequestSizeLimit(262144)]
    public Task<IActionResult> Callback(CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        var callback = bogClient.VerifyCallback(buffer.ToArray(), Request.Headers["Callback-Signature"].ToString());
        if (callback is null)
        {
            logger.LogWarning("Rejected BOG callback: invalid signature or payload");
            return Unauthorized();
        }
        var result = await paymentService.HandleBogInstallmentCallbackAsync(callback, cancellationToken);
        // A callback can race with saving the bank order ID. Ask BOG to retry it.
        return result is null ? StatusCode(503) : Ok();
    }, true, cancellationToken);

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action, bool callback, CancellationToken cancellationToken)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (ArgumentException) { return BadRequest(new { message = "აირჩიეთ განვადების პირობები ბანკის კალკულატორში." }); }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("Concurrent BOG payment update; request should be retried");
            return StatusCode(callback ? 503 : 409, new { message = "გადახდის მონაცემები ახლდება. სცადეთ ხელახლა." });
        }
        catch (InvalidOperationException)
        {
            logger.LogWarning("BOG operation rejected by payment validation");
            return StatusCode(callback ? 503 : 409, new { message = "გადახდის დაწყება ან დადასტურება ვერ მოხერხდა. შეამოწმეთ შეკვეთის მდგომარეობა და ბანკის კონფიგურაცია." });
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException or FormatException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("BOG upstream request failed ({FailureType})", exception.GetType().Name);
            return StatusCode(502, new { message = "ბანკთან დაკავშირება ვერ მოხერხდა. სცადეთ ხელახლა." });
        }
    }
}
