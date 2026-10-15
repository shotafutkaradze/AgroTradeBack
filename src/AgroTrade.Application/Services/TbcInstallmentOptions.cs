namespace AgroTrade.Application.Services;

public class TbcInstallmentOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string CreateOrderPath { get; set; } = "/v1/installments/orders";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Currency { get; set; } = "GEL";
    public string SuccessUrl { get; set; } = string.Empty;
    public string ErrorUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
}
