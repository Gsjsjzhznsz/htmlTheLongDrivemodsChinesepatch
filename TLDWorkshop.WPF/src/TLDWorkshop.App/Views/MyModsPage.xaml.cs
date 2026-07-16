using System.Windows;
using System.Windows.Input;
using TLDWorkshop.App.ViewModels;
using TLDWorkshop.Core.Models;

namespace TLDWorkshop.App.Views;

public partial class MyModsPage : System.Windows.Controls.Page
{
    public MyModsPage(MyModsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadAsync();
    }

    // Bug A 修复：回到 MouseLeftButtonDown code-behind，不用 InputBindings（避免吞 Stylus 事件）
    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MergedMod mod)
        {
            if (DataContext is MyModsViewModel vm)
                vm.SelectModCommand.Execute(mod);
        }
    }

    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is MergedMod mod)
        {
            if (DataContext is MyModsViewModel vm)
            {
                vm.SelectModCommand.Execute(mod);
                vm.UninstallModCommand.Execute(null);
            }
        }
    }
}
