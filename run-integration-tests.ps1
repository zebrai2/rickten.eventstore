# Integration Test Helper Script
# All tests now use Testcontainers - just make sure Docker is running!

param(
    [Parameter(HelpMessage = "Test target: all, sqlite, sqlserver, postgres")]
    [ValidateSet("all", "sqlite", "sqlserver", "postgres")]
    [string]$Target = "all"
)

$ErrorActionPreference = "Stop"

function Write-Header {
    param([string]$Message)
    Write-Host "`n============================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "============================================`n" -ForegroundColor Cyan
}

function Test-DockerRunning {
    try {
        docker info | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

Write-Header "Running Integration Tests: $Target"

# Check Docker status for SQL Server and PostgreSQL tests
if ($Target -eq "all" -or $Target -eq "sqlserver" -or $Target -eq "postgres") {
    if (Test-DockerRunning) {
        Write-Host "✅ Docker is running" -ForegroundColor Green
    } else {
        Write-Host "⚠️  Docker is not running" -ForegroundColor Yellow
        Write-Host "   SQL Server and PostgreSQL tests will be skipped" -ForegroundColor Yellow
        Write-Host "   SQLite tests will still run" -ForegroundColor Gray
        Write-Host "`n💡 Start Docker Desktop to run all tests" -ForegroundColor Cyan
    }
}

# Build filter based on target
$filter = switch ($Target) {
    "sqlite" { "FullyQualifiedName~Sqlite" }
    "sqlserver" { "FullyQualifiedName~SqlServer" }
    "postgres" { "FullyQualifiedName~Postgres" }
    default { "" }
}

Write-Host "`n📋 Test configuration:" -ForegroundColor Cyan
Write-Host "   SQLite: ✅ Always available (no Docker needed)" -ForegroundColor Green
Write-Host "   SQL Server: 🐳 Testcontainers (auto-managed)" -ForegroundColor Cyan
Write-Host "   PostgreSQL: 🐳 Testcontainers (auto-managed)" -ForegroundColor Cyan

# Run tests
Write-Host "`n🚀 Running tests..." -ForegroundColor Cyan

$testArgs = @("test", "Rickten.EventStore.Tests")
if ($filter) {
    $testArgs += @("--filter", $filter)
}

& dotnet @testArgs

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ All tests passed!" -ForegroundColor Green
} else {
    Write-Host "`n❌ Some tests failed" -ForegroundColor Red
    exit $LASTEXITCODE
}
