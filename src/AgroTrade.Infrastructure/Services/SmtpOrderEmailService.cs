using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using AgroTrade.Application.Abstractions;
using AgroTrade.Contracts.Orders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.Services;

public class SmtpOrderEmailService(
    IOptions<SmtpEmailOptions> options,
    ILogger<SmtpOrderEmailService> logger) : IOrderEmailService
{
    private readonly SmtpEmailOptions options = options.Value;

    public async Task SendOrderCreatedAsync(OrderDto order, CancellationToken cancellationToken)
    {
        if (!IsConfigured())
        {
            logger.LogInformation("Order email sending is disabled or SMTP is not configured.");
            return;
        }

        var subject = $"ახალი შეკვეთა {order.OrderNumber} - {Money(order.Total)}";
        var companyBody = BuildOrderEmail(order, "კომპანიას", includeAdminNote: true);
        await SendAsync(options.CompanyRecipient, subject, companyBody, cancellationToken);

        if (options.SendCustomerInvoice && !string.IsNullOrWhiteSpace(order.Customer.Email))
        {
            var customerSubject = $"Agro Trade - შეკვეთის ინვოისი {order.OrderNumber}";
            var customerBody = BuildOrderEmail(order, $"{order.Customer.FirstName} {order.Customer.LastName}", includeAdminNote: false);
            await SendAsync(order.Customer.Email!, customerSubject, customerBody, cancellationToken);
        }
    }

    private bool IsConfigured()
    {
        return options.Enabled &&
            !string.IsNullOrWhiteSpace(options.Host) &&
            !string.IsNullOrWhiteSpace(options.UserName) &&
            !string.IsNullOrWhiteSpace(options.Password) &&
            !string.IsNullOrWhiteSpace(options.FromEmail) &&
            !string.IsNullOrWhiteSpace(options.CompanyRecipient);
    }

    private async Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(options.FromEmail, options.FromName, Encoding.UTF8),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(recipient));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            Credentials = new NetworkCredential(options.UserName, options.Password)
        };

        await client.SendMailAsync(message, cancellationToken);
    }

    private static string BuildOrderEmail(OrderDto order, string greetingName, bool includeAdminNote)
    {
        var deliveryText = order.Delivery.Method == "pickup"
            ? $"ფილიალიდან გატანა: {Text(order.Delivery.PickupBranch ?? "მითითებული არ არის")}"
            : $"{Text(order.Delivery.City)}, {Text(order.Delivery.Address)}";
        var rows = string.Join("", order.Items.Select(item => $"""
            <tr>
              <td style="padding:10px;border-bottom:1px solid #e5ebe6;">
                <strong>{Text(item.ProductName)}</strong><br>
                <span style="color:#6b7b72;font-size:12px;">{Text(item.ProductSku ?? item.ProductCategory ?? "პროდუქტი")}</span>
              </td>
              <td style="padding:10px;border-bottom:1px solid #e5ebe6;text-align:center;">{item.Quantity}</td>
              <td style="padding:10px;border-bottom:1px solid #e5ebe6;text-align:right;">{Money(item.UnitPrice)}</td>
              <td style="padding:10px;border-bottom:1px solid #e5ebe6;text-align:right;"><strong>{Money(item.LineTotal)}</strong></td>
            </tr>
            """));

        var adminNote = includeAdminNote
            ? "<p style=\"margin:0 0 16px;color:#244234;\">ახალი შეკვეთა გაკეთდა ვებ-გვერდიდან. გადაამოწმეთ დეტალები ადმინისტრატორის პანელში.</p>"
            : "<p style=\"margin:0 0 16px;color:#244234;\">მადლობა შეკვეთისთვის. ჩვენი ოპერატორი დაგიკავშირდებათ დეტალების დასაზუსტებლად.</p>";

        return $$"""
            <!doctype html>
            <html>
            <body style="margin:0;background:#f4f7f4;font-family:Arial,sans-serif;color:#10281e;">
              <div style="max-width:760px;margin:0 auto;padding:24px;">
                <div style="background:#0f6b3f;color:white;padding:22px;border-radius:14px 14px 0 0;">
                  <h1 style="margin:0;font-size:22px;">Agro Trade</h1>
                  <p style="margin:8px 0 0;">შეკვეთა {{Text(order.OrderNumber)}}</p>
                </div>
                <div style="background:white;padding:24px;border:1px solid #e0e8e2;border-top:0;border-radius:0 0 14px 14px;">
                  <h2 style="margin:0 0 10px;font-size:20px;">გამარჯობა, {{Text(greetingName)}}</h2>
                  {{adminNote}}
                  <table style="width:100%;border-collapse:collapse;margin:18px 0;background:#fbfdfb;border:1px solid #e5ebe6;">
                    <tr>
                      <td style="padding:12px;"><strong>მომხმარებელი</strong><br>{{Text(order.Customer.FirstName)}} {{Text(order.Customer.LastName)}}</td>
                      <td style="padding:12px;"><strong>ტელეფონი</strong><br>{{Text(order.Customer.Phone)}}</td>
                    </tr>
                    <tr>
                      <td style="padding:12px;"><strong>ელფოსტა</strong><br>{{Text(order.Customer.Email ?? "მითითებული არ არის")}}</td>
                      <td style="padding:12px;"><strong>გადახდა</strong><br>{{Text(order.PaymentMethod)}} / {{Text(order.PaymentStatus)}}</td>
                    </tr>
                    <tr>
                      <td style="padding:12px;" colspan="2"><strong>მიწოდება</strong><br>{{deliveryText}}{{(string.IsNullOrWhiteSpace(order.Delivery.Note) ? "" : $"<br><strong>შენიშვნა:</strong> {Text(order.Delivery.Note)}")}}</td>
                    </tr>
                  </table>
                  <table style="width:100%;border-collapse:collapse;margin-top:18px;">
                    <thead>
                      <tr style="background:#eef5ef;">
                        <th style="padding:10px;text-align:left;">პროდუქტი</th>
                        <th style="padding:10px;text-align:center;">რაოდ.</th>
                        <th style="padding:10px;text-align:right;">ფასი</th>
                        <th style="padding:10px;text-align:right;">ჯამი</th>
                      </tr>
                    </thead>
                    <tbody>{{rows}}</tbody>
                  </table>
                  <div style="margin-top:18px;text-align:right;">
                    <p style="margin:4px 0;">პროდუქტები: <strong>{{Money(order.Subtotal)}}</strong></p>
                    <p style="margin:4px 0;">მიწოდება: <strong>{{Money(order.DeliveryPrice)}}</strong></p>
                    <p style="margin:8px 0 0;font-size:20px;">სულ: <strong>{{Money(order.Total)}}</strong></p>
                  </div>
                </div>
              </div>
            </body>
            </html>
            """;
    }

    private static string Money(decimal value) => string.Create(CultureInfo.InvariantCulture, $"{value:0.##} ₾");

    private static string Text(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
