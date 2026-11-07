using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>用真实的系统输入法（微软拼音）向隔离测试窗口发送按键，核对换行后上屏文字的位置。
/// 只在显式传入 --real-ime 时运行：会占用键盘；每个按键前确认测试窗口在前台，否则立即停止。</summary>
internal static class RealImeProbe
{
    // 脚本：字母为拼音按键，'_' 为空格（选第一个候选上屏），'|' 为 Enter。
    private sealed record Case(string Name, string Initial, int Caret, Action<EmojiEditor>? Prepare, string Script, string Before, string After);

    internal static async Task RunAsync(MainWindow owner, string output)
    {
        var log = new List<string>(); var failures = new List<string>();
        var editor = new EmojiEditor { FontSize = 16, MinHeight = 240 };
        var window = new Window { Width = 560, Height = 360, Content = editor, Topmost = true, Title = "输入法检查（请勿操作键盘）", WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var previousLanguage = InputLanguageManager.Current.CurrentInputLanguage;
        var cases = new[]
        {
            new Case("末尾连续换行", "", 0, null, "nihao_|shijie_||ceshi_", "", ""),
            new Case("中间换行", "第一行第二行", 3, null, "|zhong_", "第一行\r\n", "第二行"),
            new Case("恢复草稿后换行", "甲\r\n乙", -1, null, "|bing_", "甲\r\n乙\r\n", ""),
            new Case("粘贴多行后输入", "开头", -1, e => { Clipboard.SetText("一\r\n二\r\n"); e.Paste(); }, "ding_", "开头一\r\n二\r\n", ""),
            new Case("插入多行颜文字后输入", "左右", 1, e => { const string art = "  /\\_/\\\r\n ( o.o )\r\n"; var start = e.SelectionStart; e.SelectedText = art; e.Select(start + art.Length, 0); }, "wu_", "左  /\\_/\\\r\n ( o.o )\r\n", "右"),
        };
        try
        {
            window.Show(); window.Activate(); await Task.Delay(400);
            var handle = new WindowInteropHelper(window).Handle;
            // 系统的前台锁定会拒绝直接切换；临时挂到当前前台线程的输入队列上再切换，不发送任何按键。
            var foreground = GetForegroundWindow();
            var foreignThread = GetWindowThreadProcessId(foreground, IntPtr.Zero); var ownThread = GetCurrentThreadId();
            if (foreignThread != ownThread) AttachThreadInput(ownThread, foreignThread, true);
            try { BringWindowToTop(handle); SetForegroundWindow(handle); }
            finally { if (foreignThread != ownThread) AttachThreadInput(ownThread, foreignThread, false); }
            window.Activate(); editor.Focus(); Keyboard.Focus(editor); await Task.Delay(300);
            InputLanguageManager.SetInputLanguage(editor, new CultureInfo("zh-CN"));
            InputLanguageManager.Current.CurrentInputLanguage = new CultureInfo("zh-CN"); await Task.Delay(300);
            InputMethod.Current.ImeState = InputMethodState.On;
            InputMethod.Current.ImeConversionMode = ImeConversionModeValues.Native | ImeConversionModeValues.Symbol; await Task.Delay(300);
            log.Add($"language={InputLanguageManager.Current.CurrentInputLanguage} ime={InputMethod.Current.ImeState} mode={InputMethod.Current.ImeConversionMode}");
            foreach (var test in cases)
            {
                editor.Text = test.Initial; editor.CaretIndex = test.Caret < 0 ? editor.Text.Length : test.Caret; await Task.Delay(200);
                test.Prepare?.Invoke(editor); await Task.Delay(200);
                foreach (var step in test.Script)
                {
                    if (GetForegroundWindow() != handle) throw new InvalidOperationException("测试窗口不在前台，已停止发送按键。");
                    Press(step switch { '_' => (ushort)0x20, '|' => (ushort)0x0D, _ => (ushort)char.ToUpperInvariant(step) });
                    await Task.Delay(140);
                }
                await Task.Delay(500);
                var text = editor.Text; var caret = editor.CaretIndex;
                var committed = text.Length >= test.Before.Length + test.After.Length ? text[test.Before.Length..(text.Length - test.After.Length)] : "";
                // 拼音必须全部上屏为中文，落在预期的两段之间，光标停在上屏文字之后。
                var ok = text.StartsWith(test.Before, StringComparison.Ordinal) && text.EndsWith(test.After, StringComparison.Ordinal)
                    && committed.Length > 0 && !committed.Any(c => c is >= 'a' and <= 'z' or '\'')
                    && caret == text.Length - test.After.Length;
                log.Add($"{(ok ? "PASS" : "FAIL")} {test.Name}: text={Escape(text)} caret={caret} blocks={editor.Document.Blocks.Count}");
                if (!ok) failures.Add($"{test.Name}: text={Escape(text)} caret={caret}");
            }
        }
        finally
        {
            InputLanguageManager.Current.CurrentInputLanguage = previousLanguage;
            window.Close();
            File.WriteAllLines(Path.Combine(output, "real-ime.txt"), log);
        }
        if (failures.Count > 0) throw new InvalidOperationException("真实输入法检查失败：" + string.Join("；", failures));
        Console.WriteLine($"PASS: real Microsoft Pinyin input ({cases.Length} cases): enter at end/middle, restored draft, multi-line paste, multi-line emoticon.");
    }

    private static void Press(ushort vk)
    {
        var inputs = new[]
        {
            new Input { Type = 1, Keyboard = new KeyboardInput { Vk = vk } },
            new Input { Type = 1, Keyboard = new KeyboardInput { Vk = vk, Flags = 2 } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }
    private static string Escape(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");

    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Keyboard; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, IntPtr process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool value);
}
