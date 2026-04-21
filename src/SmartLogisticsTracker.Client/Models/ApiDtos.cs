namespace SmartLogisticsTracker.Client.Models;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, string Role);

public sealed record CreateShipmentRequest(
    string WaybillNumber,
    string? Sender,
    string? Receiver,
    string? Origin,
    string? Destination);

public sealed record ShipmentDto(
    Guid Id,
    string WaybillNumber,
    string? Sender,
    string? Receiver,
    string? Origin,
    string? Destination,
    string? Status,
    DateTime CreatedAtUtc);

public sealed record CreateDeviceRequest(string DeviceId, int? BatteryLevel);

public sealed record BindDeviceRequest(string DeviceId, Guid ShipmentId);

public sealed record BindingDto(Guid Id, string DeviceId, string WaybillNumber, DateTime BoundAtUtc);

public sealed record IngestTelemetryRequest(
    string DeviceId,
    int Seq,
    int Ax,
    int Ay,
    int Az,
    int T,
    int H,
    string? Topic);

public sealed record TelemetryIngestionResult(
    bool Accepted,
    bool IsDuplicate,
    Guid? TelemetryRecordId,
    IReadOnlyList<Guid> AlertIds,
    string? ErrorMessage);

public sealed record TelemetryDto(
    Guid Id,
    string DeviceId,
    int Seq,
    int Ax,
    int Ay,
    int Az,
    decimal TemperatureC,
    decimal HumidityRh,
    decimal AccelMagnitudeG,
    DateTime ReceivedAtUtc);

public sealed record AlertDto(
    Guid Id,
    string DeviceId,
    string AlertType,
    string Severity,
    string Message,
    DateTime CreatedAtUtc,
    bool IsAcknowledged,
    DateTime? AcknowledgedAtUtc);
