<#
.SYNOPSIS
    Prepares a clean, secret-free local development environment for API_Governance_F01 on a new workstation.
.DESCRIPTION
    1. Verifies Git, .NET 10 SDK, Node.js/npm, PostgreSQL 18 CLI tools, and optional ClamAV/AV scanner config.
    2. Creates least-privilege PostgreSQL roles (api_gov_app, api_gov_migrator) and databases
       (api_governance_dev, api_governance_test) bound locally.
    3. Configures fresh random credentials and .NET User Secrets outside Git.
    4. Applies EF Core migrations cleanly (never embeds real DB backups or user password hashes).
    5. Optionally applies non-sensitive synthetic demo master data (-SeedDemoData via scripts/seed-clean-dev-data.sql).
    6. Securely initializes a brand-new local administrator account via CLI `bootstrap-admin` (never reusing old credentials).
    7. Creates PrivateStorage (objects/quarantine/staging) and DataProtectionKeys folders outside web directories.
    8. Restores npm/NuGet dependencies and verifies quality gate checks.
.EXAMPLE
    .\scripts\setup-dev-environment.ps1 -SeedDemoData -BootstrapAdminEmail "admin@company.local" -BootstrapAdminName "System Administrator"
#>
param(
    [string]$PgBinDir = "",
    [string]$PgHost = "127.0.0.1",
    [int]$PgPort = 5432,
    [string]$PgSuperUser = "postgres",
    [switch]$SeedDemoData,
    [string]$BootstrapAdminEmail = "",
    [string]$BootstrapAdminName = "System Administrator",
    [string]$ClamAvHost = "",
    [int]$ClamAvPort = 3310,
    [switch]$SkipQualityGate
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

function Find-ToolPath([string]$Name, [string[]]$CandidateDirs = @()) {
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($dir in $CandidateDirs) {
        if ($dir) {
            $full = Join-Path $dir "$Name.exe"
            if (Test-Path $full) { return $full }
        }
    }
    throw "Required tool '$Name' was not found. Install it and ensure it is in PATH (or pass -PgBinDir for PostgreSQL tools)."
}

function ConvertFrom-SecurePlain([System.Security.SecureString]$Secure) {
    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr) }
    finally { [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function New-RandomStrongPassword {
    $chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%^&*"
    $bytes = New-Object byte[] 28
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $sb = New-Object System.Text.StringBuilder
    foreach ($b in $bytes) { [void]$sb.Append($chars[$b % $chars.Length]) }
    return $sb.ToString()
}

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host " API Governance Platform - New Workstation Setup (Sprint S08 Base)" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan

# 1. Verify Toolchain
$null = Find-ToolPath "git"
$dotnetExe = Find-ToolPath "dotnet"
$npmCmd = Find-ToolPath "npm"
$pgCandidates = @($PgBinDir, "$env:LOCALAPPDATA\Programs\PostgreSQL\18\bin", "C:\Program Files\PostgreSQL\18\bin", "C:\Program Files\PostgreSQL\17\bin")
$psqlExe = Find-ToolPath "psql" $pgCandidates
Write-Host "[OK] Found git, dotnet, npm, psql."

# 2. Provision fresh local PostgreSQL roles & clean databases (no hardcoded credentials)
$pgSecPwd = Read-Host -Prompt "Enter local PostgreSQL '$PgSuperUser' password for initial setup" -AsSecureString
$pgPlainPwd = ConvertFrom-SecurePlain $pgSecPwd
$appPwd = New-RandomStrongPassword
$migratorPwd = New-RandomStrongPassword

$env:PGPASSWORD = $pgPlainPwd
try {
    $testConn = [string](& $psqlExe -h $PgHost -p $PgPort -U $PgSuperUser -d postgres -tAc "SELECT 1;")
    if ($LASTEXITCODE -ne 0 -or $testConn.Trim() -ne "1") {
        throw "Unable to connect to PostgreSQL at ${PgHost}:${PgPort} as '$PgSuperUser'."
    }

    $sqlSetup = @"
DO \$\$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'api_gov_migrator') THEN
        CREATE ROLE api_gov_migrator WITH LOGIN PASSWORD '$migratorPwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT CONNECTION LIMIT 20;
    ELSE
        ALTER ROLE api_gov_migrator WITH LOGIN PASSWORD '$migratorPwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT CONNECTION LIMIT 20;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'api_gov_app') THEN
        CREATE ROLE api_gov_app WITH LOGIN PASSWORD '$appPwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT CONNECTION LIMIT 50;
    ELSE
        ALTER ROLE api_gov_app WITH LOGIN PASSWORD '$appPwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT CONNECTION LIMIT 50;
    END IF;
END \$\$;
"@
    $sqlSetup | & $psqlExe -h $PgHost -p $PgPort -U $PgSuperUser -d postgres -v ON_ERROR_STOP=1 | Out-Null

    foreach ($dbName in @("api_governance_dev", "api_governance_test")) {
        $exists = [string](& $psqlExe -h $PgHost -p $PgPort -U $PgSuperUser -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = '$dbName';")
        if ($exists.Trim() -ne "1") {
            & $psqlExe -h $PgHost -p $PgPort -U $PgSuperUser -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE $dbName OWNER api_gov_migrator ENCODING 'UTF8';" | Out-Null
        }
        $permSql = @"
REVOKE ALL ON DATABASE $dbName FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE $dbName TO api_gov_migrator;
GRANT CONNECT, TEMPORARY ON DATABASE $dbName TO api_gov_app;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO api_gov_migrator;
GRANT USAGE ON SCHEMA public TO api_gov_app;
ALTER DEFAULT PRIVILEGES FOR ROLE api_gov_migrator IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO api_gov_app;
ALTER DEFAULT PRIVILEGES FOR ROLE api_gov_migrator IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO api_gov_app;
"@
        $permSql | & $psqlExe -h $PgHost -p $PgPort -U $PgSuperUser -d $dbName -v ON_ERROR_STOP=1 | Out-Null
    }
    Write-Host "[OK] Provisioned least-privilege roles (api_gov_app, api_gov_migrator) and databases."
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}

# 3. Store fresh secrets strictly outside Git in .NET User Secrets
$hostProj = Join-Path $RepoRoot "backend\src\ApiGovernance.Host\ApiGovernance.Host.csproj"
$sharedProj = Join-Path $RepoRoot "backend\src\Shared\ApiGovernance.Shared\ApiGovernance.Shared.csproj"

$devAppConn = "Host=$PgHost;Port=$PgPort;Database=api_governance_dev;Username=api_gov_app;Password=$appPwd"
$devMigratorConn = "Host=$PgHost;Port=$PgPort;Database=api_governance_dev;Username=api_gov_migrator;Password=$migratorPwd"
$testAppConn = "Host=$PgHost;Port=$PgPort;Database=api_governance_test;Username=api_gov_app;Password=$appPwd"
$testMigratorConn = "Host=$PgHost;Port=$PgPort;Database=api_governance_test;Username=api_gov_migrator;Password=$migratorPwd"

& $dotnetExe user-secrets set "ConnectionStrings:DefaultConnection" $devAppConn --project $hostProj | Out-Null
& $dotnetExe user-secrets set "ConnectionStrings:MigrationsConnection" $devMigratorConn --project $hostProj | Out-Null
& $dotnetExe user-secrets set "ConnectionStrings:TestConnection" $testAppConn --project $hostProj | Out-Null
& $dotnetExe user-secrets set "ConnectionStrings:TestMigrationsConnection" $testMigratorConn --project $hostProj | Out-Null

# 4. Configure isolated PrivateStorage, DataProtectionKeys, and optional local ClamAV settings
$localRoot = Join-Path $env:LOCALAPPDATA "ApiGovernance"
$privateStorage = Join-Path $localRoot "PrivateStorage"
$dpDir = Join-Path $localRoot "DataProtectionKeys"
New-Item -ItemType Directory -Force -Path (Join-Path $privateStorage "objects") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $privateStorage "quarantine") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $privateStorage "staging") | Out-Null
New-Item -ItemType Directory -Force -Path $dpDir | Out-Null

