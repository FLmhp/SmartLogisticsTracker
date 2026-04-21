# SmartLogisticsTracker（MVP 初版）

基于 `plan.md` 与需求文档落地的物流状态追踪系统 MVP，当前已包含 **后端 API + Windows/Android 客户端**，聚焦“**收、存、查、看、告警**”闭环能力。

## 当前实现范围

- 账号登录与 JWT 鉴权（`SuperAdmin / Admin / User`）
- 设备管理、运单管理、设备-运单绑定
- 遥测接入（HTTP + MQTT 订阅）
- 原始报文与结构化数据双存储
- 告警生成与确认（温湿度、冲击、失联、未绑定设备）
- 运单维度的遥测与告警查询
- .NET MAUI 客户端（Windows + Android）：登录、运单浏览、遥测查看、告警查看与确认、管理员快捷操作（建单/建设备/绑定/模拟上报）

## 技术栈

- .NET 8 / ASP.NET Core Minimal API
- .NET MAUI（Windows / Android）
- Entity Framework Core
- SQLite（默认）/ PostgreSQL（可切换）
- MQTTnet
- Swagger/OpenAPI

## 目录结构

```text
.
├─ SmartLogisticsTracker.slnx
└─ src/
   ├─ SmartLogisticsTracker.Api/
   │  ├─ Contracts/
   │  ├─ Data/
   │  ├─ Domain/
   │  ├─ Options/
   │  ├─ Services/
   │  ├─ Program.cs
   │  └─ appsettings.json
   └─ SmartLogisticsTracker.Client/
      ├─ MainPage.xaml
      ├─ Services/
      ├─ Models/
      └─ Platforms/
```

## 快速启动

### 1. 环境要求

- .NET SDK 10+（可同时构建 `net8.0` API 与 `net10.0` MAUI 客户端）
- 已安装 MAUI 工作负载：`maui-windows`、`maui-android`

### 2. 构建

```powershell
dotnet build SmartLogisticsTracker.slnx
```

默认开发环境会自动初始化数据库与种子数据（`Database:AutoInitialize=true`）。

### 3. 启动后端 API

```powershell
dotnet run --project src\SmartLogisticsTracker.Api\SmartLogisticsTracker.Api.csproj
```

### 4. Swagger

开发环境启动后访问：

- `http://localhost:5082/swagger`（或终端输出中的实际地址）

### 5. 启动 MAUI Windows 客户端

```powershell
dotnet run --project src\SmartLogisticsTracker.Client\SmartLogisticsTracker.Client.csproj -f net10.0-windows10.0.19041.0
```

### 6. 启动 MAUI Android 客户端（模拟器）

```powershell
dotnet run --project src\SmartLogisticsTracker.Client\SmartLogisticsTracker.Client.csproj -f net10.0-android
```

Android 模拟器访问本机 API 时，客户端默认地址应使用 `http://10.0.2.2:5082`。
若手动输入了 `localhost` 或 `127.0.0.1`，Android 客户端会自动转换为 `10.0.2.2`；仍提示连接失败时，先确认后端 API 已在 `http://localhost:5082` 运行。

## 默认账号

- `superadmin / superadmin123`
- `admin / admin123`
- `user / user123`

## 核心配置（`src/SmartLogisticsTracker.Api/appsettings.json`）

| 配置项 | 说明 | 默认值 |
| --- | --- | --- |
| `Database:Provider` | 数据库提供方 | `Sqlite` |
| `ConnectionStrings:Sqlite` | SQLite 连接串 | `Data Source=smartlogistics-mvp.db` |
| `ConnectionStrings:PostgreSql` | PostgreSQL 连接串 | `Host=127.0.0.1;...` |
| `Database:AutoInitialize` | 启动时自动建库与种子 | `true` |
| `Jwt:*` | JWT 签发参数 | 已提供开发默认值 |
| `Mqtt:Enabled` | MQTT 接入开关 | `false` |
| `Mqtt:TopicFilter` | MQTT 订阅主题 | `tracker/+/telemetry` |
| `AlertRules:*` | 告警阈值（温湿度/冲击） | 按 MVP 默认值 |
| `DeviceMonitor:*` | 设备失联检测参数 | `30min / 60s` |

> 生产环境请务必替换 `Jwt:SigningKey` 与数据库凭据。

## MQTT 与数据契约

- 推荐主题：`tracker/{deviceId}/telemetry`
- Payload（JSON）：

```json
{"seq":5,"ax":-12,"ay":3,"az":256,"t":2350,"h":5512}
```

字段说明：
- `seq`：设备递增序号（用于幂等去重）
- `ax/ay/az`：三轴加速度整数值
- `t`：温度放大 100 倍（`2350 => 23.50°C`）
- `h`：湿度放大 100 倍（`5512 => 55.12%RH`）

## API 概览

| 方法 | 路径 | 说明 | 鉴权 |
| --- | --- | --- | --- |
| POST | `/api/auth/login` | 登录获取 JWT | 否 |
| GET | `/api/health` | 健康检查 | 否 |
| GET | `/api/shipments` | 运单列表 | 是 |
| POST | `/api/shipments` | 新建运单 | AdminOnly |
| POST | `/api/devices` | 新建设备 | AdminOnly |
| POST | `/api/bindings` | 设备绑定运单 | AdminOnly |
| POST | `/api/telemetry/ingest` | HTTP 注入遥测 | AdminOnly |
| GET | `/api/shipments/{shipmentId}/telemetry` | 运单遥测查询 | 是 |
| GET | `/api/shipments/{shipmentId}/alerts` | 运单告警查询 | 是 |
| PATCH | `/api/alerts/{alertId}/ack` | 告警确认 | AdminOnly |

## 最小联调示例（PowerShell）

### 1. 登录拿 Token

```powershell
$loginBody = @{
  username = "admin"
  password = "admin123"
} | ConvertTo-Json

$token = (Invoke-RestMethod -Method Post -Uri "http://localhost:5082/api/auth/login" -ContentType "application/json" -Body $loginBody).accessToken
```

### 2. 创建运单

```powershell
$headers = @{ Authorization = "Bearer $token" }
$shipmentBody = @{
  waybillNumber = "WB-2026-0001"
  sender = "NJU Lab"
  receiver = "Customer A"
  origin = "Nanjing"
  destination = "Shanghai"
} | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri "http://localhost:5082/api/shipments" -Headers $headers -ContentType "application/json" -Body $shipmentBody
```

---

当前版本已可用于 Windows/Android 客户端联调与业务流程验证。后续可按 MVP 规划继续扩展短信/微信登录、报表导出与高级算法管理能力。

## 详细使用与部署文档

完整的本地开发、发布、Docker 部署与生产环境建议见：

- [`DEPLOYMENT.md`](DEPLOYMENT.md)
