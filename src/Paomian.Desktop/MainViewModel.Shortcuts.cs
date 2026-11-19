namespace Paomian.Desktop;

public sealed partial class MainViewModel
{
    internal AppShortcuts Shortcuts { get; }
    public string JumpPlaceholder => Shortcuts.Hint("串号或链接", ShortcutId.Jump);
    public string RefreshListHint => Shortcuts.Hint("刷新列表", ShortcutId.RefreshList);
    public string RefreshThreadHint => Shortcuts.Hint(Shortcuts.Hint("刷新正文", ShortcutId.RefreshThread), ShortcutId.RefreshList);
    public string SubscriptionShortcutHint => Shortcuts.Hint(SubscriptionLabel, ShortcutId.Subscription) +
        Environment.NewLine + (!CurrentSubscriptionKnown ? "右键核对后订阅或重新订阅" : IsSubscribed ? "右键重新订阅" : "右键也可订阅");
    public string BackShortcut => Shortcuts.Label(ShortcutId.Back);
    public string ForwardShortcut => Shortcuts.Label(ShortcutId.Forward);

    private void ShortcutsChanged()
    {
        foreach (var name in new[] { nameof(JumpPlaceholder), nameof(RefreshListHint), nameof(RefreshThreadHint),
            nameof(SubscriptionShortcutHint), nameof(BackLabel), nameof(ForwardLabel), nameof(BackShortcut), nameof(ForwardShortcut) })
            Changed(name);
        Save();
    }
}
