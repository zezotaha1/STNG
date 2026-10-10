param
(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$SitePath,

    [Parameter(Mandatory = $true)]
    [string]$BackupPath,

    [Parameter(Mandatory = $true)]
    [string]$HealthUrl,

    [Parameter(Mandatory = $false)]
    [int]$BackupRetentionCount = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"


Write-Host "========================================"
Write-Host " STNG Deployment"
Write-Host "========================================"


# ==========================================================
# Helpers
# ==========================================================

function Invoke-RobocopyMirror
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    Write-Host ""
    Write-Host "Mirroring files..."
    Write-Host "Source      : $Source"
    Write-Host "Destination : $Destination"

    & robocopy `
        $Source `
        $Destination `
        /MIR `
        /R:3 `
        /W:2 `
        /NFL `
        /NDL `
        /NJH `
        /NJS

    $exitCode = $LASTEXITCODE

    # Robocopy exit codes 0-7 are successful/non-fatal.
    if ($exitCode -gt 7)
    {
        throw "Robocopy failed with exit code $exitCode."
    }

    Write-Host "Robocopy completed with exit code $exitCode."
}


function Enable-AppOffline
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $offlineFile = Join-Path `
        $Path `
        "app_offline.htm"

    @"
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title>STNG Maintenance</title>
</head>
<body>
    <h2>STNG is being updated.</h2>
</body>
</html>
"@ | Set-Content `
        -Path $offlineFile `
        -Encoding UTF8

    Write-Host "Application placed offline."
}


function Disable-AppOffline
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $offlineFile = Join-Path `
        $Path `
        "app_offline.htm"

    if (Test-Path $offlineFile)
    {
        Remove-Item `
            -Path $offlineFile `
            -Force
    }

    Write-Host "Application brought online."
}


function Test-StngHealth
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$Url
    )

    Write-Host ""
    Write-Host "Health check:"
    Write-Host $Url

    $maxAttempts = 12
    $delaySeconds = 5

    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++)
    {
        try
        {
            Write-Host "Health check attempt $attempt of $maxAttempts..."

            $response = Invoke-WebRequest `
                -Uri $Url `
                -UseBasicParsing `
                -TimeoutSec 15

            if (
                $response.StatusCode -ge 200 -and
                $response.StatusCode -lt 300
            )
            {
                Write-Host "Health check passed."
                return
            }

            Write-Host "Health check returned HTTP $($response.StatusCode)."
        }
        catch
        {
            Write-Host "Application not ready: $($_.Exception.Message)"
        }

        if ($attempt -lt $maxAttempts)
        {
            Start-Sleep -Seconds $delaySeconds
        }
    }

    throw "STNG health check failed after $maxAttempts attempts."
}


function Remove-OldBackups
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [int]$Keep
    )

    if ($Keep -lt 1)
    {
        throw "BackupRetentionCount must be greater than zero."
    }

    Write-Host ""
    Write-Host "Checking backup retention..."

    $oldBackups = Get-ChildItem `
        -Path $Path `
        -Directory `
        -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        Select-Object -Skip $Keep

    foreach ($oldBackup in $oldBackups)
    {
        Write-Host "Removing old backup: $($oldBackup.FullName)"

        Remove-Item `
            -Path $oldBackup.FullName `
            -Recurse `
            -Force
    }
}


# ==========================================================
# Validate deployment paths
# ==========================================================

if (-not (Test-Path $SourcePath))
{
    throw "Deployment source does not exist: $SourcePath"
}


$sourceFullPath = [System.IO.Path]::GetFullPath(
    $SourcePath
)

$siteFullPath = [System.IO.Path]::GetFullPath(
    $SitePath
)

$backupFullPath = [System.IO.Path]::GetFullPath(
    $BackupPath
)


if (
    $sourceFullPath.Equals(
        $siteFullPath,
        [System.StringComparison]::OrdinalIgnoreCase
    )
)
{
    throw "SourcePath and SitePath cannot be the same."
}


if (
    $siteFullPath -eq
    [System.IO.Path]::GetPathRoot($siteFullPath)
)
{
    throw "Refusing to deploy to a drive root."
}


