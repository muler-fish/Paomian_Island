namespace Paomian.Core;

public sealed partial class LocalStore
{
    // 仅剪贴板图片使用资料内的相对标识；手选文件和历史草稿仍引用外部原件。
    private const string DraftImageFolder = "draft-images/";
    private readonly HashSet<string> _ownedDraftImages = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsClipboardImage(string reference) => reference.Length == DraftImageFolder.Length + 32 + 4 &&
        reference.StartsWith(DraftImageFolder, StringComparison.Ordinal) && reference.EndsWith(".png", StringComparison.Ordinal) &&
        Guid.TryParseExact(reference.AsSpan(DraftImageFolder.Length, 32), "N", out _);

    public string ResolveDraftImage(string reference) => IsClipboardImage(reference) ? Path.GetFullPath(Path.Combine(Root, reference)) : reference;

    // 只登记草稿使用过的内部引用。保存失败后可以重试，正常保存不额外读取整份状态文件。
    private void RememberDraftImages(UserState state)
    {
        foreach (var draft in state.Drafts)
            if (draft.ImageFile is { Length: > 0 } reference && IsClipboardImage(reference)) _ownedDraftImages.Add(reference);
    }

    private void ReleaseUnusedDraftImages(UserState saved)
    {
        if (_ownedDraftImages.Count == 0) return;
        var paths = saved.Drafts.Select(d => d.ImageFile).Where(p => !string.IsNullOrEmpty(p))
            .Select(ResolveDraftImage).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _ownedDraftImages.RemoveWhere(reference => !paths.Contains(ResolveDraftImage(reference)) && DiscardClipboardImage(reference));
    }

    public string WriteClipboardImage(byte[] png)
    {
        if (png.Length > PostImageFile.MaxBytes || XIslandApi.DetectImage(png) != "image/png") throw new InvalidDataException("无法保存剪贴板图片。");
        var folder = Directory.CreateDirectory(Path.Combine(Root, DraftImageFolder));
        if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("草稿图片目录不能是链接。");
        var reference = $"{DraftImageFolder}{Guid.NewGuid():N}.png";
        var path = ResolveDraftImage(reference);
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try { using (file) file.Write(png); }
        catch { File.Delete(path); throw; }
        return reference;
    }

    // 显式取消，或成功保存不再引用的草稿后回收自有图片。
    public bool DiscardClipboardImage(string reference)
    {
        if (!IsClipboardImage(reference)) return false;
        try
        {
            if (File.GetAttributes(Path.Combine(Root, DraftImageFolder)).HasFlag(FileAttributes.ReparsePoint)) return false;
            File.Delete(ResolveDraftImage(reference)); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
