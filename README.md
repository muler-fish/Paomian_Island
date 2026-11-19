<div align="center">

<img src="src/Paomian.Desktop/Assets/paomian.png" width="96" alt="泡面岛 Logo">

# 泡面岛 · Paomian Island

**X 岛匿名版的非官方 Windows 客户端**

支持三栏阅读、键盘浏览、连续翻页，以及订阅、发串和回应。

[下载 1.0.3（Windows x64）](https://github.com/muler-fish/Paomian_Island/releases/download/v1.0.3/Paomian-Island-1.0.3-win-x64.zip) · [发布页和校验文件](https://github.com/muler-fish/Paomian_Island/releases/tag/v1.0.3) · [更新记录](CHANGELOG.md) · [反馈问题](https://github.com/muler-fish/Paomian_Island/issues)

</div>

---

## 快速开始

1. 在 [1.0.3 发布页](https://github.com/muler-fish/Paomian_Island/releases/tag/v1.0.3) 下载 `Paomian-Island-1.0.3-win-x64.zip`。
2. 解压到任意文件夹，双击 `Paomian Island.exe`。

- 系统要求：Windows 10 / 11 x64。
- 不需要安装 .NET，也没有安装程序，解压就能用。删除程序文件夹即可移除程序；默认资料目录不会随之删除。
- 游客最多可以浏览前 100 页。要发言，请先在左下角的「饼干管理」里导入饼干。

> 有新版本时，程序会在设置入口提示，可以直接在程序里更新，设置和数据都会保留。

## 它能做什么

**阅读**
- 三栏布局，包含版块导航、串列表和正文，栏宽和窗口位置都会自动保存。
- 串列表滚到底会自动加载下一页，串内往上往下都能续页，也支持跳到指定页和只看 PO。
- 会保存阅读进度，再打开同一个串时回到上次看到的位置。
- 有浅色和深色主题，正文字号、行间距、字间距都能调，调的时候实时预览。
- 键盘操作，一副键盘即可使用大部分功能。

**看图**
- 独立的看图窗口：缩放、拖动、旋转、保存原图。
- 支持 GIF 动图播放；复制和保存的都是完整原文件。
- 看到最后一张时，会自动尝试加载下一页的图片。
- 图片下载中断时会重试一次。缩略图加载失败时可直接查看原图，原图加载失败时可继续查看已缓存的缩略图。

**发言**
- 发串、回应，可以附图，有草稿箱、发送前预览、颜文字、防剧透和骰子。
- 管理多个饼干：给饼干写本机备注、设默认饼干，回应时能看到这个饼干在当前串里用过几次。
- 饼干导入方式：二维码图片、剪贴板图片、userhash、Cookie 头或 JSON。
- 「我的发言」保存在本机发送成功的主题和回复。
- 右键可以举报楼层，内容经过预览确认后才会发到值班室。

**整理**
- 支持多个订阅号，每个可以写备注、随时切换。
- 左键点击阅读区的订阅按钮可订阅或取消。右键点击会重新订阅该串以将其移至订阅区顶部。
- 版块收藏、浏览历史、屏蔽串和屏蔽版块。
- 缓存可以设容量上限和存放位置；访问 X 岛和检查更新可以分别选择走系统代理或直连。
- 检查更新的来源是 GitHub Releases，下载后会核对 SHA-256 校验值。

## 默认快捷键

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

老板键默认关闭。在「设置」的「快捷键」中开启后，主窗口及打开的编辑器、引用和看图窗口可一起隐藏。双击系统托盘图标可恢复，右键图标也可退出。组合键被其他程序占用时可在设置中修改。锁屏和 Windows 安全桌面使用系统自己的键盘处理。

## 数据与隐私

- 全新使用 **1.0.3** 时，默认资料目录是 `%LOCALAPPDATA%\PaomianIsland`。从 **1.0.2** 升级时，如果只有原来的 `%LOCALAPPDATA%\TideIsland`，程序会继续在原处读写资料。
- 如果两个目录同时存在，程序会停止，请用 `--data-dir` 明确选择。例如在解压目录的 PowerShell 中运行 `& ".\Paomian Island.exe" --data-dir "$env:LOCALAPPDATA\TideIsland"` 可打开旧资料。程序不合并或覆盖任何一方，也不会因默认目录无法访问而改用空资料。
- 设置、历史、草稿、饼干及订阅号保存在本机。饼干和订阅号由当前 Windows 用户的 DPAPI 加密；复制文件夹并不能使它们在另一台电脑或另一个 Windows 账号下可用。缓存可单独更换位置，清理缓存不会删除个人记录。
- 浏览、订阅和发言按需向 X 岛发送请求；GitHub 仅用于更新。没有统计或遥测。
- 解压版默认**不会**把资料放在 EXE 旁边。要使用便携资料，在解压目录打开 PowerShell：

  ```powershell
  & ".\Paomian Island.exe" --data-dir "$PWD\profile"
  ```

  也可以传入其他绝对路径。显式指定后，只使用该目录，不参与默认目录选择。同一资料目录同时只能有一个实例，不同的显式资料目录可分别运行。使用便携资料时，请勿随程序目录一起删除 `profile`。

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
