namespace SmartLogisticsTracker.Api.Options;

public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    public bool Enabled { get; set; }

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1883;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string TopicFilter { get; set; } = "tracker/+/telemetry";

    public string ClientId { get; set; } = "smart-logistics-api-ingestor";
}
