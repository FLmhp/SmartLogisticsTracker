namespace SmartLogisticsTracker.Api.Contracts;

public sealed record CreateShipmentRequest(
    string WaybillNumber,
    string? Sender,
    string? Receiver,
    string? Origin,
    string? Destination);
