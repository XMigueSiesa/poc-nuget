# ---------------------------------------------------------------------------
# CloudHub.Dockerfile — Multi-stage build for Pos.Host.CloudHub
# Context: pos-backend/
# Build:   docker build -f docker/CloudHub.Dockerfile -t pos-cloudhub .
# ---------------------------------------------------------------------------

# ---- Stage 1: Build & Publish -------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /src

# Layer 1 — restore dependencies (cached until csproj/props/config change)
COPY Directory.Build.props ./
COPY nuget.config ./
COPY src/Hosts/Pos.Host.CloudHub/Pos.Host.CloudHub.csproj \
     src/Hosts/Pos.Host.CloudHub/

RUN dotnet restore src/Hosts/Pos.Host.CloudHub/Pos.Host.CloudHub.csproj \
    -p:UseProjectReference=false

# Layer 2 — copy source and publish
COPY src/ src/

RUN dotnet publish src/Hosts/Pos.Host.CloudHub/Pos.Host.CloudHub.csproj \
    -c Release \
    -p:UseProjectReference=false \
    -o /app/publish \
    --no-restore

# ---- Stage 2: Runtime ---------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

# Security: non-root user
RUN addgroup -S posgrp && adduser -S posapp -G posgrp

WORKDIR /app
COPY --from=build /app/publish .

# Runtime configuration
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

USER posapp

ENTRYPOINT ["dotnet", "Pos.Host.CloudHub.dll"]
