<div align="center">

<img src="src/Paomian.Desktop/Assets/paomian.png" width="96" alt="泡面岛 Logo">

# 泡面岛 · Paomian Island

**X 岛匿名版的非官方 Windows 客户端**

支持三栏阅读、键盘浏览、连续翻页，以及订阅、发串和回应。

[下载 1.0.3（Windows x64）](https://github.com/muler-fish/Paomian_Island/releases/download/v1.0.3/Paomian-Island-1.0.3-win-x64.zip) · [发布页和校验文件](https://github.com/muler-fish/Paomian_Island/releases/tag/v1.0.3) · [更新记录](CHANGELOG.md) · [反馈问题](https://github.com/muler-fish/Paomian_Island/issues)

</div>

---

## 快速开始

泡面岛可在 64 位 Windows 10 和 Windows 11 上直接运行。

1. 下载页面顶部的 Windows x64 压缩包，解压后双击 `Paomian Island.exe`。
2. 在左栏选一个版块，点击中栏的串，就能在右栏阅读。游客最多可以浏览前 100 页。
3. 要发串或回应，先在左下角打开「饼干管理」并导入饼干。

有新版本时，「设置」入口会显示提示，打开后即可下载更新。

## 它能做什么

**阅读**
- 三栏布局，包含版块导航、串列表和正文，栏宽和窗口位置都会自动保存。
- 串列表滚到底会自动加载下一页，串内往上往下都能续页，也支持跳到指定页和只看 PO。
- 会保存阅读进度，再打开同一个串时回到上次看到的位置。
- 有浅色和深色主题，正文字号、行间距、字间距都能调，调的时候实时预览。

**看图**
- 独立的看图窗口：缩放、拖动、旋转、保存原图。
- 支持 GIF 动图播放；复制和保存的都是完整原文件。
- 看到最后一张时，会自动尝试加载下一页的图片。

**发言**
- 发串和回应支持草稿、颜文字、防剧透和骰子，发送前可以预览。
- 从 1.0.4 起，可以把图片直接拖进发言窗口、在正文按 `Ctrl+V` 粘贴截图或复制的图片文件，也可以点「添加图片」选择本地文件。一次可附一张 JPG、PNG 或 GIF，最大 5 MB。选好后就能看到缩略图。要换图，可以点缩略图右上角的 ×，也可以用 `Tab` 选中「移除图片」后按空格。
- 管理多个饼干：给饼干写本机备注、设默认饼干，回应时能看到这个饼干在当前串里用过几次。
- 饼干导入方式：二维码图片、剪贴板图片、userhash、Cookie 头或 JSON。
- 「我的发言」保存在本机发送成功的主题和回复。
- 右键可以举报楼层，内容经过预览确认后才会发到值班室。

**整理**
- 支持多个订阅号，每个可以写备注、随时切换。
- 左键点击阅读区的订阅按钮可订阅或取消。右键点击会重新订阅该串以将其移至订阅区顶部。
- 版块收藏、浏览历史、屏蔽串和屏蔽版块。
- 缓存可以设容量上限和存放位置；访问 X 岛和检查更新可以分别选择走系统代理或直连。

## 默认快捷键

用键盘读串时，按 `Tab` 切到串列表，按 `F` 显示字母提示，再输入目标串旁的字母就能打开它。

| 按键 | 作用 |
| --- | --- |
| `Tab` / `Shift+Tab` | 在主窗口浏览时正反向切换三栏，保留各栏位置 |
| 左栏方向键、`Enter` | 选择导航项、展开或收起分组、打开版块或窗口 |
| 中右栏 `↑` / `↓`、`PgUp` / `PgDn` | 连续滚动或按屏翻页 |
| 中右栏 `F` | 显示当前栏可见操作的字母，输入标签即可执行 |
| `Ctrl+L` | 输入串号或 X 岛链接，按 Enter 打开 |
| `F5` | 刷新当前活动的串列表或正文 |
| `Ctrl+R` | 刷新正文 |
| `Ctrl+D` | 订阅或取消订阅当前串 |
| `Ctrl+Alt+H` | 开启老板键后，在 Windows 桌面隐藏或恢复泡面岛 |
| `Esc` | 取消字母提示或关闭子窗口 |
| 看图时 `←` / `→` | 上一张 / 下一张 |
| 看图时滚轮、`+` / `−` | 缩放 |
| 看图时 `1` / `0` / `R` | 原始大小 / 适应窗口 / 向左旋转 |

在主窗口用 `Ctrl+Tab` / `Ctrl+Shift+Tab` 可以依次访问控件。输入框和弹窗内沿用标准的 Tab 操作。常用命令可在「设置」的「快捷键」中调整。

老板键默认关闭。在「设置」的「快捷键」中开启后，主窗口及打开的编辑器、引用和看图窗口可一起隐藏。双击系统托盘图标可恢复，右键图标也可退出。组合键被其他程序占用时可在设置中修改。

## 数据与隐私

首次使用时，泡面岛默认将资料保存在 `%LOCALAPPDATA%\PaomianIsland`。把这个路径粘贴到文件资源管理器的地址栏，即可打开资料目录。删除程序文件夹时，默认资料目录会保留。

- 设置、浏览历史、草稿、饼干和订阅号都保存在本机。饼干和订阅号由 Windows DPAPI 加密，只能在这台电脑上用保存它们的 Windows 账户解密。
- 从 1.0.4 起，粘贴的截图随草稿保存在资料目录。移除附图、删除草稿或发送成功后，如果其他草稿也用不到这张截图，程序会将它清理。拖入或选择的文件仍保存在原处。
- 缓存的位置和容量可以在「设置」中调整。清理缓存会保留个人资料。
- 浏览、订阅和发言时按需连接 X 岛，检查和下载更新时连接 GitHub。程序没有统计或遥测。

### 便携使用与多开

要把资料放在程序旁边的 `profile` 文件夹里，请在解压目录打开 PowerShell，以后每次都用这条命令启动。

```powershell
& ".\Paomian Island.exe" --data-dir "$PWD\profile"
```

`--data-dir` 也可以指向其他资料目录。同一份资料同时只能由一个实例打开，多开时请为每个实例指定不同的目录。删除便携程序时，记得保留存放资料的 `profile` 文件夹。

## 参与开发

需要 Windows 和 [.NET 10 SDK](https://dotnet.microsoft.com/download)；发布脚本使用 PowerShell 7（`pwsh`）。在 PowerShell 中运行：

```powershell
git clone https://github.com/muler-fish/Paomian_Island.git
cd Paomian_Island
dotnet build Paomian.slnx -c Release
dotnet run --project src/Paomian.Desktop -c Release --no-build -- --data-dir "$PWD/artifacts/dev-profile"
```

| 目录 | 内容 |
| --- | --- |
| `src/Paomian.Core` | X 岛接口、数据模型、缓存、本地存储、更新 |
| `src/Paomian.Desktop` | WPF 界面（WPF UI）、视图模型、主题与交互 |
| `tests` | 核心测试和按功能划分的桌面专项检查 |
| `tools` | 构建、打包、图标和颜文字资源脚本 |

发布包用 `pwsh -File tools/Build.ps1 -SelfContained` 生成，当前的工作项见 [TODO.md](TODO.md)。

欢迎提交 Issue 和 PR。报告问题时，请写上版本号、Windows 版本、显示缩放比例和复现步骤，**截图前请遮住饼干和订阅号**。改动比较大的话，建议先开个 Issue 讨论一下。

## 致谢与许可

- 交互参考了记忆中老 A 岛的一款 Windows 客户端，接口参考了[雾岛](https://github.com/123Duo3/XDNMB)和[霞岛](https://github.com/orzogc/xdnmb)。
- 用到了 [WPF UI](https://github.com/lepoco/wpfui)、ZXing.Net、Emoji.Wpf、XamlAnimatedGif 等开源组件，它们各自的许可放在 [licenses](licenses) 目录。
- 泡面岛以 [GPL-3.0](LICENSE) 许可证开源。

泡面岛不是 X 岛的官方软件。
