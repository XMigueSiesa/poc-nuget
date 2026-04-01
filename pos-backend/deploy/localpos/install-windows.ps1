# ---------------------------------------------------------------------------
# install-windows.ps1 — Install Pos.Host.LocalPOS as a Windows Service
#
# Usage (run as Administrator):
#   .\install-windows.ps1 `
#       -InstallPath "C:\SIESA\LocalPOS" `
#       -ServiceName "PosLocalPOS" `
#       -CloudBaseUrl "https://pos-cloudhub-xyz.a.run.app" `
#       -LocalDbConnectionString "Host=localhost;Database=pos_local;Username=posapp;Password=..." `
#       -ClientId "store-001" `
#       -ClientSecret "super-secret"
# ---------------------------------------------------------------------------

#Requires -RunAsAdministrator

[CmdletBinding()]
param(
    [string] $InstallPath           = "C:\SIESA\LocalPOS",
    [string] $ServiceName           = "PosLocalPOS",
    [Parameter(Mandatory = $true)]
    [string] $CloudBaseUrl,
    [Parameter(Mandatory = $true)]
    [string] $LocalDbConnectionString,
    [Parameter(Mandatory = $true)]
    [string] $ClientId,
    [Parameter(Mandatory = $true)]
    [string] $ClientSecret
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Write-Step {
    param([string]$Message)
    Write-Host "[*] $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[!] $Message" -ForegroundColor Yellow
}

function Write-Fail {
    param([string]$Message)
    Write-Host "[FAIL] $Message" -ForegroundColor Red
}

# ---------------------------------------------------------------------------
# Pre-flight: Administrator check (belt-and-suspenders beyond #Requires)
# ---------------------------------------------------------------------------

$currentPrincipal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Fail "This script must be run as Administrator. Right-click PowerShell and select 'Run as administrator'."
    exit 1
}

$ExePath = Join-Path $InstallPath "Pos.Host.LocalPOS.exe"

# ---------------------------------------------------------------------------
# 1. Create install directory
# ---------------------------------------------------------------------------

Write-Step "Creating install directory: $InstallPath"

if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
    Write-Success "Directory created."
} else {
    Write-Warn "Directory already exists — will update configuration."
}

# ---------------------------------------------------------------------------
# 2. Write appsettings.Production.json
# ---------------------------------------------------------------------------

Write-Step "Writing appsettings.Production.json"

$appSettings = @{
    ConnectionStrings = @{
        LocalDb = $LocalDbConnectionString
    }
    CloudHub = @{
        BaseUrl      = $CloudBaseUrl
        ClientId     = $ClientId
        ClientSecret = $ClientSecret
    }
    Logging = @{
        LogLevel = @{
            Default      = "Information"
            Microsoft    = "Warning"
            "Microsoft.Hosting.Lifetime" = "Information"
        }
    }
}

$appSettingsPath = Join-Path $InstallPath "appsettings.Production.json"
$appSettings | ConvertTo-Json -Depth 10 | Set-Content -Path $appSettingsPath -Encoding UTF8
Write-Success "appsettings.Production.json written to $appSettingsPath"

# ---------------------------------------------------------------------------
# 3. Stop and remove existing service (idempotent re-install)
# ---------------------------------------------------------------------------

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    Write-Step "Stopping existing service '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Write-Step "Removing existing service '$ServiceName'..."
    sc.exe delete $ServiceName | Out-Null
    # Give SCM a moment to remove the service record
    Start-Sleep -Seconds 2
    Write-Success "Existing service removed."
}

# ---------------------------------------------------------------------------
# 4. Validate binary exists before registering
# ---------------------------------------------------------------------------

if (-not (Test-Path $ExePath)) {
    Write-Fail "Executable not found: $ExePath"
    Write-Host "  Publish the application first:" -ForegroundColor Yellow
    Write-Host "    dotnet publish src/Hosts/Pos.Host.LocalPOS -c Release -o `"$InstallPath`"" -ForegroundColor Yellow
    exit 1
}

# ---------------------------------------------------------------------------
# 5. Register as Windows Service (auto-start)
# ---------------------------------------------------------------------------

Write-Step "Registering Windows Service '$ServiceName'..."

$newServiceParams = @{
    Name           = $ServiceName
    BinaryPathName = $ExePath
    DisplayName    = "SIESA POS LocalPOS Service"
    Description    = "SIESA POS modular monolith — local on-premise host with CloudHub sync"
    StartupType    = "Automatic"
}
New-Service @newServiceParams | Out-Null
Write-Success "Service registered."

# ---------------------------------------------------------------------------
# 6. Inject environment variables via registry
# ---------------------------------------------------------------------------

Write-Step "Configuring service environment variables..."

$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$envBlock = @(
    "ASPNETCORE_ENVIRONMENT=Production",
    "DOTNET_EnableDiagnostics=0"
)
Set-ItemProperty -Path $regPath -Name "Environment" -Value $envBlock -Type MultiString
Write-Success "Environment variables set."

# ---------------------------------------------------------------------------
# 7. Configure failure recovery: restart on first / second / subsequent failures
# ---------------------------------------------------------------------------

sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/10000/restart/30000 | Out-Null
Write-Success "Failure recovery configured (restart on failure)."

# ---------------------------------------------------------------------------
# 8. Start the service
# ---------------------------------------------------------------------------

Write-Step "Starting service '$ServiceName'..."
Start-Service -Name $ServiceName

$service = Get-Service -Name $ServiceName
if ($service.Status -eq "Running") {
    Write-Success "Service is running."
} else {
    Write-Fail "Service failed to start. Status: $($service.Status)"
    Write-Host "  Check Event Viewer > Windows Logs > Application for details." -ForegroundColor Yellow
    exit 1
}

# ---------------------------------------------------------------------------
# 9. Summary
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  SIESA POS LocalPOS — Install Summary" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  Service name   : $ServiceName" -ForegroundColor White
Write-Host "  Install path   : $InstallPath" -ForegroundColor White
Write-Host "  Startup type   : Automatic" -ForegroundColor White
Write-Host "  Status         : $($service.Status)" -ForegroundColor Green
Write-Host "  Cloud base URL : $CloudBaseUrl" -ForegroundColor White
Write-Host "  Client ID      : $ClientId" -ForegroundColor White
Write-Host ""
Write-Host "  Logs: Event Viewer > Windows Logs > Application" -ForegroundColor Gray
Write-Host "        Source: $ServiceName" -ForegroundColor Gray
Write-Host "==========================================" -ForegroundColor Cyan
