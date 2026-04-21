namespace SmartLogisticsTracker.Api.Options;

public sealed class AlertRulesOptions
{
    public const string SectionName = "AlertRules";

    public decimal MinTemperatureC { get; set; } = -20m;

    public decimal MaxTemperatureC { get; set; } = 60m;

    public decimal MinHumidityRh { get; set; } = 10m;

    public decimal MaxHumidityRh { get; set; } = 85m;

    public decimal WarningShockG { get; set; } = 3m;

    public decimal CriticalShockG { get; set; } = 5m;

    public decimal AccelerationScale { get; set; } = 1000m;
}
