using System.Text.Json.Serialization;

namespace SmartLogisticsTracker.Api.Contracts;

public sealed record IngestTelemetryRequest(
    string DeviceId,
    int Seq,
    int Ax,
    int Ay,
    int Az,
    int T,
    int H,
    string? Topic);

public sealed record TelemetryPacket(
    int Seq,
    int Ax,
    int Ay,
    int Az,
    int T,
    int H);

/// <summary>
/// MCU MQTT 上传格式：{"seq":1,"t":25.37,"h":65.20,"a":3.85}
/// </summary>
public sealed record McuTelemetryPacket(
    [property: JsonPropertyName("seq")] int Seq,
    [property: JsonPropertyName("t")] decimal T,
    [property: JsonPropertyName("h")] decimal H,
    [property: JsonPropertyName("a")] decimal A);
