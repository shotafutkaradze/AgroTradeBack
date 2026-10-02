namespace AgroTrade.Domain.Models;

public class ActivityLog
{
    public int Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Severity { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string? Provider { get; set; }
    public string? Status { get; set; }
    public string? Actor { get; set; }
    public string? Details { get; set; }
}
