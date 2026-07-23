# ═══════════════════════════════════════════════════════════════
# unrealestate.au / Aigents — LOCAL DEVELOPMENT SETUP
# ═══════════════════════════════════════════════════════════════
# Run: .\scripts\setup-local.ps1
# Truth: docs/HANDOFF.md §10. Compose = Postgres 16 + Redis + MinIO (+ MailDev).
# ═══════════════════════════════════════════════════════════════

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "unrealestate.au — local infra setup" -ForegroundColor Blue
Write-Host ""

Write-Host "Checking prerequisites..." -ForegroundColor Cyan

try {
    $dockerVersion = docker --version
    Write-Host "OK Docker: $dockerVersion" -ForegroundColor Green
} catch {
    Write-Host "Docker not found. Install Docker Desktop." -ForegroundColor Red
    exit 1
}

try {
    $dotnetVersion = dotnet --version
    Write-Host "OK .NET SDK: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host ".NET SDK not found." -ForegroundColor Red
    exit 1
}

$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
Write-Host "OK Architecture: $arch" -ForegroundColor Green

Write-Host ""
Write-Host "Starting Docker containers (Postgres / Redis / MinIO / MailDev)..." -ForegroundColor Cyan
docker compose up -d

Write-Host "Waiting for Postgres health..." -ForegroundColor Yellow
$ready = $false
for ($i = 1; $i -le 30; $i++) {
    try {
        docker exec unrealestate-postgres pg_isready -U unrealestate -d unrealestate 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "OK Postgres is ready" -ForegroundColor Green
            $ready = $true
            break
        }
    } catch {}
    Write-Host "." -NoNewline
    Start-Sleep -Seconds 2
}

if (-not $ready) {
    Write-Host ""
    Write-Host "Postgres may still be starting. Check: docker logs unrealestate-postgres" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Building solution..." -ForegroundColor Cyan
dotnet build Aigents.sln
Write-Host "OK Build finished" -ForegroundColor Green

Write-Host ""
Write-Host "Services (see docker compose ps):" -ForegroundColor Cyan
Write-Host "  Postgres  localhost:5432  (db/user unrealestate)"
Write-Host "  Redis     localhost:6379"
Write-Host "  MailDev   http://localhost:1080"
Write-Host ""
Write-Host "Next — set AppHost user-secrets (values off-GitHub; see docs/HANDOFF.md §10):" -ForegroundColor Yellow
Write-Host '  dotnet user-secrets set "Parameters:azure-ai-endpoint"   "https://...." --project src\Aigents.AppHost'
Write-Host '  dotnet user-secrets set "Parameters:azure-ai-deployment" "gpt-4.1"     --project src\Aigents.AppHost'
Write-Host '  dotnet user-secrets set "Parameters:jwt-secret"         "<32+ chars>" --project src\Aigents.AppHost'
Write-Host '  dotnet user-secrets set "Parameters:google-maps-api-key" "<restricted browser key>" --project src\Aigents.AppHost'
Write-Host '  # optional for mail: smtp-host / smtp-username / smtp-password'
Write-Host ""
Write-Host "Then:  dotnet run --project src\Aigents.AppHost"
Write-Host "Stop:  docker compose down"
Write-Host ""
