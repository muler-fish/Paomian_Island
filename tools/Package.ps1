# Publish the English executable; older updaters receive the same binary under their expected filename.
param([Parameter(Mandatory)][string]$ReleaseDirectory, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = [IO.Path]::GetFullPath($ReleaseDirectory)
[xml]$brand = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
$name = [string]$brand.Project.PropertyGroup.AppEnglishName
$chineseName = [string]$brand.Project.PropertyGroup.AppDisplayName
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
$package = Join-Path $output "$name-$version-win-x64.zip"
$chinese = Join-Path $output "$chineseName-$version-win-x64.zip"
$legacy = Join-Path $output "Tide-$version-win-x64.zip"
foreach ($path in @($package, $chinese, $legacy)) {
    if ((Test-Path -LiteralPath $path) -or (Test-Path -LiteralPath ($path -replace '\.zip$', '.sha256'))) { throw "Package already exists: $path" }
}
[IO.Compression.ZipFile]::CreateFromDirectory($source, $package, [IO.Compression.CompressionLevel]::Optimal, $false)
$archive = [IO.Compression.ZipFile]::Open($legacy, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName).Replace('\', '/')
        if ($relative -eq 'SHA256SUMS.txt') {
            $writer = [IO.StreamWriter]::new($archive.CreateEntry($relative).Open(), [Text.UTF8Encoding]::new($false))
            try { $writer.Write("$actual  Tide.exe`n") } finally { $writer.Dispose() }
        } else {
            $entryName = if ($relative -eq $exeName) { 'Tide.exe' } else { $relative }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
} finally { $archive.Dispose() }
Copy-Item -LiteralPath $legacy -Destination $chinese
foreach ($path in @($package, $chinese, $legacy)) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    [IO.File]::WriteAllText(($path -replace '\.zip$', '.sha256'), "$hash  $([IO.Path]::GetFileName($path))`n", [Text.UTF8Encoding]::new($false))
}
Get-Item -LiteralPath $package, $chinese, $legacy | Select-Object FullName, Length
