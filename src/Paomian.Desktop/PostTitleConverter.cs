using System.Globalization;
using System.Windows.Data;

namespace Paomian.Desktop;

/// <summary>帖子标题沿用列表和主楼的占位值、防剧透规则。</summary>
public sealed class PostTitleConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        PostCard.Optional(values.ElementAtOrDefault(0) as string ?? "", "无标题", values.ElementAtOrDefault(1) is true);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
