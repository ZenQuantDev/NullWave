<#
.SYNOPSIS
  Starts NullWave several times and reports how long it takes to show a window, how much CPU it
  uses while starting, and how much memory it holds after it settles.

.DESCRIPTION
  Works in Windows PowerShell 5.1 and PowerShell 7. It never changes any file in the repository.

  Metrics per run:
    WindowMs  time from launch until the process has a top-level window handle. This is roughly
              "something is on screen"; it is NOT "the library is ready". For that, read the
              [STARTUP-TIME] line that NullWave writes to its log.
    CpuMs     total processor time the process used by the end of the settle period. Lower is
              kinder to a dual-core laptop; this number often matters more than window time.
    MemMb     working set at the end of the settle period.

  Measure a Release build (dotnet publish -c Release), not `dotnet run`: a Debug build has no
  ReadyToRun code and extra diagnostics, so its numbers say little about what users see.

.PARAMETER ExePath       The NullWave.exe to launch. Default: .\publish\win\NullWave.exe
.PARAMETER Runs          How many times to launch it. Default: 7
.PARAMETER SettleSeconds How long to let it run after the window appears before measuring CPU and
                         memory. Default: 8
.PARAMETER FreshProfile  Use a new empty data folder for every run (sets NULLWAVE_HOME to a temp
                         folder). Without this switch the app uses your real profile, so close any
                         running NullWave first; the single-instance lock would otherwise stop it.
.PARAMETER Csv           Optional path to save the per-run numbers.

.EXAMPLE
  .\scripts\measure-startup.ps1 -ExePath .\publish\win\NullWave.exe -Runs 7
.EXAMPLE
  .\scripts\measure-startup.ps1 -FreshProfile -Csv .\startup-fresh.csv
#>
param(
    [string]$ExePath = ".\publish\win\NullWave.exe",
    [int]$Runs = 7,
    [int]$SettleSeconds = 8,
    [switch]$FreshProfile,
    [string]$Csv
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ExePath)) {
    throw "Cannot find '$ExePath'. Publish a Release build first, or pass -ExePath."
}
$ExePath = (Resolve-Path $ExePath).Path
$processName = [System.IO.Path]::GetFileNameWithoutExtension($ExePath)

if (-not $FreshProfile) {
    $already = Get-Process -Name $processName -ErrorAction SilentlyContinue
    if ($already) {
        throw "'$processName' is already running. Close it first (single-instance lock), or use -FreshProfile."
    }
}

function Get-Median([double[]]$values) {
    if ($values.Count -eq 0) { return 0 }
    $sorted = @($values | Sort-Object)
    $n = $sorted.Count
    if ($n % 2 -eq 1) { return $sorted[[int](($n - 1) / 2)] }
    return ($sorted[($n / 2) - 1] + $sorted[$n / 2]) / 2
}

Write-Host "Measuring $ExePath ($Runs runs, settle $SettleSeconds s, fresh profile: $($FreshProfile.IsPresent))" -ForegroundColor Cyan

$results = @()

for ($i = 1; $i -le $Runs; $i++) {
    $tempHome = $null
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $ExePath
    $psi.WorkingDirectory = Split-Path $ExePath
    $psi.UseShellExecute = $false

    if ($FreshProfile) {
        $tempHome = Join-Path ([System.IO.Path]::GetTempPath()) ("nullwave-measure-" + [guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $tempHome | Out-Null
        $psi.EnvironmentVariables["NULLWAVE_HOME"] = $tempHome
    }

    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = [System.Diagnostics.Process]::Start($psi)
    $windowMs = $null

    try {
        while ($clock.Elapsed.TotalSeconds -lt 60) {
            $proc.Refresh()
            if ($proc.HasExited) { break }
            if ($proc.MainWindowHandle -ne [IntPtr]::Zero) {
                $windowMs = $clock.ElapsedMilliseconds
                break
            }
            Start-Sleep -Milliseconds 15
        }

        if ($null -eq $windowMs) {
            Write-Warning "Run $i : no window appeared (exited: $($proc.HasExited)). Skipping this run."
        }
        else {
            Start-Sleep -Seconds $SettleSeconds
            $proc.Refresh()
            $cpuMs = [math]::Round($proc.TotalProcessorTime.TotalMilliseconds)
            $memMb = [math]::Round($proc.WorkingSet64 / 1MB)

            $row = [pscustomobject]@{ Run = $i; WindowMs = $windowMs; CpuMs = $cpuMs; MemMb = $memMb }
            $results += $row
            Write-Host ("Run {0}: window {1} ms | cpu {2} ms | memory {3} MB" -f $i, $windowMs, $cpuMs, $memMb)
        }
    }
    finally {
        try {
            if (-not $proc.HasExited) {
                [void]$proc.CloseMainWindow()
                if (-not $proc.WaitForExit(5000)) { $proc.Kill() }
            }
        } catch { }

        if ($tempHome) {
            Start-Sleep -Milliseconds 300
            Remove-Item -Recurse -Force $tempHome -ErrorAction SilentlyContinue
        }
        Start-Sleep -Seconds 1
    }
}

if ($results.Count -eq 0) { throw "No successful runs." }

Write-Host ""
Write-Host "Summary ($($results.Count) runs)" -ForegroundColor Green
foreach ($metric in @("WindowMs", "CpuMs", "MemMb")) {
    $values = [double[]]($results | ForEach-Object { $_.$metric })
    $min = ($values | Measure-Object -Minimum).Minimum
    $max = ($values | Measure-Object -Maximum).Maximum
    $median = Get-Median $values
    Write-Host ("{0,-9} min {1,7:N0}   median {2,7:N0}   max {3,7:N0}" -f $metric, $min, $median, $max)
}

if ($Csv) {
    $results | Export-Csv -Path $Csv -NoTypeInformation
    Write-Host "Saved $Csv"
}

Write-Host ""
Write-Host "Tip: the first run is often slower (cold disk cache). Compare medians, and compare builds on the same machine." -ForegroundColor DarkGray
