param
(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$SitePath,

    [Parameter(Mandatory = $true)]
    [string]$BackupPath,

    [Parameter(Mandatory = $true)]
    [string]$AppPoolName,

    [Parameter(Mandatory = $true)]
    [string]$HealthUrl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"


Write-Host "========================================"
Write-Host " STNG Production Deployment"
Write-Host "========================================"


Import-Module WebAdministration


# ----------------------------------------------------------
# Validate package
# ----------------------------------------------------------

if (-not (Test-Path $SourcePath))
{
    throw "Deployment source does not exist: $SourcePath"
}


# ----------------------------------------------------------
# Create folders
# ----------------------------------------------------------

New-Item `
    -ItemType Directory `
    -Force `
    -Path $SitePath |
    Out-Null

New-Item `
    -ItemType Directory `
    -Force `
    -Path $BackupPath |
    Out-Null


# ----------------------------------------------------------
# Backup current deployment
# ----------------------------------------------------------

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

$currentBackup = Join-Path `
    $BackupPath `
    $timestamp

Write-Host ""
Write-Host "Creating backup:"
Write-Host $currentBackup


New-Item `
    -ItemType Directory `
    -Force `
    -Path $currentBackup |
    Out-Null


if (Test-Path "$SitePath\*")
{
    robocopy `
        $SitePath `
        $currentBackup `
        /MIR `
        /R:2 `
        /W:2 `
        /NFL `
        /NDL `
        /NJH `
        /NJS

    if ($LASTEXITCODE -gt 7)
    {
        throw "Backup failed. Robocopy exit code: $LASTEXITCODE"
    }
}


# ----------------------------------------------------------
# Stop IIS application pool
# ----------------------------------------------------------

Write-Host ""
Write-Host "Stopping IIS App Pool: $AppPoolName"


$appPoolPath = "IIS:\AppPools\$AppPoolName"

if (-not (Test-Path $appPoolPath))
{
    throw "IIS App Pool does not exist: $AppPoolName"
}


$state = (Get-WebAppPoolState -Name $AppPoolName).Value

if ($state -ne "Stopped")
{
    Stop-WebAppPool -Name $AppPoolName
}


Start-Sleep -Seconds 2


# ----------------------------------------------------------
# Deploy
# ----------------------------------------------------------

try
{
    Write-Host ""
    Write-Host "Deploying application..."


    robocopy `
        $SourcePath `
        $SitePath `
        /MIR `
        /R:3 `
        /W:2 `
        /NFL `
        /NDL `
        /NJH `
        /NJS


    if ($LASTEXITCODE -gt 7)
    {
        throw "Deployment copy failed. Robocopy exit code: $LASTEXITCODE"
    }


    # ------------------------------------------------------
    # Start IIS
    # ------------------------------------------------------

    Write-Host ""
    Write-Host "Starting IIS App Pool..."


    Start-WebAppPool `
        -Name $AppPoolName


    Start-Sleep -Seconds 5


    # ------------------------------------------------------
    # Health Check
    # ------------------------------------------------------

    Write-Host ""
    Write-Host "Health check:"
    Write-Host $HealthUrl


    $response = Invoke-WebRequest `
        -Uri $HealthUrl `
        -UseBasicParsing `
        -TimeoutSec 30


    if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300)
    {
        throw "Health check failed with HTTP $($response.StatusCode)"
    }


    Write-Host ""
    Write-Host "========================================"
    Write-Host " DEPLOYMENT SUCCESSFUL"
    Write-Host "========================================"
}
catch
{
    Write-Host ""
    Write-Host "Deployment failed:"
    Write-Host $_


    # ------------------------------------------------------
    # Rollback
    # ------------------------------------------------------

    Write-Host ""
    Write-Host "Rolling back application files..."


    try
    {
        Stop-WebAppPool `
            -Name $AppPoolName `
            -ErrorAction SilentlyContinue


        robocopy `
            $currentBackup `
            $SitePath `
            /MIR `
            /R:3 `
            /W:2 `
            /NFL `
            /NDL `
            /NJH `
            /NJS


        Start-WebAppPool `
            -Name $AppPoolName


        Write-Host ""
        Write-Host "Previous application version restored."
    }
    catch
    {
        Write-Host ""
        Write-Host "ROLLBACK FAILED!"
        Write-Host $_
    }


    throw
}