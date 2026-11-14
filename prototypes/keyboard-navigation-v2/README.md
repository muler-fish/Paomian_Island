# 泡面岛键盘交互 V2 原型

独立的 .NET 10 WPF 实验程序。使用内置模拟版块、串、引用和图片，输入只保留在当前会话。

在仓库根目录运行，需 Windows 和 .NET 10 SDK。

```powershell
dotnet run --project prototypes/keyboard-navigation-v2/KeyboardNavigationV2.csproj -c Release
```

启动时显示「沿河步道随手记」，焦点在串列表。当前栏的标题和下方细线使用强调色。每一栏的滚动位置、左侧树选中项和已打开的串在切栏时保留。

| 位置 | 键盘操作 |
| --- | --- |
| 主窗口三栏 | `Tab` 按「版块 → 串列表 → 阅读区 → 版块」循环，`Shift+Tab` 反向循环。 |
| 左侧版块树 | `↑`／`↓` 选择可见行。`→` 展开或进入第一个子项，`←` 收起或回到父项。`Enter` 打开版块或功能窗，也可展开／收起分组。 |
| 串列表与阅读区 | `↑`／`↓` 连续滚动，`PgUp`／`PgDn` 翻动约一屏。按 `F` 显示当前栏的可见串、引用、链接、图片及可用工具按钮。输入完整标签执行，`Backspace` 撤回一个字母，`Esc` 取消。启动时按 `F`，再按「沿河步道随手记」的标签即可进入右栏。 |
| 已聚焦的按钮 | `Enter` 或 `Space` 执行控件。主窗工具还可以通过 `F` 标签和 WPF 按钮的无障碍入口执行。 |
| 串号框 | `Ctrl+L` 聚焦并全选。输入 `710001` 等串号后按 `Enter` 跳转。`Tab` 到「跳转」按钮，`Shift+Tab` 返回输入框，`Esc` 回此前焦点。 |
| 引用、链接和图片预览 | 初始焦点在预览正文。仍可用方向键、翻页和 `F`。`Tab`／`Shift+Tab` 在当前窗口内遍历，`Esc` 先取消字母提示，再关闭当前窗口。 |
| 设置和模拟发言 | `Tab`／`Shift+Tab` 由 WPF 遍历表单，编辑快捷键和输入法也由 WPF 处理。在编辑框中可 `Tab` 到「关闭」并按 `Enter`，也可使用 Windows 的 `Alt+F4`。 |

阅读 No.710001 时，到 No.710102 试密集标签，到 No.710103 的「08 · 亲水平台」试长文中段的弹窗恢复。依次打开引用 No.720101、No.720201、No.720301，再逐层按 `Esc` 返回。用左栏「设置」切换浅色或深色。两条分栏线可用鼠标拖动。

同一项目可在 WPF 窗口中自动复查，生成滚动数据和截图。专项使用 WPF 输入路由、控件调用和 DPI 消息，实际硬件输入可按上表体验。

```powershell
dotnet run --project prototypes/keyboard-navigation-v2/KeyboardNavigationV2.csproj -c Release -- --verify artifacts/keyboard-navigation-v2/check-tab
```
