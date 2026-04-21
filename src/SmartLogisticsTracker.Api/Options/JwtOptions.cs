namespace SmartLogisticsTracker.Api.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "SmartLogisticsTracker";

    public string Audience { get; set; } = "SmartLogisticsTracker.Client";

    public string SigningKey { get; set; } = "ReplaceWithAtLeast32CharactersForProduction";

    public int AccessTokenExpireMinutes { get; set; } = 120;
}
