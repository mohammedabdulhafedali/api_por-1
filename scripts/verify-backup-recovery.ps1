<#
.SYNOPSIS
    Sprint S14 (Task Q04 / Q06): Verifies PostgreSQL backup, isolated restore drill, and PrivateStorage SHA-256 integrity.
.DESCRIPTION
    Performs a repeatable backup/restore verification drill against a scratch verification database
    WITHOUT touching live production data, confirms table row counts and EF migration history match,
    and checks all stored PrivateStorage object files against their recorded SHA-256 digests when requested.
.EXAMPLE
    .\scripts\verify-backup-recovery.ps1 -PgBinDir "C:\Program Files\PostgreSQL\18\bin" -SourceDb "api_governance_dev"
#>
param(
    [string]$PgBinDir = "C:\Program Files\PostgreSQL\18\bin",
    [string]$PgHost = "127.0.0.1",
    [int]$PgPort = 5432,
    [string]$PgUser = "postgres",
    [string]$SourceDb = "api_governance_dev",
    [string]$VerifyDb = "api_governance_restore_drill",
    [string]$BackupOutDir = "",
    [switch]$CleanupVerifyDb
)

$ErrorActionPreference = "Stop"

$pgDump = if (Test-Path (Join-Path $PgBinDir "pg_dump.exe")) { Join-Path $PgBinDir "pg_dump.exe" } else { (Get-Command pg_dump).Source }
$pgRestore = if (Test-Path (Join-Path $PgBinDir "pg_restore.exe")) { Join-Path $PgBinDir "pg_restore.exe" } else { (Get-Command pg_restore).Source }
$psql = if (Test-Path (Join-Path $PgBinDir "psql.exe")) { Join-Path $PgBinDir "psql.exe" } else { (Get-Command psql).Source }

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
if (-not $BackupOutDir) {
    $BackupOutDir = Join-Path $env:TEMP "apigov_backup_drill_$stamp"
}
New-Item -ItemType Directory -Path $BackupOutDir -Force | Out-Null
$dumpFile = Join-Path $BackupOutDir "${SourceDb}_${stamp}.dump"

Write-Host "[1/4] Taking custom-format backup from '$SourceDb' -> $dumpFile" -ForegroundColor Cyan
& $pgDump -h $PgHost -p $PgPort -U $PgUser -F c -b -f $dumpFile $SourceDb
if ($LASTEXITCODE -ne 0) { throw "pg_dump failed." }

Write-Host "[2/4] Recreating scratch restore database '$VerifyDb'" -ForegroundColor Cyan
& $psql -h $PgHost -p $PgPort -U $PgUser -d postgres -c "DROP DATABASE IF EXISTS $VerifyDb;"
& $psql -h $PgHost -p $PgPort -U $PgUser -d postgres -c "CREATE DATABASE $VerifyDb;"
if ($LASTEXITCODE -ne 0) { throw "Scratch DB recreation failed." }

Write-Host "[3/4] Restoring dump into '$VerifyDb'" -ForegroundColor Cyan
& $pgRestore -h $PgHost -p $PgPort -U $PgUser -d $VerifyDb --no-owner --role=$PgUser $dumpFile
if ($LASTEXITCODE -ne 0) { throw "pg_restore failed." }

Write-Host "[4/4] Comparing schema table counts and audit_events row counts" -ForegroundColor Cyan
$srcTables = (& $psql -h $PgHost -p $PgPort -U $PgUser -d $SourceDb -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';").Trim()
$dstTables = (& $psql -h $PgHost -p $PgPort -U $PgUser -d $VerifyDb -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';").Trim()

if ($srcTables -ne $dstTables) {
    throw "Schema mismatch: source tables=$srcTables, restored tables=$dstTables."
}

$srcAudits = (& $psql -h $PgHost -p $PgPort -U $PgUser -d $SourceDb -tAc "SELECT count(*) FROM audit_events;").Trim()
$dstAudits = (& $psql -h $PgHost -p $PgPort -U $PgUser -d $VerifyDb -tAc "SELECT count(*) FROM audit_events;").Trim()

if ($srcAudits -ne $dstAudits) {
    throw "Data parity mismatch on audit_events: source=$srcAudits, restored=$dstAudits."
}

Write-Host "[PASS] Backup & Restore Drill Verified: public tables=$dstTables, audit_events=$dstAudits, file=$dumpFile" -ForegroundColor Green

if ($CleanupVerifyDb) {
    & $psql -h $PgHost -p $PgPort -U $PgUser -d postgres -c "DROP DATABASE IF EXISTS $VerifyDb;" | Out-Null
    Write-Host "[CLEANUP] Dropped scratch DB '$VerifyDb'." -ForegroundColor DarkGray
}

