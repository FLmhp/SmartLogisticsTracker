namespace SmartLogisticsTracker.Api.Domain.Entities;

public sealed class TelemetryRecord
{
    public Guid Id { get; set; }

    public Guid? DeviceEntityId { get; set; }

    public Device? Device { get; set; }

    public Guid? ShipmentId { get; set; }

    public Shipment? Shipment { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public int Seq { get; set; }

    public int Ax { get; set; }

    public int Ay { get; set; }

    public int Az { get; set; }

    public decimal TemperatureC { get; set; }

    public decimal HumidityRh { get; set; }

    /// <summary>Peak acceleration magnitude in g (sqrt(ax²+ay²+az²) or MCU pre-computed value).</summary>
    public decimal AccelMagnitudeG { get; set; }

    public DateTime ReceivedAtUtc { get; set; }
}
