namespace AgroTrade.Api.Services;

public interface IWooCommerceService
{
    Task<IReadOnlyList<WooCommerceProduct>> GetProductsAsync(CancellationToken cancellationToken);
}
