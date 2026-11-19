using System.Text.Json;
using System.Text.RegularExpressions;

namespace Paomian.Desktop;

/// <summary>按 Unicode 的表情呈现规则识别；普通箭头等默认文本符号需要 VS16 才转为彩色。</summary>
internal static class EmojiSequences
{
    private static readonly Regex Pattern = CreatePattern();
    private static Regex CreatePattern()
    {
        using var data = typeof(EmojiSequences).Assembly.GetManifestResourceStream("Paomian.EmojiSequences.json")!;
        var sequences = JsonSerializer.Deserialize<string[]>(data)!;
        return new Regex(string.Join("|", sequences.Select(Regex.Escape)), RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    internal static Match[] Matches(string text) => Pattern.Matches(text).Cast<Match>()
        // VS15 明确要求文本外观，即使前面的字符默认是 emoji 也不转换。
        .Where(m => m.Index + m.Length == text.Length || text[m.Index + m.Length] != '\ufe0e').ToArray();
}
