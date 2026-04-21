namespace SmartLogisticsTracker.Api.Domain.Entities;

public sealed class Shipment
{
    public Guid Id { get; set; }

    public string WaybillNumber { get; set; } = string.Empty;

    public string? Sender { get; set; }

    public string? Receiver { get; set; }

    public string? Origin { get; set; }

    public string? Destination { get; set; }

    public string? Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<DeviceBinding> DeviceBindings { get; set; } = new List<DeviceBinding>();

    public ICollection<TelemetryRecord> TelemetryRecords { get; set; } = new List<TelemetryRecord>();

    public ICollection<AlertEvent> AlertEvents { get; set; } = new List<AlertEvent>();
}