if (
    $backupFullPath.StartsWith(
        $siteFullPath.TrimEnd('\') + '\',
        [System.StringComparison]::OrdinalIgnoreCase
    )
)
{
    throw "BackupPath cannot be inside SitePath."
}


if ($BackupRetentionCount -lt 1)
{
    throw "BackupRetentionCount must be greater than zero."
}


# ==========================================================
# Prepare directories
# ==========================================================

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


# ==========================================================
# Detect previous deployment
# ==========================================================

$existingFiles = @(
    Get-ChildItem `
        -Path $SitePath `
        -Force `
        -ErrorAction SilentlyContinue
)


$hasPreviousDeployment = `
    $existingFiles.Count -gt 0


Write-Host ""

if ($hasPreviousDeployment)
{
    Write-Host "Previous STNG deployment detected."
}
else
{
    Write-Host "First STNG deployment detected."
}


# ==========================================================
# Create backup
# ==========================================================

$currentBackup = $null


if ($hasPreviousDeployment)
{
    $timestamp = Get-Date `
        -Format "yyyyMMdd-HHmmss"

    $currentBackup = Join-Path `
        $BackupPath `
        $timestamp


    New-Item `
        -ItemType Directory `
        -Force `
        -Path $currentBackup |
        Out-Null


    Write-Host ""
    Write-Host "Creating backup:"
    Write-Host $currentBackup


    Invoke-RobocopyMirror `
        -Source $SitePath `
        -Destination $currentBackup
}
else
{
    Write-Host ""
    Write-Host "Backup skipped."
    Write-Host "This is the first STNG deployment."
}


# ==========================================================
# Deployment paths
# ==========================================================

$siteOfflineFile = Join-Path `
    $SitePath `
    "app_offline.htm"


$sourceOfflineFile = Join-Path `
    $SourcePath `
    "app_offline.htm"


# ==========================================================
# Deployment
# ==========================================================

try
{
    # ------------------------------------------------------
    # Put application offline
    # ------------------------------------------------------

    Write-Host ""
    Write-Host "Taking STNG offline..."


    Enable-AppOffline `
        -Path $SitePath


    # Give ASP.NET Core Module time to stop the process
    # and release application files.

    Start-Sleep -Seconds 3


    # ------------------------------------------------------
    # Preserve app_offline.htm during /MIR
    # ------------------------------------------------------

    Copy-Item `
        -Path $siteOfflineFile `
        -Destination $sourceOfflineFile `
        -Force


    # ------------------------------------------------------
    # Deploy new release
    # ------------------------------------------------------

    Write-Host ""
    Write-Host "Deploying STNG..."


    Invoke-RobocopyMirror `
        -Source $SourcePath `
        -Destination $SitePath


    # ------------------------------------------------------
    # Remove temporary offline file from artifact
    # ------------------------------------------------------

    if (Test-Path $sourceOfflineFile)
    {
        Remove-Item `
            -Path $sourceOfflineFile `
            -Force
    }


    # ------------------------------------------------------
    # Bring application online
    # ------------------------------------------------------

    Disable-AppOffline `
        -Path $SitePath


    # ------------------------------------------------------
    # Health check
    # ------------------------------------------------------

    Test-StngHealth `
        -Url $HealthUrl


    # ------------------------------------------------------
    # Backup retention
    # ------------------------------------------------------

    Remove-OldBackups `
        -Path $BackupPath `
        -Keep $BackupRetentionCount


    # ------------------------------------------------------
    # Success
    # ------------------------------------------------------

    Write-Host ""
    Write-Host "========================================"
    Write-Host " DEPLOYMENT SUCCESSFUL"
    Write-Host "========================================"
}
catch
{
    $deploymentError = $_


    Write-Host ""
    Write-Host "========================================"
    Write-Host " DEPLOYMENT FAILED"
    Write-Host "========================================"

    Write-Host $deploymentError


    # ======================================================
    # First deployment failure
    # ======================================================

    if (-not $hasPreviousDeployment)
    {
        Write-Host ""
        Write-Host "This was the first STNG deployment."
        Write-Host "No previous deployment exists for rollback."

        Write-Host ""
        Write-Host "The application will remain offline because"
        Write-Host "there is no known-good version to restore."

        throw $deploymentError
    }


    # ======================================================
    # Rollback
    # ======================================================

    Write-Host ""
    Write-Host "Rolling back to previous deployment..."


    try
    {
        # --------------------------------------------------
        # Ensure application is offline
        # --------------------------------------------------

        Enable-AppOffline `
            -Path $SitePath


        Start-Sleep -Seconds 3


        # --------------------------------------------------
        # Keep app_offline.htm during rollback mirror
        # --------------------------------------------------

        $backupOfflineFile = Join-Path `
            $currentBackup `
            "app_offline.htm"


        Copy-Item `
            -Path $siteOfflineFile `
            -Destination $backupOfflineFile `
            -Force


        # --------------------------------------------------
        # Restore previous release
        # --------------------------------------------------

        Invoke-RobocopyMirror `
            -Source $currentBackup `
            -Destination $SitePath


        # --------------------------------------------------
        # Remove temporary backup offline file
        # --------------------------------------------------

        if (Test-Path $backupOfflineFile)
        {
            Remove-Item `
                -Path $backupOfflineFile `
                -Force
        }


        # --------------------------------------------------
        # Bring restored application online
        # --------------------------------------------------

        Disable-AppOffline `
            -Path $SitePath


        Write-Host ""
        Write-Host "Previous STNG deployment restored."


        # --------------------------------------------------
        # Validate restored application
        # --------------------------------------------------

        Test-StngHealth `
            -Url $HealthUrl


        Write-Host ""
        Write-Host "Rollback health check passed."
    }
    catch
    {
        Write-Host ""
        Write-Host "========================================"
        Write-Host " ROLLBACK FAILED"
        Write-Host "========================================"

        Write-Host $_

        Write-Host ""
        Write-Host "STNG may remain offline."
        Write-Host "Manual intervention is required."
    }


    throw $deploymentError
}
finally
{
    # ======================================================
    # Cleanup temporary artifact file
    # ======================================================

    if (Test-Path $sourceOfflineFile)
    {
        Remove-Item `
            -Path $sourceOfflineFile `
            -Force `
            -ErrorAction SilentlyContinue
    }
}