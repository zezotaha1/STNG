#requires -Version 5.1
#requires -RunAsAdministrator

[CmdletBinding()]
param
(
    [Parameter(Mandatory = $false)]
    [string]$AppPoolName = "STNG",

    [Parameter(Mandatory = $false)]
    [string]$SitePath = "C:\MySites\STNG",

    [Parameter(Mandatory = $false)]
    [string]$LogDirectory = "C:\Deploy\STNG\Logs\Api",

    [Parameter(Mandatory = $false)]
    [string]$LogFilePath = "",

    [Parameter(Mandatory = $false)]
    [string]$LegacyArchiveDirectory = "C:\Deploy\STNG\Logs\Archive",

    [Parameter(Mandatory = $false)]
    [string]$EnvironmentVariableName = "STNG__Logging__FilePath",

    [Parameter(Mandatory = $false)]
    [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-FullyQualifiedWindowsPath
{
    param
    (
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path))
    {
        return $false
    }

    if ($Path -match '^[A-Za-z]:[\\/]')
    {
        return $true
    }

    return $Path -match '^[\\/]{2}[^\\/]+[\\/][^\\/]+(?:[\\/].*)?$'
}

function Get-NormalizedFullPath
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-PathIsWithin
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$CandidatePath,

        [Parameter(Mandatory = $true)]
        [string]$ParentPath
    )

    $candidate = Get-NormalizedFullPath -Path $CandidatePath
    $parent = Get-NormalizedFullPath -Path $ParentPath
    $parentPrefix = $parent + [System.IO.Path]::DirectorySeparatorChar

    return $candidate.StartsWith(
        $parentPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Import-IisAdministrationAssembly
{
    $assemblyPath = Join-Path $env:WINDIR "System32\inetsrv\Microsoft.Web.Administration.dll"
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf))
    {
        throw "IIS administration assembly was not found: $assemblyPath"
    }

    if (-not ("Microsoft.Web.Administration.ServerManager" -as [type]))
    {
        Add-Type -Path $assemblyPath
    }
}

function Get-IisApplicationPoolEnvironmentVariable
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$PoolName,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    Import-IisAdministrationAssembly
    $serverManager = New-Object Microsoft.Web.Administration.ServerManager

    try
    {
        $configuration = $serverManager.GetApplicationHostConfiguration()
        $section = $configuration.GetSection("system.applicationHost/applicationPools")
        $applicationPools = $section.GetCollection()
        $applicationPool = $null

        foreach ($element in $applicationPools)
        {
            if (
                $element.ElementTagName -eq "add" -and
                [string]$element.GetAttributeValue("name") -eq $PoolName
            )
            {
                $applicationPool = $element
                break
            }
        }

        if ($null -eq $applicationPool)
        {
            throw "IIS Application Pool '$PoolName' does not exist."
        }

        $environmentVariables = $applicationPool.GetCollection("environmentVariables")
        foreach ($element in $environmentVariables)
        {
            if ([string]$element.GetAttributeValue("name") -eq $Name)
            {
                return [string]$element.GetAttributeValue("value")
            }
        }

        return $null
    }
    finally
    {
        $serverManager.Dispose()
    }
}

function Set-IisApplicationPoolEnvironmentVariable
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$PoolName,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    Import-IisAdministrationAssembly
    $serverManager = New-Object Microsoft.Web.Administration.ServerManager

    try
    {
        $configuration = $serverManager.GetApplicationHostConfiguration()
        $section = $configuration.GetSection("system.applicationHost/applicationPools")
        $applicationPools = $section.GetCollection()
        $applicationPool = $null

        foreach ($element in $applicationPools)
        {
            if (
                $element.ElementTagName -eq "add" -and
                [string]$element.GetAttributeValue("name") -eq $PoolName
            )
            {
                $applicationPool = $element
                break
            }
        }

        if ($null -eq $applicationPool)
        {
            throw "IIS Application Pool '$PoolName' does not exist."
        }

        $environmentVariables = $applicationPool.GetCollection("environmentVariables")
        $environmentVariable = $null

        foreach ($element in $environmentVariables)
        {
            if ([string]$element.GetAttributeValue("name") -eq $Name)
            {
                $environmentVariable = $element
                break
            }
        }

        if ($null -eq $environmentVariable)
        {
            $environmentVariable = $environmentVariables.CreateElement("add")
            $environmentVariable["name"] = $Name
            $environmentVariable["value"] = $Value
            $environmentVariables.Add($environmentVariable)
        }
        else
        {
            $environmentVariable["value"] = $Value
        }

        $serverManager.CommitChanges()
    }
    finally
    {
        $serverManager.Dispose()
    }
}

