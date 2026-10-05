namespace AgroTrade.Infrastructure.Services;

public class SmtpEmailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Agro Trade";
    public string CompanyRecipient { get; set; } = string.Empty;
    public bool SendCustomerInvoice { get; set; } = true;
}
