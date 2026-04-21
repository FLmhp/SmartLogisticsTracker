using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SmartLogisticsTracker.Api.Contracts;
using SmartLogisticsTracker.Api.Data;
using SmartLogisticsTracker.Api.Domain.Entities;
using SmartLogisticsTracker.Api.Domain.Enums;
using SmartLogisticsTracker.Api.Options;
using SmartLogisticsTracker.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));
builder.Services.Configure<AlertRulesOptions>(builder.Configuration.GetSection(AlertRulesOptions.SectionName));
builder.Services.Configure<DeviceMonitorOptions>(builder.Configuration.GetSection(DeviceMonitorOptions.SectionName));

var databaseProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";
builder.Services.AddDbContext<LogisticsDbContext>(options =>
{
    if (string.Equals(databaseProvider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
    {
        var connectionString = builder.Configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required for PostgreSql provider.");
        options.UseNpgsql(connectionString);
        return;
    }

    if (string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        var connectionString = builder.Configuration.GetConnectionString("Sqlite")
            ?? throw new InvalidOperationException("ConnectionStrings:Sqlite is required for Sqlite provider.");
        options.UseSqlite(connectionString);
        return;
    }

    throw new InvalidOperationException($"Unsupported database provider: {databaseProvider}");
});

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT options are missing.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy =>
        policy.RequireRole(UserRole.SuperAdmin.ToString(), UserRole.Admin.ToString()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartLogisticsTracker API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Input your JWT token in this format: Bearer {token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddScoped<ITelemetryIngestionService, TelemetryIngestionService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddSingleton<IPasswordHasher, Sha256PasswordHasher>();
builder.Services.AddHostedService<MqttIngestionHostedService>();
builder.Services.AddHostedService<DeviceOfflineMonitorHostedService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Configuration.GetValue<bool>("Database:AutoInitialize"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<LogisticsDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

    await dbContext.Database.EnsureCreatedAsync();

    // Add AccelMagnitudeG column if upgrading from older schema
    try
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE TelemetryRecords ADD COLUMN AccelMagnitudeG TEXT NOT NULL DEFAULT '0'");
    }
    catch { /* Column already exists */ }

    await SeedData.InitializeAsync(dbContext, passwordHasher);
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestampUtc = DateTime.UtcNow }));

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    LogisticsDbContext dbContext,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest("Username and password are required.");
    }

    var user = await dbContext.Users.SingleOrDefaultAsync(
        x => x.Username == request.Username,
        cancellationToken);

    if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
    {
        return Results.Unauthorized();
    }

    var token = tokenService.CreateToken(user);
    return Results.Ok(new LoginResponse(token.Token, token.ExpiresAtUtc, user.Role.ToString()));
});

