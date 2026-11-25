$ErrorActionPreference = 'Stop'
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","User") + ";" + [System.Environment]::GetEnvironmentVariable("Path","Machine")
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "=========================================================="
Write-Host " API Governance Platform - Automated Quality Gate (F06)"
Write-Host "=========================================================="

Push-Location (Join-Path $repoRoot "frontend")
try {
  Write-Host ""
  Write-Host "[1/6] Running Frontend ESLint (npm run lint)..."
  npm run lint
  if ($LASTEXITCODE -ne 0) { throw "Frontend ESLint failed with exit code $LASTEXITCODE" }

  Write-Host ""
  Write-Host "[2/6] Running Frontend TypeScript Check (npm run typecheck)..."
  npm run typecheck
  if ($LASTEXITCODE -ne 0) { throw "Frontend TypeCheck failed with exit code $LASTEXITCODE" }

  Write-Host ""
  Write-Host "[3/6] Running Frontend Automated Unit/UI Tests (npm test)..."
  npm test
  if ($LASTEXITCODE -ne 0) { throw "Frontend Automated Tests failed with exit code $LASTEXITCODE" }

  Write-Host ""
  Write-Host "[4/6] Running Frontend Production Build (npm run build)..."
  npm run build
  if ($LASTEXITCODE -ne 0) { throw "Frontend Build failed with exit code $LASTEXITCODE" }
} finally {
  Pop-Location
}

Push-Location (Join-Path $repoRoot "backend")
try {
  Write-Host ""
  Write-Host "[5/6] Running Backend Solution Build (dotnet build -c Release --warnaserror)..."
  dotnet build ApiGovernance.sln -c Release --no-restore --nologo --warnaserror
  if ($LASTEXITCODE -ne 0) { throw "Backend Solution Build failed with exit code $LASTEXITCODE" }

  Write-Host ""
  Write-Host "[6/6] Running Backend Automated Unit and Integration Tests (dotnet test -c Release)..."
  dotnet test ApiGovernance.sln -c Release --no-build --nologo --logger "console;verbosity=normal"
  if ($LASTEXITCODE -ne 0) { throw "Backend Automated Tests failed with exit code $LASTEXITCODE" }
} finally {
  Pop-Location
}

Write-Host ""
Write-Host "=========================================================="
Write-Host " ALL QUALITY GATE CHECKS (LINT + BUILD + TESTS) PASSED!"
Write-Host "=========================================================="
