using Microsoft.EntityFrameworkCore;
using SmartLogisticsTracker.Api.Domain.Entities;
using SmartLogisticsTracker.Api.Domain.Enums;
using SmartLogisticsTracker.Api.Services;

namespace SmartLogisticsTracker.Api.Data;

public static class SeedData
{
    public static async Task InitializeAsync(LogisticsDbContext dbContext, IPasswordHasher passwordHasher)
    {
        var now = DateTime.UtcNow;

        if (!await dbContext.Users.AnyAsync())
        {
            dbContext.Users.AddRange(
                new AppUser
                {
                    Id = Guid.NewGuid(),
                    Username = "superadmin",
                    PasswordHash = passwordHasher.Hash("superadmin123"),
                    Role = UserRole.SuperAdmin,
                    CreatedAtUtc = now
                },
                new AppUser
                {
                    Id = Guid.NewGuid(),
                    Username = "admin",
                    PasswordHash = passwordHasher.Hash("admin123"),
                    Role = UserRole.Admin,
                    CreatedAtUtc = now
                },
                new AppUser
                {
                    Id = Guid.NewGuid(),
                    Username = "user",
                    PasswordHash = passwordHasher.Hash("user123"),
                    Role = UserRole.User,
                    CreatedAtUtc = now
                });
        }

        if (!await dbContext.Shipments.AnyAsync())
        {
            dbContext.Shipments.Add(new Shipment
            {
                Id = Guid.NewGuid(),
                WaybillNumber = "WB-2026-0001",
                Sender = "NJU Lab",
                Receiver = "Customer A",
                Origin = "Nanjing",
                Destination = "Shanghai",
                Status = "InTransit",
                CreatedAtUtc = now
            });
        }

        if (!await dbContext.Devices.AnyAsync())
        {
            dbContext.Devices.Add(new Device
            {
                Id = Guid.NewGuid(),
                DeviceId = "DEV-0001",
                IsOnline = false,
                BatteryLevel = 100,
                CreatedAtUtc = now
            });
        }

        await dbContext.SaveChangesAsync();
    }
}
