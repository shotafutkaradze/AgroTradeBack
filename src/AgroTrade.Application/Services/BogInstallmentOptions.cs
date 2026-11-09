namespace AgroTrade.Application.Services;

public class BogInstallmentOptions
{
    public string BaseUrl { get; set; } = "https://api.bog.ge";
    public string TokenUrl { get; set; } = "https://oauth2.bog.ge/auth/realms/bog/protocol/openid-connect/token";
    public string CallbackUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Currency { get; set; } = "GEL";
    public string Locale { get; set; } = "ka";
    public string FrontendReturnUrl { get; set; } = "http://localhost:5173/";
}
