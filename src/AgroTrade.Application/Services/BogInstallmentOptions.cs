namespace AgroTrade.Application.Services;

public class BogInstallmentOptions
{
    public string BaseUrl { get; set; } = "https://installment-test.bog.ge";
    public string ClientId { get; set; } = "10002550";
    public string SecretKey { get; set; } = string.Empty;
    public string Currency { get; set; } = "GEL";
    public string Locale { get; set; } = "ka";
    public int DefaultInstallmentMonth { get; set; } = 6;
    public string DefaultInstallmentType { get; set; } = "STANDARD";
    public string FrontendReturnUrl { get; set; } = "http://localhost:5173/AgroTradefrontend/";
}
