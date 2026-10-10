param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$Executable = [IO.Path]::GetFullPath($Executable)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$jobs = @(
    @('ui','en','--ui-check',$true), @('dialogs','en','--dialog-check',$true),
    @('windows-en','en','--windows-integration',$false), @('windows-zh','zh-CN','--windows-integration',$false),
    @('lifecycle','en','--lifecycle',$false), @('clicks-en','en','--clicks',$false),
    @('clicks-zh','zh-CN','--clicks',$false), @('shortcuts','en','--shortcuts',$false),
    @('camera-raw','zh-CN','--camera-raw',$false), @('performance','en','--performance',$false),
    @('native-icons','en','--native-icons',$true), @('brush-performance','en','--brush-performance',$false),
    @('stroke-tools-performance','en','--stroke-tools-performance',$false), @('materials','zh-CN','--materials',$true),
    @('editing-controls','en','--editing-controls',$true))
$previousData = $env:COMPOSITOR_DATA_DIR
$previousFastCheck = $env:COMPOSITOR_MAC_QA_ONLY
$results = @()
try {
    $env:COMPOSITOR_MAC_QA_ONLY = $null
    foreach ($job in $jobs) {
        $name, $language, $command, $directoryOutput = $job
        $folder = Join-Path $OutputDirectory $name
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        $env:COMPOSITOR_DATA_DIR = Join-Path $folder 'data'
        $target = if ($directoryOutput) { $folder } elseif ($command -in @('--performance','--brush-performance','--stroke-tools-performance')) { Join-Path $folder 'performance.json' } else { Join-Path $folder 'window.png' }
        $log = Join-Path $folder 'stdout.log'
        $process = Start-Process -FilePath $Executable -ArgumentList @('--lang',$language,$command,('"'+$target+'"')) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $log -RedirectStandardError (Join-Path $folder 'stderr.log')
        if ($process.ExitCode -ne 0) { throw "Published check failed: $name; see $folder" }
        $lines = @(Get-Content -LiteralPath $log | Where-Object { $_ -and $_ -notlike 'wrote *' })
        $results += [ordered]@{name=$name;exit_code=$process.ExitCode;report_lines=$lines.Count;
            pass_lines=@($lines | Where-Object { $_ -like 'PASS:*' }).Count}
        Write-Output "PASS: published $name"
    }
} finally { $env:COMPOSITOR_DATA_DIR=$previousData; $env:COMPOSITOR_MAC_QA_ONLY=$previousFastCheck }
[ordered]@{exe_sha256=(Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash.ToLowerInvariant();checks=$results} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'verification-summary.json') -Encoding utf8
