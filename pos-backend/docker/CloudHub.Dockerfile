# ---------------------------------------------------------------------------
# CloudHub.Dockerfile — multi-stage build for Pos.Host.CloudHub
# Context: pos-backend/
# Build:   docker build -f docker/CloudHub.Dockerfile -t pos-cloudhub .
# ---------------------------------------------------------------------------

# ---- Stage 1: build & publish ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy build configuration first for restore layer caching.
# With UseProjectReference=false the host consumes NuGet packages,
# so only the host csproj is needed for restore.
COPY Directory.Build.props ./
COPY nuget.config ./
COPY src/Hosts/Pos.Host.CloudHub/Pos.Host.CloudHub.csproj \
     src/Hosts/Pos.Host.CloudHub/

RUN dotnet restore src/Hosts/Pos.Host.CloudHub/Pos.Host.CloudHub.csproj \
    -p:UseProjectReference=false

# Copy all source and publish
COPY src/ src/
RUN dotnet publish src/Hosts/Pos.Host.CloudHub/Pos.Host.CloudHub.csproj \
    -c Release \
    -p:UseProjectReference=false \
    -o /app/publish \
    --no-restore

# ---- Stage 2: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

RUN addgroup -S posapp && adduser -S posapp -G posapp

WORKDIR /app
COPY --from=build /app/publish ./

# Cloud Run injects PORT but defaults to 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

USER posapp
ENTRYPOINT ["dotnet", "Pos.Host.CloudHub.dll"]
