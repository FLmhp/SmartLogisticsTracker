using SmartLogisticsTracker.Api.Domain.Enums;

namespace SmartLogisticsTracker.Api.Domain.Entities;

public sealed class AlertEvent
{
    public Guid Id { get; set; }

    public Guid? DeviceEntityId { get; set; }

    public Device? Device { get; set; }

    public Guid? ShipmentId { get; set; }

    public Shipment? Shipment { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public string AlertType { get; set; } = string.Empty;

    public AlertSeverity Severity { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public bool IsAcknowledged { get; set; }

    public DateTime? AcknowledgedAtUtc { get; set; }
}
