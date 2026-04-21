namespace SmartLogisticsTracker.Api.Domain.Entities;

public sealed class DeviceBinding
{
    public Guid Id { get; set; }

    public Guid DeviceEntityId { get; set; }

    public Device Device { get; set; } = null!;

    public Guid ShipmentId { get; set; }

    public Shipment Shipment { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime BoundAtUtc { get; set; }

    public DateTime? UnboundAtUtc { get; set; }
}
