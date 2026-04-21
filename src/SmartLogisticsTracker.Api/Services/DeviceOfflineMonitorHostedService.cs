using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartLogisticsTracker.Api.Data;
using SmartLogisticsTracker.Api.Domain.Entities;
using SmartLogisticsTracker.Api.Domain.Enums;
using SmartLogisticsTracker.Api.Options;

namespace SmartLogisticsTracker.Api.Services;

public sealed class DeviceOfflineMonitorHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<DeviceMonitorOptions> monitorOptions) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly DeviceMonitorOptions _monitorOptions = monitorOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(10, _monitorOptions.CheckIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CheckOfflineDevicesAsync(stoppingToken);
        }
    }

    private async Task CheckOfflineDevicesAsync(CancellationToken cancellationToken)
    {
        var threshold = DateTime.UtcNow.AddMinutes(-Math.Max(1, _monitorOptions.OfflineMinutes));

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LogisticsDbContext>();
        var devices = await dbContext.Devices
            .Where(x => x.IsOnline && x.LastSeenAtUtc.HasValue && x.LastSeenAtUtc < threshold)
            .ToListAsync(cancellationToken);

        if (devices.Count == 0)
        {
            return;
        }

        foreach (var device in devices)
        {
            device.IsOnline = false;

            var hasOpenOfflineAlert = await dbContext.AlertEvents.AnyAsync(
                x => x.DeviceId == device.DeviceId &&
                     x.AlertType == "DeviceOffline" &&
                     !x.IsAcknowledged,
                cancellationToken);

            if (hasOpenOfflineAlert)
            {
                continue;
            }

            dbContext.AlertEvents.Add(new AlertEvent
            {
                Id = Guid.NewGuid(),
                DeviceEntityId = device.Id,
                ShipmentId = await ResolveShipmentIdAsync(dbContext, device.Id, cancellationToken),
                DeviceId = device.DeviceId,
                AlertType = "DeviceOffline",
                Severity = AlertSeverity.Warning,
                Message = $"Device {device.DeviceId} appears offline. Last seen at {device.LastSeenAtUtc:O}.",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Guid?> ResolveShipmentIdAsync(
        LogisticsDbContext dbContext,
        Guid deviceEntityId,
        CancellationToken cancellationToken)
    {
        var activeBinding = await dbContext.DeviceBindings
            .AsNoTracking()
            .Where(x => x.DeviceEntityId == deviceEntityId && x.IsActive)
            .OrderByDescending(x => x.BoundAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return activeBinding?.ShipmentId;
    }
}
