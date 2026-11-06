# Package the verified release directory as the single English-named ZIP used by GitHub Releases and the updater.
param([Parameter(Mandatory)][string]$ReleaseDirectory, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = [IO.Path]::GetFullPath($ReleaseDirectory)
[xml]$brand = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
$name = [string]$brand.Project.PropertyGroup.AppEnglishName
$exeName = "$name.exe"
$exePattern = [Regex]::Escape($exeName)
$exe = Join-Path $source $exeName
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion.Split('+')[0]
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?$') { throw 'Invalid executable version.' }
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'release\packages' }
if ($output.StartsWith($source.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or $output -eq $source) { throw 'Package output must be outside the executable directory.' }
$files = Get-ChildItem -LiteralPath $source -File -Recurse
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($source, $file.FullName).Replace('\', '/')
    if ($relative -notmatch "^($exePattern|SHA256SUMS\.txt|licenses/[A-Za-z0-9_.-]+\.(txt|md|pdf))$") { throw "Unexpected release file: $relative" }
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Release files cannot be links.' }
}
$actual = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$manifest = Get-Content -LiteralPath (Join-Path $source 'SHA256SUMS.txt') -Raw
if ($manifest -notmatch "(?im)^$actual\s+\*?$exePattern\s*$") { throw 'Executable checksum mismatch.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
# GitHub release assets cannot contain spaces; keep the file name identical to AppIdentity.PackageName.
$package = Join-Path $output "$($name.Replace(' ', '-'))-$version-win-x64.zip"
$checksum = $package -replace '\.zip$', '.sha256'
if ((Test-Path -LiteralPath $package) -or (Test-Path -LiteralPath $checksum)) { throw "Package already exists: $package" }
[IO.Compression.ZipFile]::CreateFromDirectory($source, $package, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
[IO.File]::WriteAllText($checksum, "$hash  $([IO.Path]::GetFileName($package))`n", [Text.UTF8Encoding]::new($false))
Get-Item -LiteralPath $package | Select-Object FullName, Length
