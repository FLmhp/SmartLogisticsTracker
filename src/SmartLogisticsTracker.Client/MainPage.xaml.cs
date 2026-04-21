using System.Collections.ObjectModel;
using SmartLogisticsTracker.Client.Models;
using SmartLogisticsTracker.Client.Services;

namespace SmartLogisticsTracker.Client;

public partial class MainPage : ContentPage
{
    private readonly LogisticsApiClient _apiClient = new();
    private readonly ObservableCollection<ShipmentDto> _shipments = [];
    private readonly ObservableCollection<TelemetryDto> _telemetry = [];
    private readonly ObservableCollection<AlertDto> _alerts = [];

    private ShipmentDto? _selectedShipment;
    private AlertDto? _selectedAlert;
    private bool _isBusy;

    public MainPage()
    {
        InitializeComponent();

        ShipmentsCollectionView.ItemsSource = _shipments;
        TelemetryCollectionView.ItemsSource = _telemetry;
        AlertsCollectionView.ItemsSource = _alerts;

        BaseUrlEntry.Text = _apiClient.BaseUrl;
        UsernameEntry.Text = "admin";
        PasswordEntry.Text = "admin123";

        SeqEntry.Text = "1";
        AxEntry.Text = "100";
        AyEntry.Text = "50";
        AzEntry.Text = "200";
        TEntry.Text = "2300";
        HEntry.Text = "5000";

        RefreshUiState();
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            var login = await _apiClient.LoginAsync(
                BaseUrlEntry.Text ?? string.Empty,
                UsernameEntry.Text ?? string.Empty,
                PasswordEntry.Text ?? string.Empty);

            SetStatus($"登录成功，角色={login.Role}，过期时间={login.ExpiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC。");
            await LoadShipmentsAsync();
        });
    }

    private void OnLogoutClicked(object? sender, EventArgs e)
    {
        _apiClient.Logout();
        _selectedShipment = null;
        _selectedAlert = null;
        _shipments.Clear();
        _telemetry.Clear();
        _alerts.Clear();
        SelectedShipmentLabel.Text = "当前未选中运单。";
        SetStatus("已退出登录。");
        RefreshUiState();
    }

    private async void OnRefreshShipmentsClicked(object? sender, EventArgs e)
    {
        await ExecuteAsync(LoadShipmentsAsync);
    }

    private async void OnCreateShipmentClicked(object? sender, EventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            var waybill = (WaybillEntry.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(waybill))
            {
                throw new InvalidOperationException("请输入运单号。");
            }

            _ = await _apiClient.CreateShipmentAsync(new CreateShipmentRequest(
                waybill,
                "Client Sender",
                "Client Receiver",
                "Nanjing",
                "Shanghai"));

            WaybillEntry.Text = string.Empty;
            SetStatus($"运单 {waybill} 创建成功。");
            await LoadShipmentsAsync();
        });
    }

    private async void OnCreateDeviceClicked(object? sender, EventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            var deviceId = (DeviceIdEntry.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new InvalidOperationException("请输入设备号。");
            }

            int? battery = null;
            if (!string.IsNullOrWhiteSpace(BatteryEntry.Text))
            {
                if (!int.TryParse(BatteryEntry.Text, out var parsedBattery))
                {
                    throw new InvalidOperationException("电量必须是整数。");
                }

                battery = parsedBattery;
            }

            await _apiClient.CreateDeviceAsync(new CreateDeviceRequest(deviceId, battery));
            SetStatus($"设备 {deviceId} 创建成功。");
        });
    }

    private async void OnBindDeviceClicked(object? sender, EventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            if (_selectedShipment is null)
            {
                throw new InvalidOperationException("请先选择一个运单。");
            }

            var deviceId = (DeviceIdEntry.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new InvalidOperationException("请输入设备号。");
            }

            var binding = await _apiClient.BindDeviceAsync(new BindDeviceRequest(deviceId, _selectedShipment.Id));
            SetStatus($"绑定成功：{binding.DeviceId} -> {binding.WaybillNumber}");
        });
    }

    private async void OnIngestTelemetryClicked(object? sender, EventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            var deviceId = (DeviceIdEntry.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new InvalidOperationException("请输入设备号（用于上报 telemetry）。");
            }

            var request = new IngestTelemetryRequest(
                deviceId,
                ParseRequiredInt(SeqEntry.Text, "seq"),
                ParseRequiredInt(AxEntry.Text, "ax"),
                ParseRequiredInt(AyEntry.Text, "ay"),
                ParseRequiredInt(AzEntry.Text, "az"),
                ParseRequiredInt(TEntry.Text, "t"),
                ParseRequiredInt(HEntry.Text, "h"),
                null);

            var result = await _apiClient.IngestTelemetryAsync(request);
            SetStatus($"遥测上报成功，Accepted={result.Accepted}, Duplicate={result.IsDuplicate}, Alerts={result.AlertIds.Count}");

            if (_selectedShipment is not null)
            {
                await LoadTelemetryAsync(_selectedShipment.Id);
                await LoadAlertsAsync(_selectedShipment.Id);
            }
        });
    }

    private async void OnRefreshTelemetryClicked(object? sender, EventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            if (_selectedShipment is null)
            {
                throw new InvalidOperationException("请先选择一个运单。");
            }

            await LoadTelemetryAsync(_selectedShipment.Id);
            SetStatus("遥测刷新完成。");
        });
    }

    private async void OnRefreshAlertsClicked(object? sender, EventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            if (_selectedShipment is null)
            {
                throw new InvalidOperationException("请先选择一个运单。");
            }

            await LoadAlertsAsync(_selectedShipment.Id);
            SetStatus("告警刷新完成。");
        });
    }

    private async void OnAcknowledgeAlertClicked(object? sender, EventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            if (_selectedShipment is null)
            {
                throw new InvalidOperationException("请先选择一个运单。");
            }

            if (_selectedAlert is null)
            {
                throw new InvalidOperationException("请先选择一个告警。");
            }

            await _apiClient.AcknowledgeAlertAsync(_selectedAlert.Id);
            SetStatus($"告警已确认：{_selectedAlert.AlertType}");
            await LoadAlertsAsync(_selectedShipment.Id);
        });
    }

    private async void OnShipmentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedShipment = e.CurrentSelection.FirstOrDefault() as ShipmentDto;
        _selectedAlert = null;
        if (_selectedShipment is null)
        {
            SelectedShipmentLabel.Text = "当前未选中运单。";
            _telemetry.Clear();
            _alerts.Clear();
            RefreshUiState();
            return;
        }

        SelectedShipmentLabel.Text = $"当前运单: {_selectedShipment.WaybillNumber} ({_selectedShipment.Id})";

        await ExecuteAsync(async () =>
        {
            await LoadTelemetryAsync(_selectedShipment.Id);
            await LoadAlertsAsync(_selectedShipment.Id);
            SetStatus($"已加载运单 {_selectedShipment.WaybillNumber} 的遥测与告警。");
        });
    }

    private void OnAlertSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedAlert = e.CurrentSelection.FirstOrDefault() as AlertDto;
        RefreshUiState();
    }

    private async Task LoadShipmentsAsync()
    {
        var shipments = await _apiClient.GetShipmentsAsync();
        ReplaceCollection(_shipments, shipments);

        if (_selectedShipment is not null)
        {
            _selectedShipment = _shipments.FirstOrDefault(x => x.Id == _selectedShipment.Id);
        }

        SetStatus($"运单刷新完成，共 {_shipments.Count} 条。");
    }

    private async Task LoadTelemetryAsync(Guid shipmentId)
    {
        var telemetry = await _apiClient.GetTelemetryAsync(shipmentId);
        ReplaceCollection(_telemetry, telemetry);
    }

    private async Task LoadAlertsAsync(Guid shipmentId)
    {
        var alerts = await _apiClient.GetAlertsAsync(shipmentId, UnacknowledgedOnlySwitch.IsToggled);
        ReplaceCollection(_alerts, alerts);
        _selectedAlert = null;
    }

    private async Task ExecuteAsync(Func<Task> operation)
    {
        if (_isBusy)
        {
            return;
        }

        try
        {
            _isBusy = true;
            BusyIndicator.IsVisible = true;
            BusyIndicator.IsRunning = true;
            RefreshUiState();
            await operation();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            _isBusy = false;
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            RefreshUiState();
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = isError ? Colors.IndianRed : Colors.DarkGreen;
    }

    private void RefreshUiState()
    {
        var isAuthenticated = _apiClient.IsAuthenticated;
        var isAdmin = _apiClient.IsAdmin;
        var shipmentSelected = _selectedShipment is not null;
        var alertSelected = _selectedAlert is not null;

        BaseUrlEntry.IsEnabled = !_isBusy && !isAuthenticated;
        UsernameEntry.IsEnabled = !_isBusy && !isAuthenticated;
        PasswordEntry.IsEnabled = !_isBusy && !isAuthenticated;
        LoginButton.IsEnabled = !_isBusy && !isAuthenticated;
        LogoutButton.IsEnabled = !_isBusy && isAuthenticated;

        RefreshShipmentsButton.IsEnabled = !_isBusy && isAuthenticated;
        ShipmentsCollectionView.IsEnabled = !_isBusy;

        CreateShipmentButton.IsEnabled = !_isBusy && isAuthenticated && isAdmin;
        CreateDeviceButton.IsEnabled = !_isBusy && isAuthenticated && isAdmin;
        BindButton.IsEnabled = !_isBusy && isAuthenticated && isAdmin && shipmentSelected;
        IngestTelemetryButton.IsEnabled = !_isBusy && isAuthenticated && isAdmin;

        RefreshTelemetryButton.IsEnabled = !_isBusy && isAuthenticated && shipmentSelected;
        RefreshAlertsButton.IsEnabled = !_isBusy && isAuthenticated && shipmentSelected;
        AcknowledgeAlertButton.IsEnabled = !_isBusy && isAuthenticated && isAdmin && shipmentSelected && alertSelected;
    }

    private static int ParseRequiredInt(string? text, string fieldName)
    {
        if (!int.TryParse(text, out var value))
        {
            throw new InvalidOperationException($"{fieldName} 必须是整数。");
        }

        return value;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
