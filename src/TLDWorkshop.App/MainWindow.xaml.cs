using System.Windows;
using System.Windows.Threading;
using TLDWorkshop.App.Controls;
using TLDWorkshop.App.Services;
using TLDWorkshop.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;
using AppNavService = TLDWorkshop.App.Services.NavigationService;

namespace TLDWorkshop.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel vm, AppNavService nav, IPageService pageService)
    {
        InitializeComponent();
        DataContext = vm;

        RootNavigation.SetPageService(pageService);
        nav.Attach(RootNavigation, SnackbarPresenter);

        Loaded += OnLoaded;

        // Bug 2 修复：每次导航后，用 Background 优先级应用滚动支持（只调用一次）
        RootNavigation.Navigated += (_, _) =>
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                ScrollViewerHelper.ApplyToAllScrollableViewers(RootNavigation);
            }), DispatcherPriority.Background);
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        RootNavigation.Navigate(typeof(Views.HomePage));

        var vm = (MainViewModel)DataContext;
        await vm.InitializeAsync();

        // Bug 2 修复：窗口加载后应用滚动支持（只调用一次）
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            ScrollViewerHelper.ApplyToAllScrollableViewers(this);
        }), DispatcherPriority.Background);

        I18nService.Instance.LanguageChanged += () =>
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                ScrollViewerHelper.ApplyToAllScrollableViewers(this);
            }), DispatcherPriority.Background);
        };
    }
}
