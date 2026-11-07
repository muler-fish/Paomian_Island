using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>发言正文输入：经 WPF 输入管线模拟逐字输入、Enter、粘贴和输入法组合，核对原文与光标位置。只用模拟接口，不发送。</summary>
internal static class ComposerInputChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var store = new LocalStore(Path.Combine(output, "input-profile"));
        using var vm = new MainViewModel(UiRefinementChecks.CreateApi(), store);
        vm.State.Forums = [new(4, "综合版1", "综合")];
        vm.State.Cookies = [new("test", "测试饼干", SecretProtection.Protect("synthetic-cookie"))]; vm.State.ActiveCookieId = "test";
        var composer = new ComposerWindow(vm, 0) { Owner = owner, ShowInTaskbar = false };
        composer.Show(); await Pause();
        var results = new Dictionary<string, bool>();
        try
        {
            var editor = UiRefinementChecks.Descendants<EmojiEditor>(composer).Single(e => AutomationProperties.GetName(e) == "发言正文");
            editor.Focus(); await Pause();
            async Task Check(string name, string text, int caret)
            {
                await Pause();
                var ok = editor.Text == text && editor.CaretIndex == caret;
                results[name] = ok;
                Require(ok, $"{name}: text=[{Escape(editor.Text)}] caret={editor.CaretIndex}，期望 [{Escape(text)}] caret={caret}");
            }

            // 1. 输入 → Enter → 继续输入
            await Type(editor, "第一行"); await Enter(editor); await Type(editor, "第二行");
            await Check("enter-then-type", "第一行\r\n第二行", 8);
            // 2. 连续多个换行
            await Enter(editor); await Enter(editor); await Enter(editor); await Type(editor, "末");
            await Check("multiple-enters", "第一行\r\n第二行\r\n\r\n\r\n末", 15);
            // 3. 在正文中间插入换行
            editor.Text = "前后"; editor.CaretIndex = 1; await Pause();
            await Enter(editor); await Type(editor, "中");
            await Check("enter-in-middle", "前\r\n中后", 4);
            // 4. 输入法组合：组合期间的确认键以 ImeProcessed 送达，不能变成换行；候选上屏后光标在上屏文字之后。
            editor.Text = "拼音："; editor.CaretIndex = 3; await Pause();
            var composition = new TextComposition(InputManager.Current, editor, "中文", TextCompositionAutoComplete.Off);
            TextCompositionManager.StartComposition(composition); await Pause();
            await Key(editor, System.Windows.Input.Key.ImeProcessed);
            TextCompositionManager.CompleteComposition(composition);
            await Check("ime-commit", "拼音：中文", 5);
            await Enter(editor); await Type(editor, "下一行");
            await Check("ime-then-enter", "拼音：中文\r\n下一行", 10);
            // 4b. 组合被取消（不会收到 PreviewTextInput）后按 Enter：仍由编辑器处理，光标在新行。
            TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, editor, "x", TextCompositionAutoComplete.Off)); await Pause();
            await Enter(editor); await Type(editor, "续");
            await Check("cancelled-ime-then-enter", "拼音：中文\r\n下一行\r\n续", 13);
            // Enter 产生段落，不在段落末尾留下后面没有文字的 LineBreak：TSF 输入法会把组合文字插到这种 LineBreak 之前。
            Require(editor.Document.Blocks.OfType<Paragraph>().All(p => p.Inlines.LastInline is not LineBreak), "段落末尾留下了 LineBreak");
            // 5. 粘贴多行（结尾带换行，会生成多个段落）
            editor.Text = "开头"; editor.CaretIndex = 2; await Pause();
            Clipboard.SetText("甲\r\n乙\r\n"); editor.Paste(); await Pause();
            await Type(editor, "丙");
            await Check("paste-multiline", "开头甲\r\n乙\r\n丙", 9);
            await Enter(editor); await Type(editor, "丁");
            await Check("paste-then-enter", "开头甲\r\n乙\r\n丙\r\n丁", 12);
            // 6. 选中文字后替换
            editor.Text = "abcdef"; editor.Select(2, 2); await Pause();
            await Type(editor, "X");
            await Check("replace-selection", "abXef", 3);
            // 7. 工具栏插入：颜文字（多行、结尾换行）、防剧透、骰子，插入后继续输入都在插入内容之后。
            editor.Text = "左右"; editor.CaretIndex = 1; await Pause();
            await Invoke(Tool(composer, "颜文字")); var art = composer.Emoticons;
            vm.State.CustomEmoticons = [new("art", "  /\\_/\\\n ( o.o )\n", "猫")];
            await Invoke(Tool(composer, "颜文字")); await Invoke(Tool(composer, "颜文字"));
            var cat = UiRefinementChecks.Descendants<Button>(art.Popup.Child).First(b => b.Tag is EmoticonEntry { CustomId: "art" } || (b.Tag is EmoticonEntry e && e.Text.StartsWith("  /")));
            await Invoke(cat); await Type(editor, "!");
            await Check("emoticon-insert", "左  /\\_/\\\r\n ( o.o )\r\n!右", 21);
            editor.Text = "剧透内容"; editor.Select(0, 2); await Pause();
            await Invoke(Tool(composer, "防剧透")); await Type(editor, "。");
            await Check("spoiler-selection", "[h]剧透[/h]。内容", 10);
            editor.Text = "前"; editor.CaretIndex = 1; await Pause();
            await Invoke(Tool(composer, "防剧透")); await Type(editor, "秘");
            await Check("spoiler-empty", "前[h]秘[/h]", 5);
            editor.CaretIndex = editor.Text.Length; await Pause();
            await Invoke(Tool(composer, "骰子")); await Type(editor, "后");
            await Check("dice", "前[h]秘[/h][1,100]后", 17);
            // 8. 撤销与重做
            editor.Text = ""; await Pause();
            await Type(editor, "撤"); await Enter(editor); await Type(editor, "销");
            var typed = editor.Text;
            editor.Undo(); await Pause(); var undone = editor.Text;
            editor.Redo(); await Pause();
            results["undo-redo"] = undone != typed && editor.Text == typed;
            Require(results["undo-redo"], $"撤销/重做: undone=[{Escape(undone)}] redo=[{Escape(editor.Text)}]");
            editor.CaretIndex = editor.Text.Length; await Type(editor, "后"); await Check("type-after-redo", "撤\r\n销后", 5);
            // 9. 草稿自动保存（600 ms）后继续输入
            editor.Text = "草稿"; editor.CaretIndex = 2; await Pause();
            await Enter(editor); await Type(editor, "保存前"); await Task.Delay(800);
            Require(store.Load().Drafts.Any(d => d.Content == "草稿\r\n保存前"), "自动保存没有写入草稿");
            await Type(editor, "之后"); await Enter(editor); await Type(editor, "再写");
            await Check("after-autosave", "草稿\r\n保存前之后\r\n再写", 13);
            await Task.Delay(800);
            Require(store.Load().Drafts.Any(d => d.Content == "草稿\r\n保存前之后\r\n再写"), "继续输入后的草稿没有保存");
            results["autosave"] = true;
            capture(composer, Path.Combine(output, "composer-input.png"));
            File.WriteAllText(Path.Combine(output, "composer-input-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: composer input ({results.Count} checks): enter/multi-enter/middle, IME commit and cancelled composition, multi-line paste, selection replace, emoticon/spoiler/dice insert, undo/redo, autosave.");
        }
        finally { composer.Close(); }
    }

    internal static async Task Type(EmojiEditor editor, string text)
    {
        foreach (var ch in text) { TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, editor, ch.ToString())); await Task.Delay(15); }
        await Pause();
    }
    internal static Task Enter(EmojiEditor editor) => Key(editor, System.Windows.Input.Key.Enter);
    // 先送 PreviewKeyDown；编辑器未处理时再送 KeyDown，交给 RichTextBox 的默认命令，与真实按键一致。
    internal static async Task Key(EmojiEditor editor, Key key)
    {
        var source = PresentationSource.FromVisual(editor)!;
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        editor.RaiseEvent(preview);
        if (!preview.Handled) editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
        await Pause();
    }
    internal static Button Tool(Window window, string name) => UiRefinementChecks.Descendants<Button>(window).Single(b => AutomationProperties.GetName(b) == name);
    internal static async Task Invoke(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await Pause(); }
    private static string Escape(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");
    private static Task Pause() => Task.Delay(80);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