& $dotnetExe user-secrets set "FileStorage:RootPath" $privateStorage --project $hostProj | Out-Null
& $dotnetExe user-secrets set "AuthenticationPolicy:DataProtectionKeyDirectory" $dpDir --project $hostProj | Out-Null
if (-not [string]::IsNullOrWhiteSpace($ClamAvHost)) {
    & $dotnetExe user-secrets set "FileScanning:Provider" "ClamAv" --project $hostProj | Out-Null
    & $dotnetExe user-secrets set "FileScanning:ClamAvHost" $ClamAvHost --project $hostProj | Out-Null
    & $dotnetExe user-secrets set "FileScanning:ClamAvPort" $ClamAvPort.ToString() --project $hostProj | Out-Null
    Write-Host "[OK] Configured ClamAV scanner endpoint (${ClamAvHost}:${ClamAvPort})."
} else {
    Write-Host "[INFO] FileScanning:ClamAvHost left empty — Fail-Closed PENDING_SCAN quarantine remains active until IT configures ClamAV/AV." -ForegroundColor Yellow
}

# 5. Restore packages & apply EF Core Migrations cleanly
Push-Location (Join-Path $RepoRoot "backend")
try {
    & $dotnetExe restore ApiGovernance.sln
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

    $env:API_GOVERNANCE_MIGRATIONS_DB_CONNECTION = $devMigratorConn
    try {
        & $dotnetExe ef database update --project $sharedProj --startup-project $hostProj
        if ($LASTEXITCODE -ne 0) { throw "EF Core migrations on api_governance_dev failed." }
    }
    finally {
        Remove-Item Env:API_GOVERNANCE_MIGRATIONS_DB_CONNECTION -ErrorAction SilentlyContinue
    }
    Write-Host "[OK] Applied EF Core migrations to api_governance_dev cleanly."
}
finally {
    Pop-Location
}

