# 泡面岛键盘交互 V2 原型

独立的 .NET 10 WPF 实验程序。使用内置模拟版块、串、引用和图片，输入只保留在当前会话。

在仓库根目录运行，需 Windows 和 .NET 10 SDK。

```powershell
dotnet run --project prototypes/keyboard-navigation-v2/KeyboardNavigationV2.csproj -c Release
```

启动时显示「沿河步道随手记」，焦点在串列表。F6／Shift+F6 切栏。↑ ↓ 连续滚动，PgUp／PgDn 翻屏。F8 在当前位置切换方案。

| 方案 | 在串列表、正文或引用预览的阅读视口中操作 |
| --- | --- |
| A | F 显示候选标签，按完整字母执行。Backspace 更正前缀，Esc 取消。 |
| B | Enter 选择第一个可见目标，↑ ↓ 移动标记，Enter 执行，Esc 退出。 |
| 两者 | Tab／Shift+Tab 访问控件，Enter／Space 按下聚焦的按钮。预览的 Esc 先取消当前选择，再关当前窗。编辑框的 Esc 由原生输入处理，可 Tab 到「关闭」。 |

打开串 No.710001 后，到 No.710102 比较密集引用，到 No.710103 的「08 · 亲水平台」比较长文中段的选择和返回。引用 No.720101 可继续打开 No.720201、No.720301。

同一项目可在 WPF 窗口中自动复查，生成滚动数据和截图。使用自己指定的输出目录。

```powershell
dotnet run --project prototypes/keyboard-navigation-v2/KeyboardNavigationV2.csproj -c Release -- --verify artifacts/keyboard-navigation-v2/check
```
