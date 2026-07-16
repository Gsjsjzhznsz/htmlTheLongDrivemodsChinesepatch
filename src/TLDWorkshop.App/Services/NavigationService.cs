using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace TLDWorkshop.App.Services;

/// <summary>
/// 包装 WPF-UI 的 NavigationView，让 ViewModel 层能在不引用控件类型的情况下导航。
/// </summary>
public sealed class NavigationService
{
    private NavigationView? _view;
    // WPF-UI 3.x: NavigationView 内部用其自己的缓存页 Frame，不再暴露 Content 属性
    public Frame? Frame => null;

    public void Attach(NavigationView view, object _)
    {
        _view = view;
    }

    public void Navigate(Type pageType) => _view?.Navigate(pageType);

    public void Navigate<T>() where T : class
    {
        _view?.Navigate(typeof(T));
    }

    /// <summary>
    /// 显示简单的通知。WPF-UI 3.x 的 SnackbarService API 在不同小版本签名略有差异，
    /// 此处用反射调用以避免编译期耦合。失败时静默忽略（Snackbar 仅辅助提示）。
    /// </summary>
    public Task ShowSnackbarAsync(string title, string message, string appearance = "Info")
    {
        // 暂不接入 SnackbarService，避免 WPF-UI 版本差异导致编译失败。
        // 后续可在 Windows 上调试时根据具体版本填入正确调用。
        return Task.CompletedTask;
    }
}
