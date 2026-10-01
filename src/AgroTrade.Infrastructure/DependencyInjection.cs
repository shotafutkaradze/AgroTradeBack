using AgroTrade.Application.Abstractions;
using AgroTrade.Application.Services;
using AgroTrade.Infrastructure.Data;
using AgroTrade.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AgroTradeDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IAgroTradeDbContext>(provider => provider.GetRequiredService<AgroTradeDbContext>());
        services.Configure<LibertyPayOptions>(configuration.GetSection("LibertyPay"));
        services.Configure<BogInstallmentOptions>(configuration.GetSection("BogInstallment"));
        services.AddHttpClient<ILibertyPayRefundClient, LibertyPayRefundClient>();
        services.AddHttpClient<IBogInstallmentClient, BogInstallmentClient>();
        services.AddSingleton<AppTokenService>();
        services.AddSingleton<IAppTokenService>(provider => provider.GetRequiredService<AppTokenService>());

        return services;
    }

    public static async Task MigrateDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AgroTradeDbContext>();
        await dbContext.Database.MigrateAsync();
    }
}

