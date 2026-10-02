namespace AgroTrade.Contracts.ActivityLogs;

public record ActivityLogDto(
    int Id,
    DateTimeOffset CreatedAt,
    string Severity,
    string Category,
    string Event,
    string Message,
    int? OrderId,
    string? OrderNumber,
    string? Provider,
    string? Status,
    string? Actor,
    string? Details);
