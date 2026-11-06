# 从任意工作目录执行均可；传入 -SelfContained 时生成带 .NET 运行时的单文件版本。
param([switch]$SelfContained, [string]$OutputDirectory, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $projectRoot
try {
    # 已还原过依赖的工作区可离线构建，避免为纯界面修改再次访问 NuGet。
    $restoreArgs = @()
    if ($NoRestore) { $restoreArgs += '--no-restore' }
    # 先编译并运行模拟接口测试，失败时立即停止，避免继续发布未通过检查的程序。
    dotnet build Tide.slnx -c Release --nologo @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet 'tests\Tide.Tests\bin\Release\net10.0\Tide.Tests.dll'
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    # 默认按版本输出，试用界面不会覆盖已经交付的 standalone；需要指定路径时显式传入参数。
    [xml]$project = Get-Content -Raw 'src\Tide.Desktop\Tide.Desktop.csproj'
    $version = [string]$project.Project.PropertyGroup.Version
    $folder = if ($SelfContained) { $version } else { "$version-lite" }
    $target = if ($OutputDirectory) { $OutputDirectory } else { Join-Path 'release' $folder }
    $contained = if ($SelfContained) { 'true' } else { 'false' }
    # 单文件包含原生库并启用压缩；部分原生文件会在启动时由 .NET 提取到缓存目录。
    dotnet publish src/Tide.Desktop/Tide.Desktop.csproj -c Release -r win-x64 --self-contained $contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $target --nologo @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    # WPF UI 及其内置图标等资源的许可随二进制一起交付；包位置由 NuGet 还原结果确定。
    $assets = Get-Content -Raw 'src\Tide.Desktop\obj\project.assets.json' | ConvertFrom-Json
    $uiVersion = ($project.Project.ItemGroup.PackageReference | Where-Object Include -eq 'WPF-UI').Version
    $package = $assets.packageFolders.PSObject.Properties.Name |
        ForEach-Object { Join-Path $_ "wpf-ui\$uiVersion" } |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'LICENSE.md') } | Select-Object -First 1
    if (!$package) { throw 'WPF UI license files not found in the restored package.' }
    $licenses = Join-Path $target 'licenses'
    New-Item -ItemType Directory -Force $licenses | Out-Null
    Copy-Item -LiteralPath (Join-Path $package 'LICENSE.md') -Destination (Join-Path $licenses 'WPF-UI-LICENSE.md')
    Copy-Item -LiteralPath (Join-Path $package 'ThirdPartyNotices.txt') -Destination (Join-Path $licenses 'WPF-UI-ThirdPartyNotices.txt')
    # 二维码识别使用 ZXing.Net；许可证随自包含 EXE 交付，构建时不再联网下载。
    # 项目自身以 GPL-3.0 发布，许可全文随程序一同交付。
    Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $licenses 'PaomianIsland-GPL-3.0.txt')
    Copy-Item -LiteralPath 'licenses\ZXing.Net-LICENSE.txt' -Destination (Join-Path $licenses 'ZXing.Net-LICENSE.txt')
    Copy-Item -LiteralPath 'licenses\NotoSansTagbanwa-OFL.txt' -Destination (Join-Path $licenses 'NotoSansTagbanwa-OFL.txt')
    Copy-Item -LiteralPath 'licenses\NotoSansBatak-OFL.txt' -Destination (Join-Path $licenses 'NotoSansBatak-OFL.txt')
    Copy-Item -LiteralPath 'licenses\EmoticonData-NOTICE.txt' -Destination (Join-Path $licenses 'EmoticonData-NOTICE.txt')
    Copy-Item -LiteralPath 'licenses\XamlAnimatedGif-LICENSE.txt' -Destination (Join-Path $licenses 'XamlAnimatedGif-LICENSE.txt')
    Copy-Item -LiteralPath 'licenses\XamlAnimatedGif-NOTICE.txt' -Destination (Join-Path $licenses 'XamlAnimatedGif-NOTICE.txt')
    foreach ($notice in @('Emoji-NOTICE.txt', 'Emoji.Wpf-LICENSE.txt', 'Typography-NOTICES.md', 'Typography-MIT.txt', 'FreeType-LICENSE.txt', 'Unicode-LICENSE.txt', 'JeremyAnsel.HLSL-LICENSE.txt')) {
        Copy-Item -LiteralPath (Join-Path 'licenses' $notice) -Destination (Join-Path $licenses $notice)
    }
    [xml]$brand = Get-Content -LiteralPath 'Directory.Build.props' -Raw
    $exeName = [string]$brand.Project.PropertyGroup.AppEnglishName + '.exe'
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $target $exeName)).Hash
    Set-Content -LiteralPath (Join-Path $target 'SHA256SUMS.txt') -Value "$hash  $exeName" -Encoding utf8
    # 以十进制 100 MB 为硬预算，包含本次发布的全部文件和许可证。
    $releaseBytes = (Get-ChildItem -LiteralPath $target -File -Recurse | Measure-Object Length -Sum).Sum
    if ($releaseBytes -ge 100000000) { throw "Release exceeds 100 MB budget: $releaseBytes bytes." }
    Write-Output "Release size: $releaseBytes bytes / 100000000 bytes"
    if ($SelfContained) { & (Join-Path $PSScriptRoot 'Package.ps1') -ReleaseDirectory $target }
    Get-ChildItem -LiteralPath $target | Select-Object FullName,Length
} finally { Pop-Location }