function Set-RestrictedDirectoryAcl
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $false)]
        [System.Security.Principal.SecurityIdentifier]$ApplicationPoolSid
    )

    $systemSid = New-Object System.Security.Principal.SecurityIdentifier("S-1-5-18")
    $administratorsSid = New-Object System.Security.Principal.SecurityIdentifier("S-1-5-32-544")
    $inheritance = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    $propagation = [System.Security.AccessControl.PropagationFlags]::None
    $allow = [System.Security.AccessControl.AccessControlType]::Allow

    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)

    foreach ($existingRule in @($acl.Access))
    {
        [void]$acl.RemoveAccessRuleSpecific($existingRule)
    }

    $acl.SetOwner($administratorsSid)
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
        $systemSid,
        [System.Security.AccessControl.FileSystemRights]::FullControl,
        $inheritance,
        $propagation,
        $allow)))
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
        $administratorsSid,
        [System.Security.AccessControl.FileSystemRights]::FullControl,
        $inheritance,
        $propagation,
        $allow)))

    if ($null -ne $ApplicationPoolSid)
    {
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
            $ApplicationPoolSid,
            [System.Security.AccessControl.FileSystemRights]::Modify,
            $inheritance,
            $propagation,
            $allow)))
    }

    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Test-ApiLogDirectoryAcl
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [System.Security.Principal.SecurityIdentifier]$ApplicationPoolSid,

        [Parameter(Mandatory = $true)]
        [string]$ApplicationPoolName
    )

    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected)
    {
        throw "The API log directory still inherits ACLs: $Path"
    }

    $matchingRule = $null
    $expectedApplicationPoolRights = [System.Security.AccessControl.FileSystemRights]::Modify -bor
        [System.Security.AccessControl.FileSystemRights]::Synchronize
    foreach ($rule in $acl.Access)
    {
        $ruleSid = $rule.IdentityReference.Translate(
            [System.Security.Principal.SecurityIdentifier])

        if (
            $ruleSid.Value -eq $ApplicationPoolSid.Value -and
            $rule.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Allow -and
            $rule.FileSystemRights -eq $expectedApplicationPoolRights
        )
        {
            $matchingRule = $rule
            break
        }
    }

    if ($null -eq $matchingRule)
    {
        throw "The API log directory does not grant Modify access to IIS AppPool\$ApplicationPoolName."
    }

    $broadWriteSids = @("S-1-1-0", "S-1-5-11", "S-1-5-32-545")
    $writeRights = [System.Security.AccessControl.FileSystemRights]::Write -bor
        [System.Security.AccessControl.FileSystemRights]::Delete -bor
        [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [System.Security.AccessControl.FileSystemRights]::ChangePermissions -bor
        [System.Security.AccessControl.FileSystemRights]::TakeOwnership

    foreach ($rule in $acl.Access)
    {
        $ruleSid = $rule.IdentityReference.Translate(
            [System.Security.Principal.SecurityIdentifier])

        if (
            $rule.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Allow -and
            $broadWriteSids -contains $ruleSid.Value -and
            ($rule.FileSystemRights -band $writeRights)
        )
        {
            throw "A broad identity has write access to the API log directory: $($rule.IdentityReference)."
        }
    }
}

function Copy-ExistingApplicationLogs
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$ExistingLogDirectory,

        [Parameter(Mandatory = $true)]
        [string]$ArchiveRoot
    )

    if (-not (Test-Path -LiteralPath $ExistingLogDirectory -PathType Container))
    {
        Write-Host "No existing site-local log directory was found."
        return
    }

    $existingLogFile = Get-ChildItem -LiteralPath $ExistingLogDirectory -File -Recurse |
        Select-Object -First 1

    if ($null -eq $existingLogFile)
    {
        Write-Host "The existing site-local log directory is empty."
        return
    }

    $archivePath = Join-Path $ArchiveRoot (
        "Api-before-external-" + (Get-Date -Format "yyyyMMdd-HHmmssfff"))
    New-Item -ItemType Directory -Path $archivePath -Force | Out-Null

    & robocopy $ExistingLogDirectory $archivePath /E /COPY:DAT /DCOPY:DAT /XJ /R:2 /W:1 /NFL /NDL /NJH /NJS
    $robocopyExitCode = $LASTEXITCODE

    if ($robocopyExitCode -gt 7)
    {
        throw "Failed to preserve existing application logs. Robocopy exit code: $robocopyExitCode."
    }

    Write-Host "Existing application logs were preserved at: $archivePath"
}

