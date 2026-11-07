using System.IO;
using System.Text.Json;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>内置或自定义颜文字；Text 是插入用的完整原文，Display 只用于列表显示。</summary>
internal sealed record EmoticonEntry(string Text, string Name, string Source)
{
    // 自定义条目的本地 Id；内置条目为空。
    public string CustomId { get; init; } = "";
    public bool IsCustom => CustomId.Length > 0;
    // 多行或较长的图案在列表里只显示简称，完整内容放到预览中。
    public bool IsLarge => Text.Contains('\n') || Text.Length > 24;
    public string Display => Name.Length > 0 ? Name : IsLarge ? Excerpt(Text) : Text;
    // 纯 ASCII 的多行图案依赖等宽排列；含中日文或全角字符的图案沿用正文字体，避免改变原有对齐。
    public bool IsAsciiArt => Text.Contains('\n') && Text.All(c => c < 0x80);

    // 取第一行有内容的文字，最多 16 个字符；只影响列表显示，不改动原文。
    internal static string Excerpt(string text)
    {
        var line = text.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.Trim().Length > 0) ?? text;
        var trimmed = line.Trim();
        var lines = text.Count(c => c == '\n') + 1;
        var head = trimmed.Length > 16 ? trimmed[..16] + "…" : trimmed;
        return lines > 1 ? $"{head}（{lines} 行）" : head;
    }
}

internal static class EmoticonCatalog
{
    public const int MaxLength = 10000;
    public static IReadOnlyList<EmoticonEntry> Entries { get; } = Load();

    /// <summary>自定义分类：按添加顺序排列。</summary>
    public static List<EmoticonEntry> Custom(UserState state) =>
        state.CustomEmoticons.Select(c => new EmoticonEntry(c.Text, c.Name, "自定义") { CustomId = c.Id }).ToList();

    /// <summary>全部分类：未隐藏的内置条目在前，自定义条目在后。</summary>
    public static List<EmoticonEntry> Visible(UserState state)
    {
        var hidden = state.HiddenEmoticons.ToHashSet(StringComparer.Ordinal);
        return Entries.Where(e => !hidden.Contains(e.Text)).Concat(Custom(state)).ToList();
    }

    public static List<EmoticonEntry> Hidden(UserState state)
    {
        var hidden = state.HiddenEmoticons.ToHashSet(StringComparer.Ordinal);
        return Entries.Where(e => hidden.Contains(e.Text)).ToList();
    }

    // 最近使用按原文记录；只显示当前仍可见的条目，已删除或隐藏的不会出现。
    public static List<EmoticonEntry> Recent(UserState state)
    {
        var visible = Visible(state);
        return state.RecentEmoticons.Distinct().Select(text => visible.FirstOrDefault(e => e.Text == text)).OfType<EmoticonEntry>().Take(20).ToList();
    }

    public static List<string> Remember(UserState state, string text) =>
        new[] { text }.Concat(state.RecentEmoticons).Distinct().Where(t => Visible(state).Any(e => e.Text == t)).Take(20).ToList();

    /// <summary>新增或修改自定义颜文字；原文只统一换行符，空格、全角空格和缩进全部保留。</summary>
    public static CustomEmoticon Save(UserState state, string? id, string text, string name)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.Trim(' ', '\t', '\n').Length == 0) throw new ArgumentException("请填写颜文字内容。");
        if (text.Length > MaxLength) throw new ArgumentException($"颜文字最多 {MaxLength} 个字符。");
        var existing = id is null ? null : state.CustomEmoticons.Find(c => c.Id == id) ?? throw new ArgumentException("这个自定义颜文字已被删除。");
        if (state.CustomEmoticons.Any(c => c.Id != id && c.Text == text)) throw new ArgumentException("已经有内容相同的自定义颜文字。");
        if (Entries.Any(e => e.Text == text)) throw new ArgumentException("内置颜文字里已有相同内容。");
        var entry = new CustomEmoticon(existing?.Id ?? Guid.NewGuid().ToString("N"), text, LocalStore.EmoticonName(name));
        if (existing is null) state.CustomEmoticons.Add(entry);
        else
        {
            state.CustomEmoticons[state.CustomEmoticons.IndexOf(existing)] = entry;
            // 改了原文时，最近使用跟着指向新内容，保持原来的位置。
            if (existing.Text != text) state.RecentEmoticons = state.RecentEmoticons.Select(t => t == existing.Text ? text : t).Distinct().ToList();
        }
        return entry;
    }

    public static void Delete(UserState state, string id)
    {
        var existing = state.CustomEmoticons.Find(c => c.Id == id);
        if (existing is null) return;
        state.CustomEmoticons.Remove(existing);
        // 同一原文不会再有其他可见条目（新增时已去重），删除后清掉对应的最近使用记录。
        state.RecentEmoticons.RemoveAll(t => t == existing.Text);
    }

    public static void Hide(UserState state, string text)
    {
        if (Entries.Any(e => e.Text == text) && !state.HiddenEmoticons.Contains(text)) state.HiddenEmoticons.Add(text);
    }

    public static void Restore(UserState state, string text) => state.HiddenEmoticons.Remove(text);

    private static IReadOnlyList<EmoticonEntry> Load()
    {
        using var stream = typeof(EmoticonCatalog).Assembly.GetManifestResourceStream("Tide.Emoticons.json")
            ?? throw new InvalidDataException("缺少内置颜文字库。");
        var entries = JsonSerializer.Deserialize<List<EmoticonEntry>>(stream) ?? throw new InvalidDataException("颜文字库为空。");
        return entries.AsReadOnly();
    }
}
