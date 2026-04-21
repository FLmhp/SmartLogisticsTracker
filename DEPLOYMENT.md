# SmartLogisticsTracker 使用与部署指南

本文覆盖本项目的本地开发、客户端调试、发布脚本、Docker 部署与生产环境注意事项。

## 1. 项目概览

- `src\SmartLogisticsTracker.Api`：ASP.NET Core 8 Minimal API
- `src\SmartLogisticsTracker.Client`：.NET MAUI 客户端（Windows / Android）
- 默认数据库：SQLite
- 可选数据库：PostgreSQL
- 可选接入：MQTT 遥测订阅

## 2. 环境要求

### 开发环境

| 组件 | 用途 | 建议版本 |
| --- | --- | --- |
| .NET SDK | 构建整个解决方案 | 10.x |
| .NET Runtime / ASP.NET Core Runtime | 运行 API | 8.x |
| MAUI workload | 构建 Windows / Android 客户端 | `maui-windows`、`maui-android` |
| Visual Studio 2026 | MAUI 调试、Android 模拟器 | 含 .NET MAUI 与 Android 组件 |
| Android SDK / Emulator | Android 调试 | 由 VS 安装器维护 |
| Docker Desktop | 容器化部署 API | 最新稳定版 |

### 推荐检查命令

```powershell
dotnet --info
dotnet workload list
docker --version
docker compose version
```

## 3. 目录说明

```text
.
├─ .gitignore
├─ .dockerignore
├─ docker-compose.yml
├─ publish.ps1
├─ DEPLOYMENT.md
├─ SmartLogisticsTracker.slnx
└─ src
   ├─ SmartLogisticsTracker.Api
   │  ├─ appsettings.json
   │  ├─ Dockerfile
   │  └─ Program.cs
   └─ SmartLogisticsTracker.Client
      ├─ App.xaml
      ├─ MauiProgram.cs
      └─ Platforms
```

## 4. 本地开发启动

### 4.1 还原与构建

```powershell
dotnet restore .\SmartLogisticsTracker.slnx
dotnet build .\SmartLogisticsTracker.slnx -c Debug
```

### 4.2 启动 API

```powershell
dotnet run --project .\src\SmartLogisticsTracker.Api\SmartLogisticsTracker.Api.csproj
```

默认开发环境下：
- 会按 `appsettings.json` 初始化数据库
- Swagger 默认可通过 `http://localhost:5082/swagger` 访问
- 健康检查地址为 `http://localhost:5082/api/health`

### 4.3 启动 Windows 客户端

```powershell
dotnet run --project .\src\SmartLogisticsTracker.Client\SmartLogisticsTracker.Client.csproj -f net10.0-windows10.0.19041.0
```

### 4.4 启动 Android 客户端

```powershell
dotnet run --project .\src\SmartLogisticsTracker.Client\SmartLogisticsTracker.Client.csproj -f net10.0-android
```

> Android 模拟器访问宿主机 API 时，请使用 `http://10.0.2.2:5082`。

## 5. 默认账号

- `superadmin / superadmin123`
- `admin / admin123`
- `user / user123`

## 6. 配置说明

API 主配置文件：`src\SmartLogisticsTracker.Api\appsettings.json`

### 6.1 数据库

| 配置项 | 含义 | 默认值 |
| --- | --- | --- |
| `Database:Provider` | 使用的数据库提供方 | `Sqlite` |
| `Database:AutoInitialize` | 启动时自动建库与种子数据 | `true` |
| `ConnectionStrings:Sqlite` | SQLite 连接串 | `Data Source=smartlogistics-mvp.db` |
| `ConnectionStrings:PostgreSql` | PostgreSQL 连接串 | 已提供开发默认值 |

### 6.2 JWT

| 配置项 | 含义 |
| --- | --- |
| `Jwt:Issuer` | Token 签发者 |
| `Jwt:Audience` | Token 接收方 |
| `Jwt:SigningKey` | 签名密钥 |
| `Jwt:AccessTokenExpireMinutes` | Access Token 过期时间 |

> 生产环境必须更换 `Jwt:SigningKey`。

### 6.3 MQTT

| 配置项 | 含义 | 默认值 |
| --- | --- | --- |
| `Mqtt:Enabled` | 是否启用 MQTT 接入 | `false` |
| `Mqtt:Host` | Broker 地址 | `127.0.0.1` |
| `Mqtt:Port` | Broker 端口 | `1883` |
| `Mqtt:TopicFilter` | 订阅主题 | `tracker/+/telemetry` |

## 7. 一键发布脚本 `publish.ps1`

脚本支持 API、Windows 客户端、Android 客户端和 Docker 镜像构建。

### 7.1 常见用法

```powershell
# 发布全部 .NET 产物
.\publish.ps1

# 清理后发布，并打包 zip
.\publish.ps1 -Clean -Zip -Version 1.0.0

# 只发布 API，自包含 win-x64
.\publish.ps1 -Target api -Runtime win-x64 -Version 1.0.0

# 发布 Windows 客户端
.\publish.ps1 -Target client-win -Version 1.0.0

# 发布 Android APK
.\publish.ps1 -Target client-android -Version 1.0.0

# 构建 Docker 镜像
.\publish.ps1 -Target docker -Version 1.0.0
```

### 7.2 参数说明

