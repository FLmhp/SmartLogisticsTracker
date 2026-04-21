using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SmartLogisticsTracker.Client.Models;

namespace SmartLogisticsTracker.Client.Services;

public sealed class LogisticsApiClient
{
    private const string BaseUrlPreferenceKey = "api_base_url";
    private const string DefaultWindowsBaseUrl = "http://localhost:5082";
    private const string DefaultAndroidBaseUrl = "http://10.0.2.2:5082";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = new();
    private string? _accessToken;
    private string? _role;

    public string BaseUrl { get; private set; }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_accessToken);

    public bool IsAdmin =>
        string.Equals(_role, "Admin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_role, "SuperAdmin", StringComparison.OrdinalIgnoreCase);

    public string? CurrentRole => _role;

    public LogisticsApiClient()
    {
        var defaultBaseUrl = DeviceInfo.Platform == DevicePlatform.Android
            ? DefaultAndroidBaseUrl
            : DefaultWindowsBaseUrl;
        var stored = Preferences.Default.Get(BaseUrlPreferenceKey, defaultBaseUrl);
        if (!TryNormalizeBaseUrl(stored, out var normalizedBaseUrl))
        {
            normalizedBaseUrl = NormalizeBaseUrl(defaultBaseUrl);
            Preferences.Default.Set(BaseUrlPreferenceKey, normalizedBaseUrl);
        }

        BaseUrl = normalizedBaseUrl;
    }

    public async Task<LoginResponse> LoginAsync(
        string baseUrl,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("API 地址不能为空。");
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("用户名和密码不能为空。");
        }

        BaseUrl = NormalizeBaseUrl(baseUrl);
        Preferences.Default.Set(BaseUrlPreferenceKey, BaseUrl);

        var response = await SendAsync(
            HttpMethod.Post,
            "/api/auth/login",
            new LoginRequest(username.Trim(), password),
            includeAuthorization: false,
            cancellationToken);

        var payload = await DeserializeAsync<LoginResponse>(response, cancellationToken);
        _accessToken = payload.AccessToken;
        _role = payload.Role;
        return payload;
    }

    public void Logout()
    {
        _accessToken = null;
        _role = null;
    }

    public async Task<IReadOnlyList<ShipmentDto>> GetShipmentsAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var response = await SendAsync(HttpMethod.Get, "/api/shipments", null, includeAuthorization: true, cancellationToken);
        return await DeserializeAsync<List<ShipmentDto>>(response, cancellationToken);
    }

    public async Task<ShipmentDto> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var response = await SendAsync(HttpMethod.Post, "/api/shipments", request, includeAuthorization: true, cancellationToken);
        return await DeserializeAsync<ShipmentDto>(response, cancellationToken);
    }

    public async Task CreateDeviceAsync(CreateDeviceRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        _ = await SendAsync(HttpMethod.Post, "/api/devices", request, includeAuthorization: true, cancellationToken);
    }

    public async Task<BindingDto> BindDeviceAsync(BindDeviceRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var response = await SendAsync(HttpMethod.Post, "/api/bindings", request, includeAuthorization: true, cancellationToken);
        return await DeserializeAsync<BindingDto>(response, cancellationToken);
    }

    public async Task<TelemetryIngestionResult> IngestTelemetryAsync(
        IngestTelemetryRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var response = await SendAsync(HttpMethod.Post, "/api/telemetry/ingest", request, includeAuthorization: true, cancellationToken);
        return await DeserializeAsync<TelemetryIngestionResult>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TelemetryDto>> GetTelemetryAsync(
        Guid shipmentId,
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var boundedTake = Math.Clamp(take, 1, 1000);
        var response = await SendAsync(
            HttpMethod.Get,
            $"/api/shipments/{shipmentId}/telemetry?take={boundedTake}",
            null,
            includeAuthorization: true,
            cancellationToken);
        return await DeserializeAsync<List<TelemetryDto>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        Guid shipmentId,
        bool unacknowledgedOnly,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var response = await SendAsync(
            HttpMethod.Get,
            $"/api/shipments/{shipmentId}/alerts?unacknowledgedOnly={unacknowledgedOnly.ToString().ToLowerInvariant()}",
            null,
            includeAuthorization: true,
            cancellationToken);
        return await DeserializeAsync<List<AlertDto>>(response, cancellationToken);
    }

    public async Task AcknowledgeAlertAsync(Guid alertId, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        _ = await SendAsync(new HttpMethod("PATCH"), $"/api/alerts/{alertId}/ack", null, includeAuthorization: true, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        bool includeAuthorization,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (includeAuthorization)
        {
            if (string.IsNullOrWhiteSpace(_accessToken))
            {
                throw new InvalidOperationException("当前未登录。");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"请求失败: {(int)response.StatusCode} {response.ReasonPhrase}\n{details}");
        }

        return response;
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var data = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        if (data is null)
        {
            throw new InvalidOperationException("响应解析失败。");
        }

        return data;
    }

    private void EnsureAuthenticated()
    {
        if (!IsAuthenticated)
        {
            throw new InvalidOperationException("请先登录。");
        }
    }

    private void EnsureAdmin()
    {
        EnsureAuthenticated();
        if (!IsAdmin)
        {
            throw new InvalidOperationException("该操作仅管理员可用。");
        }
    }

    private Uri BuildUri(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("请求路径不能为空。");
        }

        var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
        return new Uri($"{BaseUrl}{normalizedPath}");
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        if (!TryNormalizeBaseUrl(baseUrl, out var normalized))
        {
            throw new InvalidOperationException("API 地址格式不正确，请使用 http://主机:端口。");
        }

        return normalized;
    }

    private static bool TryNormalizeBaseUrl(string baseUrl, out string normalized)
    {
        normalized = string.Empty;
        var trimmed = baseUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (DeviceInfo.Platform == DevicePlatform.Android &&
            (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
             uri.Host == "127.0.0.1"))
        {
            var androidEmulatorUri = new UriBuilder(uri)
            {
                Host = "10.0.2.2"
            }.Uri;

            normalized = androidEmulatorUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            return true;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }
}
