# 导入已确认的泡面岛图标成品；不重新绘制或改变设计。
param([Parameter(Mandatory)][string]$AssetDirectory)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($AssetDirectory)
$target = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\Tide.Desktop\Assets'))
$files = [ordered]@{
    'paomian-a-v2.ico' = 'paomian.ico'
    'paomian-a-v2-256.png' = 'paomian.png'
    'paomian-a-v2.svg' = 'paomian.svg'
    'paomian-a-mark-v2.svg' = 'paomian-mark.svg'
}
foreach ($name in $files.Keys) {
    if (!(Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) { throw "缺少图标文件：$name" }
}
foreach ($name in $files.Keys) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $target $files[$name]) -Force
}
Get-Item -LiteralPath @($files.Values | ForEach-Object { Join-Path $target $_ }) | Select-Object FullName,Length
