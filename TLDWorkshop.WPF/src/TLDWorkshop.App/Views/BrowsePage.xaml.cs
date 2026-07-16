using System.Windows;
using System.Windows.Input;
using TLDWorkshop.App.ViewModels;
using TLDWorkshop.Core.Models;

namespace TLDWorkshop.App.Views;

public partial class BrowsePage : System.Windows.Controls.Page
{
    public BrowsePage(BrowseViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadAsync();
    }

    // Bug A 修复：卡片点击回到最简单的 MouseLeftButtonDown code-behind。
    // 不要用 Border.InputBindings + MouseBinding（那个内部订阅 Stylus 事件，会阻止 ListBox
    // 内部 ScrollViewer 的 PanningMode 触屏滑动）。MouseLeftButtonDown 只响应鼠标点击，
    // 触屏 promote 由 ListBox 的 ScrollViewer 处理，互不干扰。
    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MergedMod mod)
        {
            if (DataContext is BrowseViewModel vm)
                vm.SelectModCommand.Execute(mod);
        }
    }

    private void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is MergedMod mod)
        {
            if (DataContext is BrowseViewModel vm)
                vm.QuickDownloadCommand.Execute(mod);
        }
    }
}
