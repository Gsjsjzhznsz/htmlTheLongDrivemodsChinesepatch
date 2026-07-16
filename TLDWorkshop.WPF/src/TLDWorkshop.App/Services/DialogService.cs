using System.Windows;
using ContentDialogResult = System.Windows.MessageBoxResult;

namespace TLDWorkshop.App.Services;

/// <summary>
/// 弹窗服务。Bug #7 修复：原代码异常被静默吞掉，此处统一弹 MessageBox 给用户。
///
/// 说明：WPF-UI 3.x 的 ContentDialog 需要预先设置 DialogHost 容器，配置稍繁琐且易出错。
/// 这里直接用 WPF 内置的 MessageBox，零配置、稳定可靠，足够满足"错误上报"的核心需求。
/// 后续若想要 Fluent 风格的对话框，可改回 ContentDialog 并在 MainWindow 设置 DialogHost。
/// </summary>
public sealed class DialogService
{
    public Task<ContentDialogResult> ShowInfoAsync(string title, string message)
        => ShowAsync(title, message, MessageBoxButton.OK, MessageBoxImage.Information);

    public Task<ContentDialogResult> ShowErrorAsync(string title, string message)
        => ShowAsync(title, message, MessageBoxButton.OK, MessageBoxImage.Error);

    public Task<ContentDialogResult> ShowConfirmAsync(string title, string message)
        => ShowAsync(title, message, MessageBoxButton.OKCancel, MessageBoxImage.Question);

    private static Task<ContentDialogResult> ShowAsync(string title, string message,
        MessageBoxButton buttons, MessageBoxImage icon)
    {
        // MessageBox 必须在 UI 线程调用；DialogService 通常已在 UI 线程被调用
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.InvokeAsync(() =>
                (ContentDialogResult)MessageBox.Show(message, title, buttons, icon)).Task;
        }
        return Task.FromResult((ContentDialogResult)MessageBox.Show(message, title, buttons, icon));
    }
}