# 6. Optionally load synthetic, secret-free demo master data (zero users/passwords/sessions/audit)
if ($SeedDemoData) {
    $seedFile = Join-Path $RepoRoot "scripts\seed-clean-dev-data.sql"
    $env:PGPASSWORD = $appPwd
    try {
        & $psqlExe -h $PgHost -p $PgPort -U api_gov_app -d api_governance_dev -v ON_ERROR_STOP=1 -f $seedFile | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Clean synthetic demo seed script failed." }
        Write-Host "[OK] Loaded clean synthetic master data into api_governance_dev."
    }
    finally {
        Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    }
}

# 7. Securely bootstrap a brand-new local administrator account if requested
if (-not [string]::IsNullOrWhiteSpace($BootstrapAdminEmail)) {
    $admSec = Read-Host -Prompt "Enter initial password for '$BootstrapAdminEmail' (minimum 15 chars, mixed case + digit + symbol)" -AsSecureString
    $admPlain = ConvertFrom-SecurePlain $admSec
    $env:APIGOV_TEMP_ADMIN_PWD = $admPlain
    try {
        Push-Location (Join-Path $RepoRoot "backend")
        try {
            & $dotnetExe run --project $hostProj -- bootstrap-admin --email $BootstrapAdminEmail --full-name $BootstrapAdminName --password-env APIGOV_TEMP_ADMIN_PWD
            if ($LASTEXITCODE -ne 0) { throw "bootstrap-admin failed." }
        }
        finally {
            Pop-Location
        }
    }
    finally {
        Remove-Item Env:APIGOV_TEMP_ADMIN_PWD -ErrorAction SilentlyContinue
    }
}

Push-Location (Join-Path $RepoRoot "frontend")
try {
    & $npmCmd ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed." }
}
finally {
    Pop-Location
}

if (-not $SkipQualityGate) {
    $gateScript = Join-Path $RepoRoot "tests\run-quality-gate.ps1"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $gateScript
    if ($LASTEXITCODE -ne 0) { throw "Quality gate failed with exit code $LASTEXITCODE." }
}

Write-Host ""
Write-Host "Workstation setup completed safely (0 secrets stored in Git)!" -ForegroundColor Green
Write-Host "  Backend : cd backend && dotnet run --project src\ApiGovernance.Host\ApiGovernance.Host.csproj --launch-profile http"
Write-Host "  Frontend: cd frontend && npm run dev"
if ([string]::IsNullOrWhiteSpace($BootstrapAdminEmail)) {
    Write-Host '  Admin   : Run bootstrap-admin --email <email> --full-name "<name>" --password-env <ENV_VAR> in backend/ before first login.'
}
