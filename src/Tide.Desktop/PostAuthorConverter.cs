using System.Globalization;
using System.Windows.Data;

namespace Tide.Desktop;

/// <summary>作者名称与列表沿用同一过滤规则，且随防剧透开关即时更新。</summary>
public sealed class PostAuthorConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        PostCard.Optional(values.ElementAtOrDefault(0) as string ?? "", "无名氏", values.ElementAtOrDefault(1) is true);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
