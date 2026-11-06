<div align="center">

<img src="src/Tide.Desktop/Assets/paomian.png" width="96" alt="泡面岛 Logo">

# 泡面岛 · Paomian Island

**X 岛匿名版的非官方 Windows 客户端**

安静、顺手地刷岛：三栏阅读、连续翻页、订阅、发串，一个窗口里完成。

[下载最新版](https://github.com/muler-fish/Paomian_Island/releases/latest) · [更新记录](CHANGELOG.md) · [反馈问题](https://github.com/muler-fish/Paomian_Island/issues)

</div>

---

## 快速开始

1. 打开 [Releases](https://github.com/muler-fish/Paomian_Island/releases/latest)，下载 `Paomian-Island-版本号-win-x64.zip`。
2. 解压到任意文件夹，双击 `Paomian Island.exe`。

- 系统要求：Windows 10 / 11 x64。
- 不需要安装 .NET，也没有安装程序，解压就能用。不想用了，删掉文件夹即可。
- 只是看串的话不需要饼干；要发言，先在左下角的「饼干管理」里导入饼干。

> 有新版本时，程序会在设置入口提示，可以直接在程序里更新，设置和数据都会保留。

## 它能做什么

**阅读**
- 三栏布局：板块、串列表、正文，栏宽和窗口位置都会记住。
- 串列表滚到底会自动加载下一页，串内往上往下都能续页，也支持跳到指定页和只看 PO。
- 会保存阅读进度，再打开同一个串时回到上次看到的位置。
- 引用可以在小窗里预览，站内链接也能直接预览，还支持防剧透黑条。
- 有浅色和深色主题，正文字号、行间距、字间距都能调，调的时候实时预览。

**看图**
- 独立的看图窗口：缩放、拖动、旋转、保存原图。
- 支持 GIF 动图播放；复制和保存的都是完整原文件。
- 看到最后一张时，会自动尝试加载下一页的图片。

**发言**
- 发串、回应，可以附图，有草稿箱、发送前预览、颜文字、防剧透和骰子。
- 管理多个饼干：给饼干写本机备注、设默认饼干，回应时能看到这个饼干在当前串里用过几次。
- 饼干导入方式：二维码图片、剪贴板图片、userhash、Cookie 头或 JSON。
- 「我的发言」保存在本机发送成功的主题和回复。
- 右键可以举报楼层，内容经过预览确认后才会发到值班室。

**整理**
- 支持多个订阅号，每个可以写备注、随时切换。
- 板块收藏、浏览历史、屏蔽串和屏蔽板块。
- 缓存可以设容量上限和存放位置；访问 X 岛和检查更新可以分别选择走系统代理或直连。
- 检查更新的来源是 GitHub Releases，下载后会核对 SHA-256 校验值。

## 快捷键

| 按键 | 作用 |
| --- | --- |
| `Ctrl+L` | 输入串号或 X 岛链接，按 Enter 打开 |
| `F5` | 刷新串列表 |
| `Ctrl+R` | 刷新正文 |
| `Ctrl+D` | 订阅或取消订阅当前串 |
| `Esc` | 关闭子窗口 |
| 看图时 `←` / `→` | 上一张 / 下一张 |
| 看图时滚轮、`+` / `−` | 缩放 |
| 看图时 `1` / `0` / `R` | 原始大小 / 适应窗口 / 向左旋转 |

## 数据与隐私

- 所有数据只存在你的电脑上，默认放在 `%LOCALAPPDATA%\TideIsland`。
- 饼干和订阅号用 Windows DPAPI 加密，只有当前 Windows 用户能读取。
- 程序只连接 X 岛的服务器（包括图片和公告）和 GitHub（检查更新用），没有统计或遥测。
- 想把资料放在别的地方，比如多开、做成便携版：

  ```powershell
  & ".\Paomian Island.exe" --data-dir "D:\泡面岛资料"
  ```

  同一个资料目录同时只能运行一个泡面岛。

## 参与开发

需要 Windows 和 [.NET 10 SDK](https://dotnet.microsoft.com/download)：

```powershell
git clone https://github.com/muler-fish/Paomian_Island.git
cd Paomian_Island
dotnet build Tide.slnx -c Release
dotnet run --project src/Tide.Desktop -- --data-dir "artifacts/dev-profile"
```

| 目录 | 内容 |
| --- | --- |
| `src/Tide.Core` | X 岛接口、数据模型、缓存、本地存储、更新 |
| `src/Tide.Desktop` | WPF 界面（WPF UI）、视图模型、主题与交互 |
| `tests` | 核心测试和按功能划分的桌面专项检查 |
| `tools` | 构建、打包、图标和颜文字资源脚本 |

发布包用 `pwsh -File tools/Build.ps1 -SelfContained` 生成，当前的工作项见 [TODO.md](TODO.md)。

欢迎提交 Issue 和 PR。报告问题时，请写上版本号、Windows 版本、显示缩放比例和复现步骤，**截图前请遮住饼干和订阅号**。改动比较大的话，建议先开个 Issue 讨论一下。

## 致谢与许可

- 交互参考了记忆中老 A 岛的一款 Windows (不知道是不是第三方的)客户端，接口参考了[雾岛](https://github.com/123Duo3/XDNMB)和[霞岛](https://github.com/orzogc/xdnmb)。
- 用到了 [WPF UI](https://github.com/lepoco/wpfui)、ZXing.Net、Emoji.Wpf、XamlAnimatedGif 等开源组件，它们各自的许可放在 [licenses](licenses) 目录。
- 泡面岛以 [GPL-3.0](LICENSE) 许可证开源。

泡面岛不是 X 岛的官方软件。
