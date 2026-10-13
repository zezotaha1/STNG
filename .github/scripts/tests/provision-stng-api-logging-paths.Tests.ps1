#requires -Version 5.1

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$provisioningScriptPath = [System.IO.Path]::GetFullPath((
    Join-Path $PSScriptRoot "..\provision-stng-api-logging.ps1"))

$tokens = $null
$parseErrors = $null
$scriptAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $provisioningScriptPath,
    [ref]$tokens,
    [ref]$parseErrors)

if ($parseErrors.Count -gt 0)
{
    $messages = ($parseErrors | ForEach-Object { $_.Message }) -join [Environment]::NewLine
    throw "Provisioning script syntax validation failed:$([Environment]::NewLine)$messages"
}

$pathValidationFunction = $scriptAst.Find(
    {
        param($node)

        return (
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq "Test-FullyQualifiedWindowsPath")
    },
    $true)

if ($null -eq $pathValidationFunction)
{
    throw "Test-FullyQualifiedWindowsPath was not found in the provisioning script."
}

. ([scriptblock]::Create($pathValidationFunction.Extent.Text))

$testCases = @(
    [pscustomobject]@{ Path = "C:\Deploy\STNG\Logs\Api"; Expected = $true },
    [pscustomobject]@{ Path = "C:\"; Expected = $true },
    [pscustomobject]@{ Path = "C:/Deploy/STNG/Logs/Api"; Expected = $true },
    [pscustomobject]@{ Path = "\\server\share"; Expected = $true },
    [pscustomobject]@{ Path = "\\server\share\Logs\Api"; Expected = $true },
    [pscustomobject]@{ Path = "Logs\Api"; Expected = $false },
    [pscustomobject]@{ Path = ".\Logs\Api"; Expected = $false },
    [pscustomobject]@{ Path = "..\Logs\Api"; Expected = $false },
    [pscustomobject]@{ Path = "C:Logs"; Expected = $false },
    [pscustomobject]@{ Path = "C:"; Expected = $false },
    [pscustomobject]@{ Path = "\Logs"; Expected = $false },
    [pscustomobject]@{ Path = "/Logs"; Expected = $false },
    [pscustomobject]@{ Path = "\\server"; Expected = $false },
    [pscustomobject]@{ Path = ""; Expected = $false },
    [pscustomobject]@{ Path = " "; Expected = $false }
)

foreach ($testCase in $testCases)
{
    $actual = Test-FullyQualifiedWindowsPath -Path $testCase.Path
    if ($actual -ne $testCase.Expected)
    {
        throw (
            "Path validation failed for '{0}'. Expected: {1}; Actual: {2}." -f
            $testCase.Path,
            $testCase.Expected,
            $actual)
    }
}

if (Select-String -LiteralPath $provisioningScriptPath -SimpleMatch "IsPathFullyQualified")
{
    throw "The provisioning script still references Path.IsPathFullyQualified()."
}

Write-Host "Provisioning script syntax and path validation tests passed ($($testCases.Count) cases)."
