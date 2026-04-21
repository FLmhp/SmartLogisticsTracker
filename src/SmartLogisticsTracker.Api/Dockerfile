# ============================================================
#  SmartLogisticsTracker.Api — Dockerfile
#  - Multi-stage, multi-arch (buildx) friendly
#  - Non-root runtime
#  - SQLite data persisted via /app/data volume
#  - Health endpoint: GET /api/health
#  Build context = repository root
# ============================================================

ARG DOTNET_VERSION=8.0
ARG VERSION=1.0.0

# ---------- Build ----------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG TARGETARCH
ARG VERSION
WORKDIR /src

# csproj first → cached restore layer
COPY src/SmartLogisticsTracker.Api/SmartLogisticsTracker.Api.csproj src/SmartLogisticsTracker.Api/
RUN dotnet restore src/SmartLogisticsTracker.Api/SmartLogisticsTracker.Api.csproj -a $TARGETARCH

# Sources
COPY src/SmartLogisticsTracker.Api/ src/SmartLogisticsTracker.Api/
RUN dotnet publish src/SmartLogisticsTracker.Api/SmartLogisticsTracker.Api.csproj \
        -c Release \
        -a $TARGETARCH \
        --no-restore \
        -p:UseAppHost=false \
        -p:Version=${VERSION} \
        -p:AssemblyVersion=${VERSION}.0 \
        -p:FileVersion=${VERSION}.0 \
        -o /app/publish

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
ARG VERSION

LABEL org.opencontainers.image.title="SmartLogisticsTracker.Api" \
      org.opencontainers.image.description="Smart logistics tracker backend API" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.licenses="Proprietary"

# Install curl for HTTP-level healthcheck (tiny)
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Non-root user + data dir
RUN mkdir -p /app/data \
    && if ! getent group app >/dev/null; then groupadd -g 1000 app; fi \
    && if ! id -u app >/dev/null 2>&1; then useradd -u 1000 -g app -m -s /usr/sbin/nologin app; fi \
    && chown -R app:app /app

COPY --from=build --chown=app:app /app/publish ./

USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Database__Provider=Sqlite \
    Database__AutoInitialize=true \
    ConnectionStrings__Sqlite="Data Source=/app/data/smartlogistics-mvp.db"

EXPOSE 8080
VOLUME ["/app/data"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "SmartLogisticsTracker.Api.dll"]
