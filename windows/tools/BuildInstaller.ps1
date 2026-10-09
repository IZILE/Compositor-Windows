param(
    [string]$Dotnet = 'dotnet',
    [string]$Compiler,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../dist'),
    [string]$PublishedDirectory,
    [switch]$SkipTests,
    [switch]$TestInstall
)
$ErrorActionPreference = 'Stop'
$windowsRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent $windowsRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$payload = Join-Path $OutputDirectory 'installer-payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
$project = Join-Path $windowsRoot 'src/Compositor.Desktop/Compositor.Desktop.csproj'
[xml]$projectMetadata = Get-Content -LiteralPath $project -Raw
$version = [string]$projectMetadata.Project.PropertyGroup.Version
function Invoke-Dotnet([string[]]$DotnetArguments) {
    & $Dotnet @DotnetArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
if (-not $PublishedDirectory) {
    if (-not $SkipTests) {
        Invoke-Dotnet -DotnetArguments @('test', (Join-Path $windowsRoot 'tests/Compositor.Core.Tests/Compositor.Core.Tests.csproj'), '-c', 'Release')
    }
    $PublishedDirectory = Join-Path $OutputDirectory 'published'
    Invoke-Dotnet -DotnetArguments @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None',
        '-p:DebugSymbols=false', '-warnaserror', '-o', $PublishedDirectory)
}
$executable = Join-Path $PublishedDirectory 'Compositor.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'The published executable is missing.' }
$actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion.Split('+')[0]
if ($actualVersion -ne $version) { throw "Version mismatch: source $version, executable $actualVersion" }
Copy-Item -LiteralPath $executable -Destination $payload -Force
Copy-Item -LiteralPath (Join-Path $windowsRoot 'src/Compositor.Desktop/Assets/Compositor.ico') -Destination $payload -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $payload -Force
Copy-Item -LiteralPath (Join-Path $windowsRoot 'INSTALLING.md') -Destination (Join-Path $payload 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $windowsRoot 'src/Compositor.Desktop/Assets/ICON-SOURCE.md') -Destination $payload -Force
@{ version = $version; channel = 'installed'; settings = 'LocalAppData/Compositor-Windows/data' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $payload 'compositor.install.json') -Encoding utf8

$licenses = Join-Path $payload 'ThirdPartyLicenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $windowsRoot 'licenses') -File | Copy-Item -Destination $licenses -Force
$assetPath = Join-Path $windowsRoot 'src/Compositor.Desktop/obj/project.assets.json'
$assets = Get-Content -LiteralPath $assetPath -Raw | ConvertFrom-Json
$packageRoots = $assets.packageFolders.PSObject.Properties.Name
$inventory = @('# Dependency inventory', '', '| Package | License | Source |', '| --- | --- | --- |')
foreach ($library in $assets.libraries.PSObject.Properties | Sort-Object Name) {
    if ($library.Value.type -ne 'package') { continue }
    $packagePath = $null
    foreach ($root in $packageRoots) {
        $candidate = Join-Path $root $library.Value.path
        if (Test-Path -LiteralPath $candidate -PathType Container) { $packagePath = $candidate; break }
    }
    if (-not $packagePath) { throw "Missing NuGet metadata: $($library.Name)" }
    $spec = Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' -File | Select-Object -First 1
    $destination = Join-Path $licenses ($library.Name.Replace('/', '-'))
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $spec.FullName -Destination $destination -Force
    foreach ($notice in Get-ChildItem -LiteralPath $packagePath -Recurse -File | Where-Object { $_.Name -match '^(license|copying|notice|third-party-notices)' }) {
        $relative = [IO.Path]::GetRelativePath($packagePath, $notice.FullName)
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $notice.FullName -Destination $target -Force
    }
    [xml]$specification = Get-Content -LiteralPath $spec.FullName -Raw
    $metadata = $specification.package.metadata
    $license = if ($metadata.license -is [string]) { $metadata.license } else { $metadata.license.InnerText }
    $source = if ($metadata.repository.url) { $metadata.repository.url } else { $metadata.projectUrl }
    $inventory += "| $($library.Name) | $license | $source |"
}
$inventory += @('', 'The graph includes dependencies for other platforms; only Windows runtime files are bundled.',
    'LibRaw source: LibRaw-0.21.1.tar.gz; LGPL-2.1.txt. Binding: https://github.com/sdcb/Sdcb.LibRaw.',
    'Build with PublishSingleFile=false to use separate replaceable native libraries.',
    'Inter font: OFL-Inter.txt. Original app and icon: repository MIT license.')
$inventory | Set-Content -LiteralPath (Join-Path $licenses 'DEPENDENCIES.md') -Encoding utf8

if (-not $Compiler) {
    $candidates = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7/ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'))
    $Compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $Compiler) { throw 'Supply -Compiler with a verified Inno Setup ISCC.exe path.' }
$defines = @("/DPublishDir=$payload", "/DDeliveryDir=$OutputDirectory", "/DAppVersion=$version")
if ($TestInstall) { $defines += @('/DTestInstall=1', '/DInstallIdentity={65632CB5-558C-46FC-A1C7-51B0217D1FC5}') }
& $Compiler @defines (Join-Path $windowsRoot 'installer/Compositor.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE" }
[pscustomobject]@{ Version = $version; Installer = (Join-Path $OutputDirectory 'Compositor-Setup.exe'); Payload = $payload }
