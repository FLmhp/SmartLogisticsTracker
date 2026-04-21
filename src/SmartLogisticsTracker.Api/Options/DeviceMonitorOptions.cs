namespace SmartLogisticsTracker.Api.Options;

public sealed class DeviceMonitorOptions
{
    public const string SectionName = "DeviceMonitor";

    public int OfflineMinutes { get; set; } = 30;

    public int CheckIntervalSeconds { get; set; } = 60;
}
