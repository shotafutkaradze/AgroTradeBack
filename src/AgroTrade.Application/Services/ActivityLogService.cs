using AgroTrade.Application.Abstractions;
using AgroTrade.Contracts.ActivityLogs;
using AgroTrade.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Application.Services;

public interface IActivityLogService
{
    Task<IReadOnlyList<ActivityLogDto>> GetImportantAsync(int take, CancellationToken cancellationToken);
    Task AddAsync(ActivityLogEntry entry, CancellationToken cancellationToken);
}

public record ActivityLogEntry(
    string Severity,
    string Category,
    string Event,
    string Message,
    int? OrderId = null,
    string? OrderNumber = null,
    string? Provider = null,
    string? Status = null,
    string? Actor = null,
    string? Details = null);

public class ActivityLogService(IAgroTradeDbContext dbContext) : IActivityLogService
{
    public async Task<IReadOnlyList<ActivityLogDto>> GetImportantAsync(int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 200);

        return await dbContext.ActivityLogs
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .Take(take)
            .Select(log => new ActivityLogDto(
                log.Id,
                log.CreatedAt,
                log.Severity,
                log.Category,
                log.Event,
                log.Message,
                log.OrderId,
                log.OrderNumber,
                log.Provider,
                log.Status,
                log.Actor,
                log.Details))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ActivityLogEntry entry, CancellationToken cancellationToken)
    {
        dbContext.ActivityLogs.Add(new ActivityLog
        {
            Severity = Trim(entry.Severity, 40),
            Category = Trim(entry.Category, 80),
            Event = Trim(entry.Event, 120),
            Message = Trim(entry.Message, 500),
            OrderId = entry.OrderId,
            OrderNumber = TrimOrNull(entry.OrderNumber, 80),
            Provider = TrimOrNull(entry.Provider, 80),
            Status = TrimOrNull(entry.Status, 80),
            Actor = TrimOrNull(entry.Actor, 160),
            Details = TrimOrNull(entry.Details, 4000),
            CreatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Trim(string value, int maxLength)
    {
        var result = string.IsNullOrWhiteSpace(value) ? "info" : value.Trim();
        return result.Length <= maxLength ? result : result[..maxLength];
    }

    private static string? TrimOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var result = value.Trim();
        return result.Length <= maxLength ? result : result[..maxLength];
    }
}
