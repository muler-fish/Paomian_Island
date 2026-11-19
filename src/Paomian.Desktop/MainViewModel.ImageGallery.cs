using Paomian.Core;

namespace Paomian.Desktop;

public sealed partial class MainViewModel
{
    internal ImagePageCursor? CreateImageCursor()
    {
        if (_original is null) return null;
        var id = _original.Id; var identity = State.ActiveCookieId; var lastRequestedPage = 0;
        return new(id, _threadPage, _totalPages, _replies.Select(p => p.Id).Append(id), OnlyPo ? _original.UserHash : null,
            async (page, ct) =>
            {
                void CheckIdentity()
                {
                    if (_disposed || State.ActiveCookieId != identity)
                        throw new IslandException("浏览身份已改变，请重新打开看图窗口。");
                }
                CheckIdentity();
                // 复用正文的短期缓存与 API 页码校验，但不写 ReadingPosts、页码输入框或历史。
                // 同页再次请求代表重试，绕过短期缓存，避免反复读到失败时的重复页。
                var refresh = lastRequestedPage == page; lastRequestedPage = page;
                var result = await ReadThreadAsync(id, page, refresh, ct);
                CheckIdentity(); return result;
            });
    }
}
