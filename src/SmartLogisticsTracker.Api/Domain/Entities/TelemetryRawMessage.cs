namespace SmartLogisticsTracker.Api.Domain.Entities;

public sealed class TelemetryRawMessage
{
    public Guid Id { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public string Topic { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTime ReceivedAtUtc { get; set; }

    public bool ParseSucceeded { get; set; }

    public string? ParseError { get; set; }
}
