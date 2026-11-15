<#
.SYNOPSIS
    Starts the whole system for local development: .NET API, Python AI service and Angular UI,
    each in its own PowerShell window.

.PARAMETER SqlServer
    SQL Server instance that hosts DataControlsDb (default: .\SQLEXPRESS).

.PARAMETER CheckOnly
    Run the prerequisite checks only, without starting anything.

.EXAMPLE
    .\run-all.ps1
    .\run-all.ps1 -CheckOnly
#>
[CmdletBinding()]
param(
    [string]$SqlServer = '.\SQLEXPRESS',
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$apiDir = Join-Path $root 'backend\src\DataControls.Api'
$aiDir = Join-Path $root 'ai-service'
$uiDir = Join-Path $root 'frontend'
$venvPython = Join-Path $aiDir '.venv\Scripts\python.exe'

function Test-Prerequisites {
    $problems = @()

    foreach ($tool in 'dotnet', 'node', 'npm', 'sqlcmd') {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            $problems += "'$tool' was not found on PATH."
        }
    }

    if (Get-Command sqlcmd -ErrorAction SilentlyContinue) {
        # sqlcmd writes connection errors to stderr; in Windows PowerShell that must not abort the script.
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $dbId = sqlcmd -S $SqlServer -E -b -l 5 -h -1 -W -Q "SET NOCOUNT ON; SELECT ISNULL(DB_ID('DataControlsDb'), 0)" 2>$null
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = $previous

        if ($exitCode -ne 0) {
            $problems += "Cannot connect to SQL Server '$SqlServer'."
        }
        elseif ("$dbId".Trim() -eq '0') {
            $problems += "Database 'DataControlsDb' does not exist. Run: .\database\setup.ps1"
        }
    }

    if (-not (Test-Path $venvPython)) {
        $problems += "Python virtual environment is missing. See README: 'ai-service' setup."
    }

    if (-not (Test-Path (Join-Path $uiDir 'node_modules'))) {
        $problems += "Frontend packages are missing. Run: cd frontend; npm install"
    }

    return $problems
}

function Start-InWindow([string]$title, [string]$workDir, [string]$command) {
    $script = "`$Host.UI.RawUI.WindowTitle = '$title'; Set-Location '$workDir'; $command"
    Start-Process powershell -ArgumentList '-NoExit', '-NoProfile', '-Command', $script | Out-Null
}

$problems = Test-Prerequisites
if ($problems.Count -gt 0) {
    Write-Host 'Cannot start - fix the following first (see README.md):' -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    exit 1
}

Write-Host 'All prerequisites OK.' -ForegroundColor Green
if ($CheckOnly) { exit 0 }

Start-InWindow 'DataControls API (5080)' $apiDir 'dotnet run --launch-profile http'
Start-InWindow 'DataControls AI service (8001)' $aiDir "& '$venvPython' -m uvicorn app.main:app --port 8001"
Start-InWindow 'DataControls UI (4200)' $uiDir 'npx ng serve --port 4200'

Write-Host ''
Write-Host 'Starting... (first start may take a minute)' -ForegroundColor Cyan
Write-Host '  UI          http://localhost:4200'
Write-Host '  API         http://localhost:5080   (OpenAPI: /openapi/v1.json)'
Write-Host '  AI service  http://localhost:8001   (docs: /docs)'
Write-Host 'Close the three windows to stop.'
