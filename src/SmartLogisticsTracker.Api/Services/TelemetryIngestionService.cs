using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartLogisticsTracker.Api.Contracts;
using SmartLogisticsTracker.Api.Data;
using SmartLogisticsTracker.Api.Domain.Entities;
using SmartLogisticsTracker.Api.Domain.Enums;
using SmartLogisticsTracker.Api.Options;

namespace SmartLogisticsTracker.Api.Services;

public sealed record TelemetryIngestionResult(
    bool Accepted,
    bool IsDuplicate,
    Guid? TelemetryRecordId,
    IReadOnlyList<Guid> AlertIds,
    string? ErrorMessage);

public interface ITelemetryIngestionService
{
    Task<TelemetryIngestionResult> IngestAsync(
        string deviceId,
        string topic,
        string payload,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken);
}

public sealed class TelemetryIngestionService(
    LogisticsDbContext dbContext,
    IOptions<AlertRulesOptions> rulesOptions,
    ILogger<TelemetryIngestionService> logger) : ITelemetryIngestionService
{
    private readonly LogisticsDbContext _dbContext = dbContext;
    private readonly AlertRulesOptions _rules = rulesOptions.Value;
    private readonly ILogger<TelemetryIngestionService> _logger = logger;

    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TelemetryIngestionResult> IngestAsync(
        string deviceId,
        string topic,
        string payload,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return new TelemetryIngestionResult(false, false, null, [], "deviceId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return new TelemetryIngestionResult(false, false, null, [], "payload cannot be empty.");
        }

        var rawMessage = new TelemetryRawMessage
        {
            DeviceId = deviceId,
            Topic = topic,
            Payload = payload,
            ReceivedAtUtc = receivedAtUtc,
            ParseSucceeded = false
        };
        _dbContext.TelemetryRawMessages.Add(rawMessage);

        // Try to parse - supports both MCU format {"seq","t","h","a"} and legacy {"Seq","Ax","Ay","Az","T","H"}
        ParsedTelemetry parsed;
        try
        {
            parsed = ParsePayload(payload);
        }
        catch (Exception ex)
        {
            rawMessage.ParseError = ex.Message;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new TelemetryIngestionResult(false, false, null, [], $"Invalid payload: {ex.Message}");
        }

        rawMessage.ParseSucceeded = true;

        var device = await _dbContext.Devices.SingleOrDefaultAsync(x => x.DeviceId == deviceId, cancellationToken);
        if (device is null)
        {
            device = new Device
            {
                DeviceId = deviceId,
                CreatedAtUtc = receivedAtUtc
            };
            _dbContext.Devices.Add(device);
        }

        device.IsOnline = true;
        device.LastSeenAtUtc = receivedAtUtc;

        var duplicate = await _dbContext.TelemetryRecords.AnyAsync(
            x => x.DeviceId == deviceId && x.Seq == parsed.Seq,
            cancellationToken);
        if (duplicate)
        {
            rawMessage.ParseError = "Duplicate sequence ignored.";
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new TelemetryIngestionResult(true, true, null, [], null);
        }

        var activeBinding = await _dbContext.DeviceBindings
            .AsNoTracking()
            .Where(x => x.DeviceEntityId == device.Id && x.IsActive)
            .OrderByDescending(x => x.BoundAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var telemetryRecord = new TelemetryRecord
        {
            DeviceEntityId = device.Id,
            ShipmentId = activeBinding?.ShipmentId,
            DeviceId = deviceId,
            Seq = parsed.Seq,
            Ax = parsed.Ax,
            Ay = parsed.Ay,
            Az = parsed.Az,
            TemperatureC = parsed.TemperatureC,
            HumidityRh = parsed.HumidityRh,
            AccelMagnitudeG = parsed.AccelMagnitudeG,
            ReceivedAtUtc = receivedAtUtc
        };

        _dbContext.TelemetryRecords.Add(telemetryRecord);

        var alertIds = new List<Guid>();
        var alerts = BuildAlerts(device, telemetryRecord, activeBinding?.ShipmentId);
        if (alerts.Count > 0)
        {
            _dbContext.AlertEvents.AddRange(alerts);
            alertIds.AddRange(alerts.Select(x => x.Id));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new TelemetryIngestionResult(true, false, telemetryRecord.Id, alertIds, null);
    }

    /// <summary>
    /// Parses both MCU format {"seq","t","h","a"} and legacy format {"Seq","Ax","Ay","Az","T","H"}.
    /// MCU format: t/h are floats (°C, %RH), a is pre-computed peak magnitude in g.
    /// Legacy format: T/H are integers ×100, Ax/Ay/Az are raw axis values.
    /// </summary>
    private ParsedTelemetry ParsePayload(string payload)
    {
        var json = JsonNode.Parse(payload)
            ?? throw new JsonException("Payload is null.");

        // Detect format by checking for the MCU-specific "a" field
        var aNode = json["a"];
        if (aNode is not null)
        {
            // MCU format: {"seq":1,"t":25.37,"h":65.20,"a":3.85}
            var mcu = json.Deserialize<McuTelemetryPacket>(CaseInsensitiveOptions)
                ?? throw new JsonException("Cannot parse MCU telemetry packet.");

            return new ParsedTelemetry(
                Seq: mcu.Seq,
                Ax: 0,
                Ay: 0,
                Az: 0,
                TemperatureC: mcu.T,
                HumidityRh: mcu.H,
                AccelMagnitudeG: mcu.A);
        }

        // Legacy format: {"Seq":1,"Ax":120,"Ay":-45,"Az":1024,"T":2537,"H":6520}
        var packet = JsonSerializer.Deserialize<TelemetryPacket>(payload, CaseInsensitiveOptions)
            ?? throw new JsonException("Cannot parse legacy telemetry packet.");

        var scale = _rules.AccelerationScale <= 0 ? 1000m : _rules.AccelerationScale;
        var magnitudeG = (decimal)Math.Sqrt(
            (double)(packet.Ax * packet.Ax + packet.Ay * packet.Ay + packet.Az * packet.Az)) / scale;

        return new ParsedTelemetry(
            Seq: packet.Seq,
            Ax: packet.Ax,
            Ay: packet.Ay,
            Az: packet.Az,
            TemperatureC: packet.T / 100m,
            HumidityRh: packet.H / 100m,
            AccelMagnitudeG: Math.Round(magnitudeG, 2));
    }

    private sealed record ParsedTelemetry(
        int Seq, int Ax, int Ay, int Az,
        decimal TemperatureC, decimal HumidityRh, decimal AccelMagnitudeG);

    private List<AlertEvent> BuildAlerts(Device device, TelemetryRecord telemetryRecord, Guid? shipmentId)
    {
        var alerts = new List<AlertEvent>();

        if (shipmentId is null)
        {
            alerts.Add(CreateAlert(
                device,
                shipmentId,
                "UnboundDevice",
                AlertSeverity.Warning,
                $"Device {device.DeviceId} has no active shipment binding."));
        }

        if (telemetryRecord.TemperatureC < _rules.MinTemperatureC || telemetryRecord.TemperatureC > _rules.MaxTemperatureC)
        {
            alerts.Add(CreateAlert(
                device,
                shipmentId,
                "TemperatureThresholdExceeded",
                AlertSeverity.Warning,
                $"Temperature {telemetryRecord.TemperatureC:F2}C is out of range [{_rules.MinTemperatureC}, {_rules.MaxTemperatureC}]."));
        }

        if (telemetryRecord.HumidityRh < _rules.MinHumidityRh || telemetryRecord.HumidityRh > _rules.MaxHumidityRh)
        {
            alerts.Add(CreateAlert(
                device,
                shipmentId,
                "HumidityThresholdExceeded",
                AlertSeverity.Warning,
                $"Humidity {telemetryRecord.HumidityRh:F2}%RH is out of range [{_rules.MinHumidityRh}, {_rules.MaxHumidityRh}]."));
        }

        // Use pre-computed AccelMagnitudeG for shock detection
        if (telemetryRecord.AccelMagnitudeG >= _rules.CriticalShockG)
        {
            alerts.Add(CreateAlert(
                device,
                shipmentId,
                "ShockThresholdExceeded",
                AlertSeverity.Critical,
                $"Shock reached {telemetryRecord.AccelMagnitudeG:F2}g (critical >= {_rules.CriticalShockG}g)."));
        }
        else if (telemetryRecord.AccelMagnitudeG >= _rules.WarningShockG)
        {
            alerts.Add(CreateAlert(
                device,
                shipmentId,
                "ShockThresholdExceeded",
                AlertSeverity.Warning,
                $"Shock reached {telemetryRecord.AccelMagnitudeG:F2}g (warning >= {_rules.WarningShockG}g)."));
        }

        return alerts;
    }

    private AlertEvent CreateAlert(Device device, Guid? shipmentId, string alertType, AlertSeverity severity, string message)
    {
        _logger.LogWarning("Alert created. Type={AlertType}, DeviceId={DeviceId}, Severity={Severity}, Message={Message}",
            alertType,
            device.DeviceId,
            severity,
            message);

        return new AlertEvent
        {
            Id = Guid.NewGuid(),
            DeviceEntityId = device.Id,
            ShipmentId = shipmentId,
            DeviceId = device.DeviceId,
            AlertType = alertType,
            Severity = severity,
            Message = message,
            CreatedAtUtc = DateTime.UtcNow,
            IsAcknowledged = false
        };
    }
}
