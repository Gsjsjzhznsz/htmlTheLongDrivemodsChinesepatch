using System.Windows;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TLDWorkshop.App.Services;
using TLDWorkshop.App.ViewModels;
using TLDWorkshop.Core.Contracts;
using TLDWorkshop.Core.Models;
using TLDWorkshop.Core.Services;

namespace TLDWorkshop.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    // 不再用 StartupUri，改用 Startup 事件手动 new MainWindow 以便注入依赖
    private void OnStartup(object sender, StartupEventArgs e)
    {
        // 先装全局异常兜底，确保后续任何崩溃都能弹窗而不是静默退出
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.ToString(), "未捕获异常", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.ExceptionObject?.ToString() ?? "?", "AppDomain 未捕获异常", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { AppLog.Add("Unobserved: " + e.Exception); e.SetObserved(); };

        try
        {
            BuildServiceProvider();

            // 应用级主题 + 语言：从设置读取
            // Bug 5 修复：在 MainWindow 显示后再应用主题，避免控件先用默认主题渲染导致字体黑色
            var settings = Services.GetRequiredService<AppSettings>();
            var theme = string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? Wpf.Ui.Appearance.ApplicationTheme.Light
                : Wpf.Ui.Appearance.ApplicationTheme.Dark;
            I18nService.Instance.CurrentLang = settings.Language;

            var mainWindow = Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();

            // Bug 5 修复：窗口显示后再应用主题，确保所有控件的资源字典正确刷新
            // WPF-UI 的 ApplicationThemeManager.Apply 会修改 Application.Resources，
            // 但已渲染的控件不会自动重新绑定，需要在窗口显示后强制刷新
            try
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme);
            }
            catch { /* 主题应用失败不致命 */ }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>切换主题（Dark/Light）。供 SettingsPage 调用。</summary>
    public static void ApplyTheme(bool isDark)
    {
        try
        {
            var theme = isDark
                ? Wpf.Ui.Appearance.ApplicationTheme.Dark
                : Wpf.Ui.Appearance.ApplicationTheme.Light;
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme);
        }
        catch { /* 主题切换失败忽略 */ }
    }

    private static void BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // ---- Settings ----
        services.AddSingleton(AppSettings.Load());

        // ---- Core services ----
        services.AddSingleton<IPathDetector, PathDetector>();
        services.AddSingleton<IModRepository, ModRepository>();
        services.AddSingleton<ITldPatcher, TldPatcher>();
        services.AddSingleton<ITldPatcherExtended, TldPatcher>();
        services.AddSingleton<IModInstaller, ModInstaller>();
        services.AddSingleton<IUpdateChecker, UpdateChecker>();
        services.AddSingleton<ModpackService>();

        // ---- App services ----
        services.AddSingleton<NavigationService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<FilePickerService>();
        services.AddSingleton<Wpf.Ui.IPageService, DiPageService>();

        // ---- ViewModels ----
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<TldLoaderViewModel>();
        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<MyModsViewModel>();
        services.AddSingleton<ModpackViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SubmitViewModel>();

        // ---- Pages（WPF-UI NavigationView 通过 IPageService 解析这些类型）----
        services.AddTransient<Views.HomePage>();
        services.AddTransient<Views.TldLoaderPage>();
        services.AddTransient<Views.BrowsePage>();
        services.AddTransient<Views.MyModsPage>();
        services.AddTransient<Views.ModpackPage>();
        services.AddTransient<Views.SubmitPage>();
        services.AddTransient<Views.SettingsPage>();

        // ---- MainWindow（带参数构造）----
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();
    }
}
