# Clean and Rebuild Script
# Run this from PowerShell in the solution root (E:\Rickten\Rickten\)

Write-Host "🧹 Cleaning solution..." -ForegroundColor Yellow
dotnet clean

Write-Host "`n📦 Restoring packages..." -ForegroundColor Yellow
dotnet restore

Write-Host "`n🔨 Building solution..." -ForegroundColor Yellow
dotnet build

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ Build succeeded!" -ForegroundColor Green
    Write-Host "`nYou can now run tests:" -ForegroundColor Cyan
    Write-Host "  dotnet test --filter EventStoreDbContextTests" -ForegroundColor White
} else {
    Write-Host "`n❌ Build failed! See errors above." -ForegroundColor Red
    Write-Host "`nCommon fixes:" -ForegroundColor Yellow
    Write-Host "  1. Close Visual Studio" -ForegroundColor White
    Write-Host "  2. Delete all bin/obj folders" -ForegroundColor White
    Write-Host "  3. Run this script again" -ForegroundColor White
    Write-Host "  4. Reopen Visual Studio" -ForegroundColor White
}
