using Microsoft.EntityFrameworkCore;
using SmartLogisticsTracker.Api.Domain.Entities;

namespace SmartLogisticsTracker.Api.Data;

public sealed class LogisticsDbContext(DbContextOptions<LogisticsDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<DeviceBinding> DeviceBindings => Set<DeviceBinding>();

    public DbSet<TelemetryRawMessage> TelemetryRawMessages => Set<TelemetryRawMessage>();

    public DbSet<TelemetryRecord> TelemetryRecords => Set<TelemetryRecord>();

    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>()
            .HasIndex(x => x.Username)
            .IsUnique();

        modelBuilder.Entity<Device>()
            .HasIndex(x => x.DeviceId)
            .IsUnique();

        modelBuilder.Entity<Shipment>()
            .HasIndex(x => x.WaybillNumber)
            .IsUnique();

        modelBuilder.Entity<DeviceBinding>(entity =>
        {
            entity.HasOne(x => x.Device)
                .WithMany(x => x.Bindings)
                .HasForeignKey(x => x.DeviceEntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Shipment)
                .WithMany(x => x.DeviceBindings)
                .HasForeignKey(x => x.ShipmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TelemetryRecord>(entity =>
        {
            entity.HasIndex(x => new { x.DeviceId, x.Seq }).IsUnique();

            entity.HasOne(x => x.Device)
                .WithMany(x => x.TelemetryRecords)
                .HasForeignKey(x => x.DeviceEntityId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Shipment)
                .WithMany(x => x.TelemetryRecords)
                .HasForeignKey(x => x.ShipmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AlertEvent>(entity =>
        {
            entity.HasOne(x => x.Device)
                .WithMany(x => x.AlertEvents)
                .HasForeignKey(x => x.DeviceEntityId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Shipment)
                .WithMany(x => x.AlertEvents)
                .HasForeignKey(x => x.ShipmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
