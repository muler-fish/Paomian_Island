using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace Tide.Desktop;

/// <summary>提供 WPF 属性变更通知；字段变化后，绑定该属性的控件才会重新取值。</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(property); return true;
    }
    protected void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}

/// <summary>把同步操作包装为 ICommand；条件变化时调用 Refresh 更新按钮是否可用。</summary>
public sealed class ActionCommand(Action<object?> action, Func<bool>? allowed = null) : ICommand
{
    public bool CanExecute(object? parameter) => allowed?.Invoke() ?? true;
    public void Execute(object? parameter) => action(parameter);
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>异步命令包装：同一个命令运行期间禁止重复执行，错误统一交给界面报告。</summary>
public sealed class TaskCommand(Func<object?, Task> action, Action<Exception> onError, Func<bool>? allowed = null, bool allowConcurrent = false) : ICommand
{
    private int _running;
    // 仅由自身管理取消和版本号的导航命令可重入；发送等命令仍禁止并发。
    public bool CanExecute(object? parameter) => (allowConcurrent || _running == 0) && (allowed?.Invoke() ?? true);
    // ICommand.Execute 不能返回 Task，所以在 async void 边界内捕获异常，避免异常逃逸到界面线程。
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running++; Refresh();
        try { await action(parameter); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { onError(ex); }
        finally { _running--; Refresh(); }
    }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>通过 Windows DPAPI 加解密饼干；密文绑定当前 Windows 用户，Base64 仅用于存入 JSON。</summary>
public static class SecretProtection
{
    // DATA_BLOB 的托管映射；原生接口通过长度和指针传递字节缓冲区。
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr ptr);
    public static string Protect(string value) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));
    public static string Unprotect(string value) => Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value), false));
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            // flags=1 表示禁止系统交互提示；未设置“整机范围”标记，因此使用当前用户范围。
            var success = encrypt ? CryptProtectData(ref input, Tide.Core.AppIdentity.ProtectionDescription, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法使用当前 Windows 用户加解密饼干。");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, output.Length); return result;
        }
        finally
        {
            // 两块原生内存来自不同分配器：输入用 FreeHGlobal，DPAPI 输出用 LocalFree。
            // 释放前清零可控的临时字节缓冲区；托管字符串的生命周期仍由 .NET 管理。
            Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length); LocalFree(output.Data); }
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

/// <summary>比较当前项 Key 和主窗口选中 Key，供侧栏 MultiBinding 触发高亮。</summary>
public sealed class SameValueConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) => values.Length == 2 && values[0] is string a && values[1] is string b && a == b;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>星标只读取收藏状态；点击通过命令更新，目录和快捷收藏使用同一份 Key 集合。</summary>
public sealed class ForumFavoriteConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        values.Length == 2 && values[0] is string key && values[1] is IReadOnlySet<string> keys && keys.Contains(key);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>只在最后一个列表项下留出悬浮按钮的空间；续页时随 Count 变化转移，不改变正文数据。</summary>
public sealed class FloatingActionInsetConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (values.Length < 4 || values[1] is not System.Collections.IList { Count: > 0 } items ||
            !ReferenceEquals(values[0], items[items.Count - 1])) return new System.Windows.Thickness(0);
        var actionBottom = values.Length > 4 && values[4] is double bottom ? bottom : 16;
        var statusVisible = values.Length > 5 && values[5] is string { Length: > 0 };
        return new System.Windows.Thickness(0, 0, 0, values[3] is true ? 48 + actionBottom + 16 : statusVisible ? 48 : 0);
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>发串与回应以阅读底栏的实际高度对齐；额外留白只属于按钮，不截短串列表。</summary>
public sealed class FloatingActionMarginConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        new System.Windows.Thickness(0, 0, 22, 16 + (value is double height && double.IsFinite(height) && height > 0 ? height : 49));
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
