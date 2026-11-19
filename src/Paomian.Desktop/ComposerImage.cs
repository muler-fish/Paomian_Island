using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Paomian.Core;

namespace Paomian.Desktop;

internal sealed record ComposerImage(string Reference, BitmapSource? Thumbnail = null, string? Hash = null)
{
    internal string Name => LocalStore.IsClipboardImage(Reference) ? "剪贴板图片.png" : Path.GetFileName(Reference);
}

/// <summary>在 UI 线程取得剪贴板快照；所有入口随后共用本地解码和草稿附件处理。</summary>
internal sealed record ComposerImageInput(string? FileName = null, byte[]? Png = null, BitmapSource? Bitmap = null)
{
    internal const string Limits = "JPG、PNG 或 GIF · 最多 1 张 · 不超过 5 MB";
    internal const string Occupied = "每次只能添加一张图片，请先移除已有附图。";
    private const string Formats = "请选择 JPG、PNG 或 GIF 图片。";
    private static readonly string[] PngFormats = ["PNG", "image/png"];

    internal static string FileFromDrop(IDataObject data) => data.GetData(DataFormats.FileDrop, false) is string[] { Length: 1 } files
        ? files[0] : throw new InvalidDataException("请一次选择一张图片，不要拖入文件夹或多个文件。");

