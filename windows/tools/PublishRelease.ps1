param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Directory 'published/Compositor.exe')).ProductVersion.Split('+')[0]
$tag = 'v' + $version
if ($tag -notmatch '^v\d+\.\d+\.\d+$') { throw 'A release requires a three-part version.' }
$existing = & gh release view $tag --repo $Repository --json assets 2>$null
if ($LASTEXITCODE -eq 0) {
    $assets = ($existing | ConvertFrom-Json).assets
    if (@($assets | Where-Object { $_.name -eq 'Compositor-Setup.exe' }).Count) {
        Write-Output "Release $tag already has an installer; immutable release assets were preserved."
        exit 0
    }
}
$archive = Join-Path $Directory 'Compositor-Windows-source.zip'
& git archive --format=zip --output $archive HEAD
if ($LASTEXITCODE -ne 0) { throw 'Source archiving failed.' }
$installer = Join-Path $Directory 'Compositor-Setup.exe'
$checksums = Join-Path $Directory 'SHA256SUMS.txt'
@($installer,$archive) | ForEach-Object {
    $hash=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($_))"
} | Set-Content -LiteralPath $checksums -Encoding utf8NoBOM
$notes = Join-Path $Directory 'release-notes.md'
@("Compositor Windows $version", '', 'Install or upgrade with Compositor-Setup.exe.',
    'Default: %LOCALAPPDATA%/Programs/Compositor. Settings and editable projects are preserved.',
    'English/Chinese UI; Windows window controls and shortcuts; macOS-inspired app controls.',
    'Core, published UI, dialogs, animation frames and Windows workflow checks passed.',
    'See windows/INSTALLING.md and docs/mac-parity-checklist.md for features and remaining differences.',
    'This community Windows build is unsigned. Full native macOS parity is not yet verified.') |
    Set-Content -LiteralPath $notes -Encoding utf8NoBOM
& gh release create $tag $installer $archive $checksums --repo $Repository --target $env:GITHUB_SHA --title "Compositor Windows $version" --notes-file $notes
if ($LASTEXITCODE -ne 0) { throw 'GitHub release publication failed.' }
