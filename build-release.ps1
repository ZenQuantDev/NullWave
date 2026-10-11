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
if (Test-Path $releasesDir) { Remove-Item $releasesDir -Recurse -Force }
New-Item -ItemType Directory -Path $releasesDir | Out-Null

function Get-DirSizeMB($p) { 
    [math]::Round((Get-ChildItem $p -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1) 
}

function Prune-VlcPlugins {
    param([string]$WinX64)
    if ($env:NULLWAVE_VLC_PRUNE -eq "off") {
        Write-Host "   VLC plugin pruning DISABLED (NULLWAVE_VLC_PRUNE=off)" -ForegroundColor DarkGray
        return
    }
    $plugins = Join-Path $WinX64 "plugins"
    if (-not (Test-Path $plugins)) { return }

    $beforeMb   = Get-DirSizeMB $WinX64
    $beforeDlls = (Get-ChildItem $plugins -Recurse -Filter *.dll).Count

    # 1. Folders an audio-only player can never load (video/subtitle/OSD/streaming-out/lua).
    $dropFolders = @(
        "lua", "hrtfs",
        "plugins\lua", "plugins\keystore", "plugins\meta_engine",
        "plugins\visualization", "plugins\services_discovery",
        "plugins\video_chroma", "plugins\video_filter", "plugins\video_output", "plugins\video_splitter",
        "plugins\spu", "plugins\text_renderer",
        "plugins\stream_out", "plugins\stream_extractor", "plugins\mux", "plugins\access_output",
        "plugins\d3d9", "plugins\d3d11"
    )
    foreach ($rel in $dropFolders) {
        $p = Join-Path $WinX64 $rel
        if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }

    # 2. Import libraries are build-time only; the runtime never maps them.
    Get-ChildItem $WinX64 -Filter *.lib -File | Remove-Item -Force

    # 3. Whitelist per folder: keep only what audio playback can actually reach.
    $keep = @{
        "access" = @(
            "libfilesystem_plugin.dll", "libhttp_plugin.dll", "libhttps_plugin.dll",
            "libtcp_plugin.dll", "libudp_plugin.dll", "libimem_plugin.dll",
            "libattachment_plugin.dll", "libconcat_plugin.dll" )
        "demux" = @(
            "libadaptive_plugin.dll", "libmp4_plugin.dll", "libogg_plugin.dll", "libflacsys_plugin.dll",
            "libwav_plugin.dll", "libes_plugin.dll", "libplaylist_plugin.dll", "libasf_plugin.dll",
            "libaiff_plugin.dll", "libau_plugin.dll", "libcaf_plugin.dll", "libtta_plugin.dll",
            "libvoc_plugin.dll", "libxa_plugin.dll" )
        "codec" = @(
            "libavcodec_plugin.dll", "libmpg123_plugin.dll", "libflac_plugin.dll", "libopus_plugin.dll",
            "libvorbis_plugin.dll", "libspeex_plugin.dll", "libg711_plugin.dll", "liblpcm_plugin.dll",
            "libaraw_plugin.dll", "libadpcm_plugin.dll", "libmft_plugin.dll", "libdmo_plugin.dll",
            "libspdif_plugin.dll", "libddummy_plugin.dll" )
        "audio_filter" = @(
            "libaudio_format_plugin.dll", "libscaletempo_plugin.dll", "libscaletempo_pitch_plugin.dll",
            "libequalizer_plugin.dll", "libgain_plugin.dll", "libnormvol_plugin.dll",
            "libspeex_resampler_plugin.dll", "libugly_resampler_plugin.dll",
            "libtrivial_channel_mixer_plugin.dll", "libsimple_channel_mixer_plugin.dll" )
        "audio_mixer"  = @( "libfloat_mixer_plugin.dll", "libinteger_mixer_plugin.dll" )
        "audio_output" = @(
            "libdirectsound_plugin.dll", "libmmdevice_plugin.dll", "libwasapi_plugin.dll", "libwaveout_plugin.dll", "libadummy_plugin.dll" )
        "misc"          = @( "libxml_plugin.dll", "libgnutls_plugin.dll" )
        "stream_filter" = @(
            "libcache_read_plugin.dll", "libcache_block_plugin.dll", "libprefetch_plugin.dll",
            "libinflate_plugin.dll", "libskiptags_plugin.dll" )
        "packetizer" = @(
            "libpacketizer_mpegaudio_plugin.dll", "libpacketizer_mpeg4audio_plugin.dll",
            "libpacketizer_flac_plugin.dll", "libpacketizer_copy_plugin.dll" )
    }
    foreach ($folder in $keep.Keys) {
        $dir = Join-Path $plugins $folder
        if (-not (Test-Path $dir)) { continue }
        Get-ChildItem $dir -File | Where-Object { $_.Name -notin $keep[$folder] } | Remove-Item -Force
    }

    # 4. Fail the build loudly if a playback-critical file vanished (whitelist typo guard).
    $mustSurvive = @(
        "libvlc.dll", "libvlccore.dll",
        "plugins\access\libhttp_plugin.dll", "plugins\access\libfilesystem_plugin.dll",
        "plugins\demux\libes_plugin.dll", "plugins\demux\libmp4_plugin.dll", "plugins\demux\libadaptive_plugin.dll",
        "plugins\codec\libmpg123_plugin.dll", "plugins\codec\libavcodec_plugin.dll",
        "plugins\audio_filter\libaudio_format_plugin.dll",
        "plugins\audio_output\libmmdevice_plugin.dll",
        "plugins\audio_mixer\libfloat_mixer_plugin.dll"
    )
    foreach ($rel in $mustSurvive) {
        if (-not (Test-Path (Join-Path $WinX64 $rel))) {
            Write-Error "VLC prune removed a playback-critical file: $rel"
        }
    }

    $afterMb   = Get-DirSizeMB $WinX64
    $afterDlls = (Get-ChildItem $plugins -Recurse -Filter *.dll).Count
    Write-Host ("   VLC pruned: {0} -> {1} plugin dlls | {2} MB -> {3} MB" -f $beforeDlls, $afterDlls, $beforeMb, $afterMb) -ForegroundColor DarkGray
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
        
        # CALL THE PRUNER HERE!
        Prune-VlcPlugins (Join-Path $vlc "win-x64")
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