Write-Host "Checking for remaining IClassicDesktopStyleApplicationLifetime usage..." -ForegroundColor Cyan

$files = Get-ChildItem -Path . -Include *.cs -Recurse -File | 
    Where-Object { $_.FullName -notmatch '\\(bin|obj|tests)\\' }

$count = 0
foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    if ($content -match 'IClassicDesktopStyleApplicationLifetime') {
        Write-Host "  Found in: $($file.FullName)" -ForegroundColor Yellow
        $count++
    }
}

if ($count -eq 0) {
    Write-Host "✓ All window lookup boilerplate eliminated!" -ForegroundColor Green
} else {
    Write-Host "⚠ $count files still contain window lookup code" -ForegroundColor Yellow
}

Write-Host "`nChecking DialogService usage..." -ForegroundColor Cyan
$dialogCount = ($files | Select-String -Pattern "DialogService\.Instance" | Measure-Object).Count
Write-Host "  DialogService.Instance called $dialogCount times" -ForegroundColor Green
