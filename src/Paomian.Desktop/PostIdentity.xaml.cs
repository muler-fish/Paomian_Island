using System.Windows;
using System.Windows.Controls;
using Paomian.Core;

namespace Paomian.Desktop;

/// <summary>列表、正文与引用共用的饼干 / SAGE 标记，换绑帖子时自动更新。</summary>
public partial class PostIdentity : UserControl
{
    public static readonly DependencyProperty PostProperty =
        DependencyProperty.Register(nameof(Post), typeof(Post), typeof(PostIdentity));

    public Post? Post { get => (Post?)GetValue(PostProperty); set => SetValue(PostProperty, value); }
    public static readonly DependencyProperty IsPoProperty =
        DependencyProperty.Register(nameof(IsPo), typeof(bool), typeof(PostIdentity), new PropertyMetadata(false));
    public bool IsPo { get => (bool)GetValue(IsPoProperty); set => SetValue(IsPoProperty, value); }

    public static readonly DependencyProperty ShowSageProperty =
        DependencyProperty.Register(nameof(ShowSage), typeof(bool), typeof(PostIdentity), new PropertyMetadata(true));
    public bool ShowSage { get => (bool)GetValue(ShowSageProperty); set => SetValue(ShowSageProperty, value); }

    public PostIdentity() => InitializeComponent();
}
