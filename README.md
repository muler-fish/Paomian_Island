# 泡面岛（Paomian Island）

## 项目简介

泡面岛（英文名 **Paomian Island**）是 X 岛匿名版的非官方 Windows 客户端，使用 **WPF / .NET 10 / WPF UI** 开发。采用板块导航、串列表和串阅读区的三栏布局，面向日常浏览、订阅与发言。项目原名「潮汐」，GitHub 仓库地址暂时沿用 `muler-fish/Tide`。

[下载正式版](https://github.com/muler-fish/Tide/releases/latest) · [待办列表](TODO.md) · [反馈问题](https://github.com/muler-fish/Tide/issues)

## 功能特点

- **连续阅读**：列表自动续页、串内双向续页、页码跳转、阅读位置保存和只看 PO。
- **订阅管理**：支持多个通用订阅号、备注、切换及中文输入；快照与短期缓存减少重复加载。
- **三栏布局**：可调栏宽，记住窗口位置和布局；采用 Refined Reader 界面，提供浅色、深色主题及阅读字号、行距、字距设置。
- **正文交互**：饼干、PO、SAGE 标记，引用小窗、站内串预览、板块跳转确认、官方公告和 tips。
- **独立看图**：缩放、拖动、旋转、保存原图，右键复制图片到剪贴板，末图自动尝试加载下一页。GIF 当前显示首帧，复制为当前静态图，保存时保留原文件。
- **发串与回应**：可选标题和名称、附图、草稿、发送前预览、颜文字、防剧透和骰子。
- **本机管理**：板块收藏、浏览历史、本机发言记录、屏蔽串和板块，以及右键复制和回应。
- **饼干管理**：管理与导入分开显示，支持本机备注和默认饼干；发言选择器显示当前串内使用次数。可通过二维码图片、剪贴板图片、完整 userhash、Cookie 头与 JSON 导入。
- **分类设置**：阅读、外观、网络、缓存、数据与记录、关于六类；阅读效果实时预览，支持缓存容量和路径管理，以及独立的岛内和更新网络选项。

## 安装步骤

运行环境：**Windows 10/11 x64**。本项目采用解压即用的便携包，目前没有单独的安装程序或卸载注册项。

1. 前往 [Releases](https://github.com/muler-fish/Tide/releases/latest)，选择已发布的 Windows x64 版本。新版包名为 `Paomian Island-版本号-win-x64.zip`。
2. 解压到任意目录，运行 `Paomian Island.exe`。界面及 Windows 产品显示名称仍为「泡面岛」。`泡面岛-版本号-win-x64.zip` 和 `Tide-版本号-win-x64.zip` 用于兼容旧更新器，包内保留 `Tide.exe`；自动更新沿用已安装的文件名以保持原快捷方式有效。
3. 更新时先退出旧版，再运行新版。默认资料目录相同，可继续使用原有设置。

发布包已包含 .NET 运行时，无需单独安装。程序、许可及启动时提取的运行时文件以 **100 MB 以内**为目标，个人资料和缓存另计。版本、网络、缓存和更新选项位于设置中；正文预览最多显示 7 行。

## 使用示例

### 浏览与跳串

在左侧选择板块，点击中间的串预览即可阅读。按 **Ctrl+L** 输入串号或 X 岛链接，按 Enter 打开；在串内的页码框输入目标页后跳转。

### 订阅与发言

首次启动会自动生成并保存一个随机订阅号，重启后继续使用。打开“我的订阅”管理订阅号，支持查看、修改、备注与切换。在串内点击星形按钮或按 **Ctrl+D** 订阅。

普通浏览无需导入饼干；发言及受限内容需要有效饼干。在左下角“饼干管理”导入后，点击对应内容区的发串或回应按钮。标题和名称位于默认折叠的可选项中，发送前可预览内容。

### 独立资料目录

默认数据继续保存在 `%LOCALAPPDATA%\TideIsland`。此次更名不移动或重建资料目录，原有饼干、订阅号、历史、草稿、发言记录及缓存路径设置会继续使用。需要使用独立资料目录时：

```powershell
& ".\Paomian Island.exe" --data-dir "D:\泡面岛资料"
```

同一资料目录只允许运行一个实例。饼干和订阅号使用当前 Windows 用户的 DPAPI 加密；订阅号可在管理窗口主动查看、复制。

### 常用快捷键

| 快捷键 | 操作 |
| --- | --- |
| Ctrl+L | 聚焦跳串框 |
| F5 | 刷新串列表 |
| Ctrl+R | 刷新正文 |
| Ctrl+D | 订阅 / 取消订阅 |
| Esc | 关闭子窗口 |
| 看图时滚轮 / + / − | 缩放 |
| 看图时 ← / → | 切换图片 |
| 看图时 1 / 0 / R | 原始比例 / 适应窗口 / 向左旋转 |

## 贡献指南

欢迎通过 [Issues](https://github.com/muler-fish/Tide/issues) 提交问题或建议。报告问题时请提供程序版本、Windows 版本、显示缩放、复现步骤和必要截图，并隐去饼干密钥、订阅号等个人信息。

### 本地开发

需要 Windows 和 **.NET 10 SDK**：

```powershell
git clone https://github.com/muler-fish/Tide.git
cd Tide
dotnet restore Tide.slnx
dotnet build Tide.slnx -c Release --no-restore
dotnet run --project src/Tide.Desktop -- --data-dir "artifacts/dev-profile"
```

- `src/Tide.Core`：API、数据模型、缓存与本地存储。
- `src/Tide.Desktop`：WPF 界面、视图模型和阅读交互。
- `tests`：核心测试及桌面专项检查。
- `tools`：发布和资源生成脚本；`licenses`：第三方许可。

提交改动时说明修改原因与验证结果。按改动范围运行相关专项，避免重复执行无关测试；例如点击状态与正文渲染的验证入口：

```powershell
.\tests\Tide.Desktop.Tests\bin\Release\net10.0-windows\Tide.Desktop.Tests.exe --reading-polish artifacts/reading-polish
```

使用独立资料目录，不要提交个人数据、`bin/`、`obj/`、`artifacts/` 或 `release/`。当前工作项见 [TODO.md](TODO.md)；准备较大的功能改动前，可先在 Issue 中讨论。

构建自包含发布包可运行 `.\tools\Build.ps1 -SelfContained`，该脚本会编译、执行核心测试、复制许可并检查发布目录大小，然后在 `release/packages` 生成 `Paomian Island` 英文名发布包，以及 `泡面岛`、`Tide` 两个旧更新器兼容包。三个 ZIP 使用相同程序内容，兼容包仅保留旧可执行文件名并更新对应校验清单；发布到 Releases 时需同时附带三种 ZIP 及各自的 `.sha256` 文件。

中英文名称集中在 `Directory.Build.props`；兼容标识见 `src/Tide.Core/AppIdentity.cs`。Logo 使用已确认的「挑面」A v2，矢量源文件、PNG 与多尺寸 ICO 位于 `src/Tide.Desktop/Assets/paomian.*`。Refined Reader 的主题颜色与控件样式集中在 `Themes/`，通过 `App.ApplyTheme` 切换；共用几何参数与预览行数位于 `UiMetrics.cs`，设置行规范位于 `SettingsLayout.cs`。

## 许可证

项目自身的源码暂未指定开源许可证。第三方组件、字体和资料遵循各自的许可；相关文件位于 [licenses](licenses)，发布包中也附带许可文件。WPF UI 的许可及第三方声明由构建脚本从还原的 NuGet 包中复制。

交互参考 [摸鱼岛](https://github.com/junbluedd/Moyu-Island)，接口约定参考 [雾岛](https://github.com/123Duo3/XDNMB)和[霞岛](https://github.com/orzogc/xdnmb)。本项目不是 X 岛官方软件。
