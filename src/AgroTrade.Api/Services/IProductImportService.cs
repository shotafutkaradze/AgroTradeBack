namespace AgroTrade.Api.Services;

public interface IProductImportService
{
    Task<int> SyncProductsAsync(CancellationToken cancellationToken);
}
