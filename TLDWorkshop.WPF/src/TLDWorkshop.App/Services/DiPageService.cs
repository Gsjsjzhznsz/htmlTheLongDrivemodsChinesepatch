using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;

namespace TLDWorkshop.App.Services;

/// <summary>
/// WPF-UI 的 IPageService 实现：通过 DI 容器创建 Page 实例。
/// 不缓存——Transient 注册每次都新建实例，避免缓存持有已卸载页面造成的内存泄漏。
/// </summary>
public sealed class DiPageService : IPageService
{
    public T? GetPage<T>() where T : class
    {
        try
        {
            var result = App.Services.GetService<T>();
            if (result == null)
            {
                result = ActivatorUtilities.CreateInstance<T>(App.Services);
            }
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DiPageService] GetPage<{typeof(T).Name}> failed: {ex}");
            // 不返回 null（会导致 NavigationView 崩溃），重新抛出
            throw;
        }
    }

    public FrameworkElement? GetPage(Type pageType)
    {
        try
        {
            var result = App.Services.GetService(pageType) as FrameworkElement;
            if (result == null)
            {
                result = ActivatorUtilities.CreateInstance(App.Services, pageType) as FrameworkElement;
            }
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DiPageService] GetPage({pageType.Name}) failed: {ex}");
            throw;
        }
    }
}