    private static string FileMime(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("请选择本机上的图片文件。");
        if (Directory.Exists(path)) throw new InvalidDataException("不能添加文件夹，请选择其中的一张图片。");
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpeg" or ".jpg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif",
            _ => throw new InvalidDataException(Formats)
        };
    }

    private static void CheckType(string expected, ReadOnlySpan<byte> bytes)
    {
        if (XIslandApi.DetectImage(bytes) != expected)
            throw new InvalidDataException("图片内容与扩展名不符，或文件已损坏。");
    }

    internal static void CheckFile(string path)
    {
        var mime = FileMime(path);
        using var stream = File.OpenRead(path);
        if (stream.Length > PostImageFile.MaxBytes) throw new InvalidDataException(PostImageFile.TooLarge);
        Span<byte> head = stackalloc byte[8];
        var count = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        CheckType(mime, head[..count]);
    }

    private static bool HasImageFormat(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop, false) ||
        PngFormats.Any(f => data.GetDataPresent(f, false)) || data.GetDataPresent(DataFormats.Bitmap);

    internal static ComposerImageInput? FromClipboard(IDataObject? data)
    {
        if (data is null || !HasImageFormat(data)) return null;
        var fileDrop = data.GetDataPresent(DataFormats.FileDrop, false);
        // 文件管理器可同时公开 CF_HDROP 和文本路径；只有文本确实是同一文件时才按图片附件处理。
        // 页面复制的文字＋图片则优先保留文字，不吞掉链接、说明或 Unicode。
        if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string { Length: > 0 } text)
        {
            if (!fileDrop || data.GetData(DataFormats.FileDrop, false) is not string[] { Length: 1 } files || !TextMatchesFile(text, files[0])) return null;
            var path = files[0];
            // 只有可用图片的路径才是文件附件。复制了非图片时，其附带的文字仍能照常粘贴。
            try { CheckFile(path); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
            return new(FileName: path);
        }
        if (fileDrop)
        {
            var path = FileFromDrop(data);
            CheckFile(path);
            return new(FileName: path);
        }
        foreach (var format in PngFormats.Where(f => data.GetDataPresent(f, false)))
        {
            var value = data.GetData(format, false);
            if (value is byte[] bytes) return bytes.Length <= PostImageFile.MaxBytes ? new(Png: bytes) : throw new InvalidDataException(PostImageFile.TooLarge);
            if (value is Stream stream)
            {
                if (stream.CanSeek) { if (stream.Length > PostImageFile.MaxBytes) throw new InvalidDataException(PostImageFile.TooLarge); stream.Position = 0; }
                using var copy = new LimitedPngStream(); stream.CopyTo(copy); return new(Png: copy.ToArray());
            }
        }
        if (data.GetData(DataFormats.Bitmap) is not BitmapSource bitmap) throw new InvalidDataException("无法读取剪贴板图片，请重新复制。");
        CheckFullRaster(bitmap);
        return new(Bitmap: bitmap.IsFrozen ? bitmap : (BitmapSource)bitmap.GetAsFrozen());
    }

    private static bool TextMatchesFile(string text, string path)
    {
        var value = text.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
        return string.Equals(value, path, StringComparison.OrdinalIgnoreCase);
    }

    internal Task<ComposerImage> PrepareAsync(LocalStore store, CancellationToken ct, string? restore = null) => Task.Run(async () =>
    {
        byte[] bytes;
        if (FileName is not null)
        {
            // 恢复早期草稿时按真实内容解码，不因旧版允许的文件名而丢弃原引用。
            var expected = restore is null ? FileMime(FileName) : null;
            bytes = await PostImageFile.ReadAsync(FileName, ct);
            if (expected is not null) CheckType(expected, bytes);
        }
        else if (Bitmap is not null)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Bitmap));
            using var output = new LimitedPngStream();
            try { encoder.Save(output); } catch (Exception) when (output.Exceeded) { throw new InvalidDataException(PostImageFile.TooLarge); }
            bytes = output.ToArray();
        }
        else bytes = Png ?? throw new InvalidDataException("没有可读取的图片。");
        ct.ThrowIfCancellationRequested();
        var mime = XIslandApi.DetectImage(bytes);
        if (mime is null || FileName is null && mime != "image/png") throw new InvalidDataException(Formats);
        if (mime == "image/gif") ImageService.CheckGifCanvas(bytes);
        using var source = new MemoryStream(bytes, writable: false);
        var frame = BitmapDecoder.Create(source, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        // JPG/PNG 直接按目标尺寸解码。GIF 则可能先展开整个画布。
        if (mime == "image/gif") CheckFullRaster(frame);
        const int edge = 256;
        var longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
        int Scaled(int dimension) => longest <= edge ? 0 : Math.Max(1, (int)Math.Round((double)edge * dimension / longest));
        var thumbnail = ImageService.Decode(bytes, Scaled(frame.PixelWidth), Scaled(frame.PixelHeight));
        var hash = PostImageFile.Hash(bytes);
        ct.ThrowIfCancellationRequested();
        return new ComposerImage(restore ?? FileName ?? store.WriteClipboardImage(bytes), thumbnail, hash);
    }, ct);

    private static void CheckFullRaster(BitmapSource image)
    {
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0 || (long)image.PixelWidth * image.PixelHeight > ImageService.MaxFullRasterPixels)
            throw new InvalidDataException("图片过大，无法在本机完整展开。可以先保存为 JPG 或 PNG，再选择文件添加。");
    }

    internal static string Error(Exception ex) => ex switch
    {
        InvalidDataException => ex.Message,
        FileNotFoundException => "找不到这张图片，请移除后重新添加。",
        UnauthorizedAccessException => "没有权限读取图片或保存草稿。",
        IOException => "无法读写图片，请检查文件和资料目录后重试。",
        FormatException or NotSupportedException or ExternalException or ArgumentException => "无法解码图片，请检查文件是否完整。",
        _ => "无法添加图片，原有草稿未更改。"
    };

    private sealed class LimitedPngStream : MemoryStream
    {
        internal bool Exceeded { get; private set; }
        private void Limit(long end) { if (end <= PostImageFile.MaxBytes) return; Exceeded = true; throw new InvalidDataException(PostImageFile.TooLarge); }
        public override void SetLength(long value) { Limit(value); base.SetLength(value); }
        public override void Write(byte[] buffer, int offset, int count) { Limit(Position + count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Limit(Position + buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Limit(Position + 1); base.WriteByte(value); }
    }
}
