# Package the portable app under its public Chinese name and keep an identical legacy updater asset.
param([Parameter(Mandatory)][string]$ReleaseDirectory, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = [IO.Path]::GetFullPath($ReleaseDirectory)
[xml]$brand = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
$name = [string]$brand.Project.PropertyGroup.AppDisplayName
$exe = Join-Path $source 'Tide.exe'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion.Split('+')[0]
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?$') { throw 'Invalid executable version.' }
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'release\packages' }
if ($output.StartsWith($source.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or $output -eq $source) { throw 'Package output must be outside the executable directory.' }
$files = Get-ChildItem -LiteralPath $source -File -Recurse
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($source, $file.FullName).Replace('\', '/')
    if ($relative -notmatch '^(Tide\.exe|SHA256SUMS\.txt|licenses/[A-Za-z0-9_.-]+\.(txt|md|pdf))$') { throw "Unexpected release file: $relative" }
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Release files cannot be links.' }
}
$actual = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$manifest = Get-Content -LiteralPath (Join-Path $source 'SHA256SUMS.txt') -Raw
if ($manifest -notmatch "(?im)^$actual\s+\*?Tide\.exe\s*$") { throw 'Executable checksum mismatch.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$package = Join-Path $output "$name-$version-win-x64.zip"
$legacy = Join-Path $output "Tide-$version-win-x64.zip"
foreach ($path in @($package, $legacy, ($package -replace '\.zip$', '.sha256'), ($legacy -replace '\.zip$', '.sha256'))) {
    if (Test-Path -LiteralPath $path) { throw "Package already exists: $path" }
}
[IO.Compression.ZipFile]::CreateFromDirectory($source, $package, [IO.Compression.CompressionLevel]::Optimal, $false)
Copy-Item -LiteralPath $package -Destination $legacy
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
foreach ($path in @($package, $legacy)) {
    [IO.File]::WriteAllText(($path -replace '\.zip$', '.sha256'), "$hash  $([IO.Path]::GetFileName($path))`n", [Text.UTF8Encoding]::new($false))
}
Get-Item -LiteralPath $package, $legacy | Select-Object FullName, Length