if (-not (Test-FullyQualifiedWindowsPath -Path $SitePath))
{
    throw "SitePath must be an absolute path."
}

if (-not (Test-FullyQualifiedWindowsPath -Path $LogDirectory))
{
    throw "LogDirectory must be an absolute path."
}

if ([string]::IsNullOrWhiteSpace($LogFilePath))
{
    $LogFilePath = Join-Path $LogDirectory "stng-api-.log"
}

if (-not (Test-FullyQualifiedWindowsPath -Path $LogFilePath))
{
    throw "LogFilePath must be an absolute path."
}

if (-not (Test-FullyQualifiedWindowsPath -Path $LegacyArchiveDirectory))
{
    throw "LegacyArchiveDirectory must be an absolute path."
}

$siteFullPath = Get-NormalizedFullPath -Path $SitePath
$logDirectoryFullPath = Get-NormalizedFullPath -Path $LogDirectory
$logFileFullPath = [System.IO.Path]::GetFullPath($LogFilePath)
$archiveFullPath = Get-NormalizedFullPath -Path $LegacyArchiveDirectory

if (
    $logDirectoryFullPath.Equals(
        $siteFullPath,
        [System.StringComparison]::OrdinalIgnoreCase) -or
    (Test-PathIsWithin -CandidatePath $logDirectoryFullPath -ParentPath $siteFullPath)
)
{
    throw "LogDirectory must be outside SitePath."
}

if (-not (Test-PathIsWithin -CandidatePath $logFileFullPath -ParentPath $logDirectoryFullPath))
{
    throw "LogFilePath must be inside LogDirectory."
}

if (
    $archiveFullPath.Equals(
        $siteFullPath,
        [System.StringComparison]::OrdinalIgnoreCase) -or
    (Test-PathIsWithin -CandidatePath $archiveFullPath -ParentPath $siteFullPath)
)
{
    throw "LegacyArchiveDirectory must be outside SitePath."
}

[void](Get-IisApplicationPoolEnvironmentVariable -PoolName $AppPoolName -Name $EnvironmentVariableName)

$appPoolIdentity = New-Object System.Security.Principal.NTAccount(
    "IIS AppPool",
    $AppPoolName)
$appPoolSid = $appPoolIdentity.Translate(
    [System.Security.Principal.SecurityIdentifier])

if (-not $VerifyOnly)
{
    New-Item -ItemType Directory -Path $logDirectoryFullPath -Force | Out-Null
    New-Item -ItemType Directory -Path $archiveFullPath -Force | Out-Null

    Set-RestrictedDirectoryAcl -Path $archiveFullPath
    Copy-ExistingApplicationLogs -ExistingLogDirectory (Join-Path $siteFullPath "Logs") -ArchiveRoot $archiveFullPath
    Set-RestrictedDirectoryAcl -Path $logDirectoryFullPath -ApplicationPoolSid $appPoolSid
    Set-IisApplicationPoolEnvironmentVariable -PoolName $AppPoolName -Name $EnvironmentVariableName -Value $logFileFullPath
}

if (-not (Test-Path -LiteralPath $logDirectoryFullPath -PathType Container))
{
    throw "API log directory does not exist: $logDirectoryFullPath"
}

Test-ApiLogDirectoryAcl -Path $logDirectoryFullPath -ApplicationPoolSid $appPoolSid -ApplicationPoolName $AppPoolName

$configuredLogFilePath = Get-IisApplicationPoolEnvironmentVariable -PoolName $AppPoolName -Name $EnvironmentVariableName

if (-not $logFileFullPath.Equals(
    $configuredLogFilePath,
    [System.StringComparison]::OrdinalIgnoreCase))
{
    throw "IIS Application Pool '$AppPoolName' does not have the expected $EnvironmentVariableName value."
}

Write-Host ""
Write-Host "STNG API logging pre-deployment verification passed."
Write-Host "Log directory : $logDirectoryFullPath"
Write-Host "Log file      : $logFileFullPath"
Write-Host "Application pool recycling was intentionally deferred to the normal deployment."