app.MapGet("/api/shipments", async (LogisticsDbContext dbContext, CancellationToken cancellationToken) =>
{
    var shipments = await dbContext.Shipments
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new
        {
            x.Id,
            x.WaybillNumber,
            x.Sender,
            x.Receiver,
            x.Origin,
            x.Destination,
            x.Status,
            x.CreatedAtUtc
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(shipments);
}).RequireAuthorization();

app.MapPost("/api/shipments", async (
    CreateShipmentRequest request,
    LogisticsDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.WaybillNumber))
    {
        return Results.BadRequest("WaybillNumber is required.");
    }

    var exists = await dbContext.Shipments.AnyAsync(
        x => x.WaybillNumber == request.WaybillNumber,
        cancellationToken);

    if (exists)
    {
        return Results.Conflict($"Shipment {request.WaybillNumber} already exists.");
    }

    var shipment = new Shipment
    {
        WaybillNumber = request.WaybillNumber,
        Sender = request.Sender,
        Receiver = request.Receiver,
        Origin = request.Origin,
        Destination = request.Destination,
        Status = "InTransit",
        CreatedAtUtc = DateTime.UtcNow
    };

    dbContext.Shipments.Add(shipment);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/shipments/{shipment.Id}", shipment);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/devices", async (
    CreateDeviceRequest request,
    LogisticsDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.DeviceId))
    {
        return Results.BadRequest("DeviceId is required.");
    }

    var exists = await dbContext.Devices.AnyAsync(
        x => x.DeviceId == request.DeviceId,
        cancellationToken);

    if (exists)
    {
        return Results.Conflict($"Device {request.DeviceId} already exists.");
    }

    var device = new Device
    {
        DeviceId = request.DeviceId,
        BatteryLevel = request.BatteryLevel,
        IsOnline = false,
        CreatedAtUtc = DateTime.UtcNow
    };

    dbContext.Devices.Add(device);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/devices/{device.Id}", device);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/bindings", async (
    BindDeviceRequest request,
    LogisticsDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.DeviceId))
    {
        return Results.BadRequest("DeviceId is required.");
    }

    var device = await dbContext.Devices.SingleOrDefaultAsync(
        x => x.DeviceId == request.DeviceId,
        cancellationToken);

    if (device is null)
    {
        return Results.NotFound($"Device {request.DeviceId} not found.");
    }

    var shipment = await dbContext.Shipments.SingleOrDefaultAsync(
        x => x.Id == request.ShipmentId,
        cancellationToken);

    if (shipment is null)
    {
        return Results.NotFound($"Shipment {request.ShipmentId} not found.");
    }

    var activeBindings = await dbContext.DeviceBindings
        .Where(x => x.DeviceEntityId == device.Id && x.IsActive)
        .ToListAsync(cancellationToken);

    foreach (var activeBinding in activeBindings)
    {
        activeBinding.IsActive = false;
        activeBinding.UnboundAtUtc = DateTime.UtcNow;
    }

    var binding = new DeviceBinding
    {
        DeviceEntityId = device.Id,
        ShipmentId = shipment.Id,
        IsActive = true,
        BoundAtUtc = DateTime.UtcNow
    };

    dbContext.DeviceBindings.Add(binding);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new { binding.Id, device.DeviceId, shipment.WaybillNumber, binding.BoundAtUtc });
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/telemetry/ingest", async (
    IngestTelemetryRequest request,
    ITelemetryIngestionService ingestionService,
    CancellationToken cancellationToken) =>
{
    var topic = request.Topic ?? $"tracker/{request.DeviceId}/telemetry";
    var payload = JsonSerializer.Serialize(new TelemetryPacket(request.Seq, request.Ax, request.Ay, request.Az, request.T, request.H));

    var result = await ingestionService.IngestAsync(request.DeviceId, topic, payload, DateTime.UtcNow, cancellationToken);
    if (!result.Accepted)
    {
        return Results.BadRequest(result.ErrorMessage);
    }

    return Results.Ok(result);
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/shipments/{shipmentId:guid}/telemetry", async (
    Guid shipmentId,
    int? take,
    LogisticsDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var size = take is > 0 and <= 1000 ? take.Value : 200;
    var rows = await dbContext.TelemetryRecords
        .Where(x => x.ShipmentId == shipmentId)
        .OrderByDescending(x => x.ReceivedAtUtc)
        .Take(size)
        .Select(x => new
        {
            x.Id,
            x.DeviceId,
            x.Seq,
            x.Ax,
            x.Ay,
            x.Az,
            x.TemperatureC,
            x.HumidityRh,
            x.AccelMagnitudeG,
            x.ReceivedAtUtc
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/api/shipments/{shipmentId:guid}/alerts", async (
    Guid shipmentId,
    bool unacknowledgedOnly,
    LogisticsDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var query = dbContext.AlertEvents
        .Where(x => x.ShipmentId == shipmentId);

    if (unacknowledgedOnly)
    {
        query = query.Where(x => !x.IsAcknowledged);
    }

    var alerts = await query
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new
        {
            x.Id,
            x.DeviceId,
            x.AlertType,
            Severity = x.Severity.ToString(),
            x.Message,
            x.CreatedAtUtc,
            x.IsAcknowledged,
            x.AcknowledgedAtUtc
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(alerts);
}).RequireAuthorization();

app.MapPatch("/api/alerts/{alertId:guid}/ack", async (
    Guid alertId,
    LogisticsDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var alert = await dbContext.AlertEvents.SingleOrDefaultAsync(x => x.Id == alertId, cancellationToken);
    if (alert is null)
    {
        return Results.NotFound();
    }

    if (!alert.IsAcknowledged)
    {
        alert.IsAcknowledged = true;
        alert.AcknowledgedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    return Results.NoContent();
}).RequireAuthorization("AdminOnly");

app.Run();