| 参数 | 作用 |
| --- | --- |
| `-Target` | `api` / `client-win` / `client-android` / `docker` / `all` |
| `-Configuration` | `Debug` 或 `Release` |
| `-Runtime` | API 发布目标 RID，如 `win-x64`、`linux-x64` |
| `-SelfContained` | API 是否自包含运行时 |
| `-Version` | 发布版本号 |
| `-OutputRoot` | 产物输出目录，默认 `.\publish` |
| `-DockerImage` | Docker 镜像名 |
| `-DockerPlatform` | Docker 构建平台，默认 `linux/amd64` |
| `-Clean` | 清理旧产物与 `bin/obj` |
| `-Zip` | 对 .NET 产物额外打 zip |
| `-SkipRestore` | 跳过 `dotnet restore` |

### 7.3 产物目录

默认发布结果输出到：

```text
publish\
├─ api\
├─ client-win\
└─ client-android\
```

## 8. 手动发布

### 8.1 API

```powershell
dotnet publish .\src\SmartLogisticsTracker.Api\SmartLogisticsTracker.Api.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o .\publish\api
```

### 8.2 Windows 客户端

```powershell
dotnet publish .\src\SmartLogisticsTracker.Client\SmartLogisticsTracker.Client.csproj `
  -c Release `
  -f net10.0-windows10.0.19041.0 `
  -p:WindowsPackageType=None `
  -o .\publish\client-win
```

### 8.3 Android 客户端

```powershell
dotnet publish .\src\SmartLogisticsTracker.Client\SmartLogisticsTracker.Client.csproj `
  -c Release `
  -f net10.0-android `
  -p:AndroidPackageFormat=apk `
  -o .\publish\client-android
```

## 9. Docker 部署

### 9.1 直接构建镜像

```powershell
docker build -f .\src\SmartLogisticsTracker.Api\Dockerfile --build-arg VERSION=1.0.0 -t smartlogistics-api:1.0.0 .
```

### 9.2 直接运行容器

```powershell
docker run -d `
  --name smartlogistics-api `
  -p 8080:8080 `
  -e ASPNETCORE_ENVIRONMENT=Production `
  -e Jwt__SigningKey=ChangeMe_To_A_Long_Random_Secret `
  -v smartlogistics-api-data:/app/data `
  smartlogistics-api:1.0.0
```

容器内：
- API 监听端口：`8080`
- SQLite 数据目录：`/app/data`
- 健康检查地址：`/api/health`

### 9.3 使用 `docker compose`

1. 复制配置模板：

```powershell
Copy-Item .\.env.example .\.env
```

2. 根据需要修改 `.env`：

```env
VERSION=1.0.0
API_PORT=8080
ASPNETCORE_ENVIRONMENT=Production
JWT_SIGNING_KEY=ChangeMe_To_A_Long_Random_Secret
MQTT_ENABLED=false
MQTT_HOST=127.0.0.1
MQTT_PORT=1883
```

3. 启动：

```powershell
docker compose up -d --build
```

4. 查看状态：

```powershell
docker compose ps
docker compose logs -f api
```

5. 停止并清理：

```powershell
docker compose down
```

## 10. 切换到 PostgreSQL

当前 `docker-compose.yml` 已预留 PostgreSQL 样板配置，但默认注释。

启用时需要：
1. 取消 `postgres` 服务注释
2. 在 `api.environment` 中启用：
   - `Database__Provider=PostgreSql`
   - `ConnectionStrings__PostgreSql=...`
3. 建议将数据库凭据放在 `.env` 中

## 11. 生产部署建议

### 11.1 Windows 服务器

- 使用 `publish.ps1 -Target api -Runtime win-x64`
- 将 `publish\api` 整个目录复制到服务器
- 以 Windows 服务、计划任务或 NSSM 方式托管
- 确保开放 API 端口并配置反向代理（IIS / Nginx）

### 11.2 Linux 服务器

- 优先使用 Docker 部署
- 若不用 Docker，可改为：

```powershell
.\publish.ps1 -Target api -Runtime linux-x64
```

- 将产物复制到 Linux 主机后运行 systemd 托管

### 11.3 Android 客户端分发

- 使用 `publish.ps1 -Target client-android`
- 生成 APK 后安装到真机或模拟器
- 若要正式发布，需要替换签名配置并使用正式 keystore

## 12. 排障

### API 启动失败

- 检查 `appsettings.json` 与环境变量是否冲突
- 检查 SQLite 文件目录是否可写
- 检查端口是否被占用

### Android 客户端无法访问 API

- 模拟器使用 `10.0.2.2`
- 真机需改为局域网 IP
- 检查 API 是否实际监听对应端口

### Docker 无法启动

- 确认 Docker Desktop 已启动
- 确认 Linux Engine 正常运行
- 执行：

```powershell
docker version
docker compose version
```

### 容器一直 unhealthy

- 访问容器内健康检查接口：

```powershell
docker exec -it smartlogistics-api curl -fsS http://127.0.0.1:8080/api/health
```

- 若接口不通，查看：

```powershell
docker compose logs -f api
```

## 13. 当前验证结果

截至本文撰写时，已确认：

- `dotnet build .\SmartLogisticsTracker.slnx` 可成功完成
- `publish.ps1 -Target api` 可成功发布 API
- `docker compose config` 校验通过

> 未能完成真实 Docker 镜像构建验证，因为本机 Docker daemon 当时未启动。
