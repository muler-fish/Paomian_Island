using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Paomian.Core;
using ZXing;

namespace Paomian.Desktop;

/// <summary>只在本机识别二维码并提取饼干；不上传图片，不请求二维码中的地址。</summary>
internal static class CookieImport
{
    internal sealed record Value(string Cookie, string Name);
    public static Value Parse(string text)
    {
        var raw = text.Trim();
        if (raw.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("这是网页链接，请导入官方导出的饼干二维码或完整 userhash。");
        var cookie = ContentText.NormalizeCookie(raw);
        var name = "";
        if (raw.StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("name", out var value) && value.ValueKind == JsonValueKind.String)
                name = (value.GetString() ?? "").Trim();
        }
        return new(cookie, name.Length > 50 ? name[..50] : name);
    }

    public static Task<Value> ReadFileAsync(string path) => Task.Run(() =>
    {
        if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new ArgumentException("二维码图片不能超过 20 MB。");
        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return Decode(frame);
    });

    public static Task<Value> ReadImageAsync(BitmapSource image)
    {
        // 剪贴板对象属于 UI 线程，先冻结副本再交给工作线程，识别时窗口仍可关闭。
        var frozen = image.Clone(); frozen.Freeze();
        return Task.Run(() => Decode(frozen));
    }

    private static Value Decode(BitmapSource image)
    {
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0 || (long)image.PixelWidth * image.PixelHeight > 16_000_000)
            throw new ArgumentException("图片尺寸过大，请裁剪到饼干二维码后再导入。");
        var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
        var stride = pixels.PixelWidth * 4; var bytes = new byte[stride * pixels.PixelHeight];
        pixels.CopyPixels(bytes, stride, 0);
        var reader = new BarcodeReaderGeneric { AutoRotate = true };
        reader.Options.TryHarder = true; reader.Options.TryInverted = true; reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];
        var codes = reader.DecodeMultiple(new RGBLuminanceSource(bytes, pixels.PixelWidth, pixels.PixelHeight, RGBLuminanceSource.BitmapFormat.BGR32));
        if (codes is null || codes.Length == 0) throw new ArgumentException("没有识别到二维码，请使用清晰、完整的饼干二维码图片。");
        if (codes.Length > 1) throw new ArgumentException("图片里有多个二维码，请只保留要导入的那一个。");
        return Parse(codes[0].Text);
    }
}
