namespace SmartLogisticsTracker.Api.Contracts;

public sealed record CreateDeviceRequest(string DeviceId, int? BatteryLevel);

public sealed record BindDeviceRequest(string DeviceId, Guid ShipmentId);
