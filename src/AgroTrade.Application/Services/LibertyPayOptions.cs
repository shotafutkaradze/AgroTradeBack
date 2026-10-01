namespace AgroTrade.Application.Services;

public class LibertyPayOptions
{
    public string PayUrl { get; set; } = "https://www.pay.ge/pay";
    public string Merchant { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Currency { get; set; } = "GEL";
    public string Language { get; set; } = "KA";
    public string TestMode { get; set; } = "1";
    public string PublicApiBaseUrl { get; set; } = string.Empty;
    public string FrontendReturnUrl { get; set; } = string.Empty;
    public string ServiceUrl { get; set; } = "http://pay.ge:1202/PaygeService.svc";
    public string RefundSoapAction { get; set; } = "http://tempuri.org/IPaygeService/Refund";
}
