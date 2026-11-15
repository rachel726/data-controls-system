<#
.SYNOPSIS
    Creates (or refreshes) the DataControlsDb database: schemas, security, catalog,
    sample data and sample controls - then runs the verification script.

.DESCRIPTION
    Runs every scripts\NN_*.sql in numeric order, then 99_verify.sql.
    Without -Force: idempotent refresh - sample business data and the catalog are
    re-seeded; saved controls are kept.
    With -Force: the database is dropped and created from scratch.

.PARAMETER Server
    SQL Server instance (default: .\SQLEXPRESS). Windows authentication is used.

.PARAMETER Force
    Drop and recreate the database. All saved controls are lost.

.EXAMPLE
    .\database\setup.ps1
    .\database\setup.ps1 -Force
    .\database\setup.ps1 -Server "MYPC\SQL2022" -Force
#>
[CmdletBinding()]
param(
    [string]$Server = '.\SQLEXPRESS',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$scriptsDir = Join-Path $PSScriptRoot 'scripts'
$database = 'DataControlsDb'

# sqlcmd flags: -E Windows auth, -b fail on error, -I QUOTED_IDENTIFIER ON (filtered indexes), -f 65001 UTF-8 (Hebrew)
function Invoke-Sql {
    param([string]$File, [string]$Query)

    $sqlArgs = @('-S', $Server, '-E', '-b', '-I', '-f', '65001', '-l', '15')
    if ($File) { $sqlArgs += @('-i', $File) } else { $sqlArgs += @('-Q', $Query) }

    # sqlcmd reports errors on stderr; in Windows PowerShell that must not abort before we print a clear message.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & sqlcmd @sqlArgs 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $ErrorActionPreference = $previous

    return [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Stop-Setup([string]$message, $output) {
    Write-Host ''
    Write-Host "SETUP FAILED: $message" -ForegroundColor Red
    if ($output) { $output | Select-Object -Last 25 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray } }
    exit 1
}

# ---------- prerequisites ----------
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Stop-Setup "'sqlcmd' was not found. Install the SQL Server command-line tools (see README)."
}

$probe = Invoke-Sql -Query "SET NOCOUNT ON; SELECT ISNULL(DB_ID('$database'), 0);"
if ($probe.ExitCode -ne 0) {
    Stop-Setup "cannot connect to SQL Server '$Server' with Windows authentication." $probe.Output
}
$exists = (($probe.Output | Where-Object { $_ -match '^\s*\d+\s*$' } | Select-Object -Last 1) -as [string]).Trim() -ne '0'

Write-Host "SQL Server: $Server"
if ($exists -and $Force) {
    Write-Host "Dropping existing database $database (-Force)..." -ForegroundColor Yellow
    $drop = Invoke-Sql -Query "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database];"
    if ($drop.ExitCode -ne 0) { Stop-Setup "could not drop $database." $drop.Output }
}
elseif ($exists) {
    Write-Host "Database $database exists - refreshing (saved controls are kept; use -Force to rebuild from scratch)."
}

# ---------- run scripts in order ----------
$scripts = Get-ChildItem $scriptsDir -Filter '*.sql' |
    Where-Object { $_.Name -match '^\d{2}_' -and $_.Name -notmatch '^99_' } |
    Sort-Object Name

foreach ($script in $scripts) {
    Write-Host ("  {0,-40}" -f $script.Name) -NoNewline
    $result = Invoke-Sql -File $script.FullName
    if ($result.ExitCode -ne 0) {
        Write-Host 'FAILED' -ForegroundColor Red
        Stop-Setup "$($script.Name) failed." $result.Output
    }
    Write-Host 'ok' -ForegroundColor Green
}

# ---------- verify ----------
Write-Host ("  {0,-40}" -f '99_verify.sql') -NoNewline
$verify = Invoke-Sql -File (Join-Path $scriptsDir '99_verify.sql')
$failedChecks = $verify.Output | Where-Object { $_ -match '^FAIL' }
if ($verify.ExitCode -ne 0 -or $failedChecks) {
    Write-Host 'FAILED' -ForegroundColor Red
    Stop-Setup 'verification failed.' ($failedChecks + ($verify.Output | Where-Object { $_ -match 'VERIFY|Msg' }))
}
Write-Host 'ok' -ForegroundColor Green
Write-Host ''
Write-Host ($verify.Output | Where-Object { $_ -match 'VERIFY OK' }) -ForegroundColor Green
Write-Host "Database $database is ready."
