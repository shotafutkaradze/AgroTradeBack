using System.Net;
using System.Net.Mail;
using System.Text;
using AgroTrade.Application.Abstractions;
using AgroTrade.Contracts.Auth;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.Services;

public class SmtpAccountEmailService(IOptions<SmtpEmailOptions> options) : IAccountEmailService
{
    private readonly SmtpEmailOptions options = options.Value;

    public async Task SendEmailVerificationAsync(AuthUserDto user, string verificationUrl, CancellationToken cancellationToken)
    {
        if (!CanSend())
        {
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(options.FromEmail, options.FromName, Encoding.UTF8),
            Subject = "Agro Trade - ელფოსტის დადასტურება",
            Body = BuildBody(user, verificationUrl),
            IsBodyHtml = true,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        message.To.Add(new MailAddress(user.Email, $"{user.FirstName} {user.LastName}", Encoding.UTF8));

        using var smtp = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            Credentials = new NetworkCredential(options.UserName, options.Password)
        };

        await smtp.SendMailAsync(message, cancellationToken);
    }

    private bool CanSend()
    {
        return options.Enabled &&
            !string.IsNullOrWhiteSpace(options.Host) &&
            !string.IsNullOrWhiteSpace(options.UserName) &&
            !string.IsNullOrWhiteSpace(options.Password) &&
            !string.IsNullOrWhiteSpace(options.FromEmail);
    }

    private static string BuildBody(AuthUserDto user, string verificationUrl)
    {
        return $"""
        <!doctype html>
        <html lang="ka">
        <body style="margin:0;background:#f4f7f3;font-family:Arial,sans-serif;color:#18251b;">
          <div style="max-width:640px;margin:24px auto;background:#ffffff;border:1px solid #dfe8df;border-radius:12px;overflow:hidden;">
            <div style="background:#0f7a3b;color:white;padding:24px;">
              <h1 style="margin:0;font-size:24px;">Agro Trade</h1>
              <p style="margin:8px 0 0;">ელფოსტის დადასტურება</p>
            </div>
            <div style="padding:28px;">
              <h2 style="margin-top:0;">გამარჯობა, {WebUtility.HtmlEncode(user.FirstName)}</h2>
              <p>ანგარიშის გასააქტიურებლად დაადასტურეთ თქვენი ელფოსტა.</p>
              <p style="margin:28px 0;">
                <a href="{WebUtility.HtmlEncode(verificationUrl)}" style="background:#0f7a3b;color:#ffffff;text-decoration:none;padding:13px 20px;border-radius:8px;font-weight:bold;">ელფოსტის დადასტურება</a>
              </p>
              <p style="font-size:13px;color:#617062;">თუ ღილაკი არ გაიხსნა, გახსენით ეს ბმული ბრაუზერში:<br>{WebUtility.HtmlEncode(verificationUrl)}</p>
            </div>
          </div>
        </body>
        </html>
        """;
    }
}
