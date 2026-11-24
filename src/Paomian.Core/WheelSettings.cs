using System.Text.Json;
using System.Text.Json.Serialization;

namespace Paomian.Core;

public enum WheelCurve { Linear, Quadratic, Cubic, SmoothStartStop }
public enum WheelPreset { Recommended, Short, Fine, Balanced, Fast, Custom }

public sealed record WheelTuning(double Distance = 100, double DurationMs = 192,
    double ImmediateFraction = .01, WheelCurve Curve = WheelCurve.Quadratic)
{
    public static readonly WheelTuning Recommended = new();
    public static readonly WheelTuning Short = new(72, 96, .2);
    public static readonly WheelTuning Fine = new(48, 72, .5);
    public static readonly WheelTuning Balanced = new(56, 72, .5);
    public static readonly WheelTuning Fast = new(64, 48, .7);

    public double NotchDistance(int lines, double viewport) => lines switch
    {
        0 => 0,
        < 0 => viewport,
        _ => Math.Min(viewport, Distance)
    };

    internal WheelTuning Normalize() => new(
        Math.Clamp(Math.Round(Distance), 8, 160), Math.Clamp(Math.Round(DurationMs), 16, 220),
        Math.Round(Math.Clamp(ImmediateFraction, 0, 1), 2), Enum.IsDefined(Curve) ? Curve : WheelCurve.Quadratic);
}

public sealed record WheelSettings(WheelPreset Preset = WheelPreset.Recommended, WheelTuning? Custom = null)
{
    public static readonly WheelSettings Default = new();
    // 老资料明确关闭动画时保留直接滚动；仍使用按 Delta 比例的输入路径。
    public static readonly WheelSettings LegacyDirect = new(WheelPreset.Custom, WheelTuning.Fine with { ImmediateFraction = 1 });

    [JsonIgnore]
    public WheelTuning Effective => Preset switch
    {
        WheelPreset.Short => WheelTuning.Short,
        WheelPreset.Fine => WheelTuning.Fine,
        WheelPreset.Balanced => WheelTuning.Balanced,
        WheelPreset.Fast => WheelTuning.Fast,
        WheelPreset.Custom when Custom != null => Custom,
        _ => WheelTuning.Recommended
    };

    public WheelSettings Select(WheelPreset preset) => new(preset, preset == WheelPreset.Custom ? Custom ?? Effective : Custom);
    internal WheelSettings Normalize() => new(Enum.IsDefined(Preset) ? Preset : WheelPreset.Recommended, Custom?.Normalize());
}

// 只在 UserState 的滚轮字段使用。单项类型错误时，仍可读取同一文件里的草稿与其他资料。
internal sealed class WheelSettingsConverter : JsonConverter<WheelSettings>
{
    public override WheelSettings Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var value = JsonDocument.ParseValue(ref reader);
        try { return value.RootElement.Deserialize<WheelSettings>(options) ?? WheelSettings.Default; }
        catch (JsonException) { return WheelSettings.Default; }
    }

    public override void Write(Utf8JsonWriter writer, WheelSettings value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
