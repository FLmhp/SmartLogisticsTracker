namespace SmartLogisticsTracker.Api.Domain.Entities;

public sealed class Device
{
    public Guid Id { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public bool IsOnline { get; set; }

    public int? BatteryLevel { get; set; }

    public DateTime? LastSeenAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<DeviceBinding> Bindings { get; set; } = new List<DeviceBinding>();

    public ICollection<TelemetryRecord> TelemetryRecords { get; set; } = new List<TelemetryRecord>();

    public ICollection<AlertEvent> AlertEvents { get; set; } = new List<AlertEvent>();
}
