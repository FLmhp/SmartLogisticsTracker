using System.Text;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using SmartLogisticsTracker.Api.Options;

namespace SmartLogisticsTracker.Api.Services;

public sealed class MqttIngestionHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<MqttOptions> mqttOptions,
    ILogger<MqttIngestionHostedService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly MqttOptions _mqttOptions = mqttOptions.Value;
    private readonly ILogger<MqttIngestionHostedService> _logger = logger;
    private IMqttClient? _mqttClient;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_mqttOptions.Enabled)
        {
            _logger.LogInformation("MQTT ingestion is disabled.");
            return;
        }

        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();

        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _mqttClient.DisconnectedAsync += async args =>
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            _logger.LogWarning("MQTT disconnected. Reconnecting in 5 seconds...");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await ConnectAndSubscribeAsync(stoppingToken);
        };

        await ConnectAndSubscribeAsync(stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ConnectAndSubscribeAsync(CancellationToken cancellationToken)
    {
        if (_mqttClient is null)
        {
            throw new InvalidOperationException("MQTT client is not initialized.");
        }

        if (_mqttClient.IsConnected)
        {
            return;
        }

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
            .WithClientId(_mqttOptions.ClientId);

        if (!string.IsNullOrWhiteSpace(_mqttOptions.Username))
        {
            optionsBuilder = optionsBuilder.WithCredentials(_mqttOptions.Username, _mqttOptions.Password);
        }

        await _mqttClient.ConnectAsync(optionsBuilder.Build(), cancellationToken);
        await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
            .WithTopic(_mqttOptions.TopicFilter)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .Build(), cancellationToken);

        _logger.LogInformation("MQTT connected. Subscribed to {TopicFilter}", _mqttOptions.TopicFilter);
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic ?? string.Empty;
        var payloadBytes = args.ApplicationMessage.PayloadSegment.ToArray();
        var payload = Encoding.UTF8.GetString(payloadBytes);
        var deviceId = ExtractDeviceIdFromTopic(topic);

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            _logger.LogWarning("Ignore MQTT message because deviceId cannot be extracted from topic {Topic}", topic);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var ingestionService = scope.ServiceProvider.GetRequiredService<ITelemetryIngestionService>();
        await ingestionService.IngestAsync(deviceId, topic, payload, DateTime.UtcNow, CancellationToken.None);
    }

    private static string? ExtractDeviceIdFromTopic(string topic)
    {
        // Expected: tracker/{deviceId}/telemetry
        var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : null;
    }
}
