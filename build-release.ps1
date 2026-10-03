# build-release.ps1  (win-x64 only)
param([switch]$SystemVlcOnly)   # don't bundle LibVLC; rely on the user's installed VLC (much smaller)
$ErrorActionPreference = "Stop"

# Auto-detect repository root whether run from root or from scripts/
if (Test-Path (Join-Path $PSScriptRoot "NullWave.csproj")) {
    Set-Location $PSScriptRoot
} elseif (Test-Path (Join-Path $PSScriptRoot "..\NullWave.csproj")) {
    Set-Location (Join-Path $PSScriptRoot "..")
}

Write-Host "========== NullWave Release Builder ==========" -ForegroundColor Cyan

# 1. Version from NullWave.csproj
$csproj = Get-Content "NullWave.csproj" -Raw
if ($csproj -match '<Version>(.*?)</Version>') { $version = $matches[1] }
else { $version = Read-Host "Enter version manually (e.g., is 0.6.3)" }
Write-Host "Version: v$version" -ForegroundColor Green

$publishDir = "./publish-artifacts"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir | Out-Null

$releasesDir = "./releases"
if (-not (Test-Path $releasesDir)) { New-Item -ItemType Directory -Path $releasesDir | Out-Null }

function Get-DirSizeMB($p) { 
    [math]::Round((Get-ChildItem $p -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1) 
}

function Trim-Publish($dir) {
    $vlc = Join-Path $dir "libvlc"
    if ($SystemVlcOnly) {
        if (Test-Path $vlc) { Remove-Item $vlc -Recurse -Force; Write-Host "   Removed bundled LibVLC" -ForegroundColor DarkGray }
    } elseif (Test-Path $vlc) {
        Get-ChildItem $vlc -Directory | Where-Object Name -ne "win-x64" | ForEach-Object {
            Write-Host "   Removing LibVLC $($_.Name)" -ForegroundColor DarkGray
            Remove-Item $_.FullName -Recurse -Force
        }
    }
    
    $rt = Join-Path $dir "runtimes"
    if (Test-Path $rt) {
        Get-ChildItem $rt -Directory | Where-Object { $_.Name -notin "win","win-x64" } | Remove-Item -Recurse -Force
    }
    
    if (-not $SystemVlcOnly) {
        foreach ($f in "libvlc.dll","libvlccore.dll","plugins") {
            if (-not (Test-Path (Join-Path $vlc "win-x64/$f"))) {
                Write-Error "LibVLC '$f' missing in $vlc/win-x64 - the app would crash on startup."
            }
        }
    }
    Write-Host "   $dir is now $(Get-DirSizeMB $dir) MB" -ForegroundColor DarkYellow
}

function Publish-Build($outDir, $selfContained) {
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    dotnet publish -c Release -r win-x64 --self-contained $selfContained `
        -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $outDir -v q
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed for $outDir." }
    Trim-Publish $outDir
}

function Pack-Channel($dir, $channel, $suffix, $extra) {
    vpk pack --packId "NullWave" --packTitle "NullWave" --packVersion $version `
        --packDir $dir --mainExe "NullWave.exe" --outputDir "./releases" `
        --channel $channel --runtime win-x64 @extra
    if ($LASTEXITCODE -ne 0) { Write-Error "vpk pack ($channel) failed." }

    $setup = Get-ChildItem "./releases" -Filter "*-$channel-Setup.exe" | Select-Object -First 1
    $zip   = Get-ChildItem "./releases" -Filter "*-$channel-Portable.zip" | Select-Object -First 1
    
    if (-not $setup) { Write-Error "No Setup.exe found for channel '$channel'." }
    Copy-Item $setup.FullName "$publishDir/NullWave-$version$suffix-Setup.exe"

    if ($zip) {
        if (-not $SystemVlcOnly) {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $z = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
            $ok = $z.Entries | Where-Object { $_.FullName -match 'libvlc[\\/]win-x64[\\/]libvlc\.dll$' }
            $z.Dispose()
            if (-not $ok) { Write-Error "Portable zip ($channel) has no libvlc/win-x64/libvlc.dll." }
        }
        Copy-Item $zip.FullName "$publishDir/NullWave-$version$suffix-Portable.zip"
    }

    # CRITICAL FIX: Copy Velopack update feed files to publish-artifacts so they get uploaded to GitHub
    foreach ($p in "releases.$channel.json", "RELEASES-$channel", "assets.$channel.json",
                   "*-$version-$channel-full.nupkg", "*-$version-$channel-delta.nupkg") {
        Get-ChildItem "./releases" -Filter $p -ErrorAction SilentlyContinue | Copy-Item -Destination $publishDir
    }
}

# PASS 1: self-contained (bundles .NET)
Write-Host "`n[1/4] Publishing self-contained build..." -ForegroundColor Yellow
dotnet clean -c Release -v q
Publish-Build "./publish/full" "true"
Write-Host "`n[2/4] Packing channel 'full'..." -ForegroundColor Yellow
Pack-Channel "./publish/full" "full" "" @()

# PASS 2: framework-dependent (installer fetches .NET 8 if missing)
Write-Host "`n[3/4] Publishing framework-dependent build..." -ForegroundColor Yellow
Publish-Build "./publish/fx" "false"
Write-Host "`n[4/4] Packing channel 'fx'..." -ForegroundColor Yellow
Pack-Channel "./publish/fx" "fx" "-Framework" @("--framework", "net8.0-x64-desktop")

Write-Host "`n[OK] Build complete. Upload ALL contents of '.\publish-artifacts' to GitHub Releases." -ForegroundColor Green
Get-ChildItem $publishDir | Select-Object Name, @{N="Size(MB)";E={[math]::Round($_.Length/1MB,2)}} | Format-Table -AutoSize