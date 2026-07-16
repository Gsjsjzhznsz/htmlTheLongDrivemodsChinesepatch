using System.Windows.Input;
using TLDWorkshop.App.ViewModels;
using TLDWorkshop.Core.Models;

namespace TLDWorkshop.App.Views;

public partial class ModpackPage : System.Windows.Controls.Page
{
    public ModpackPage(ModpackViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadAsync();
    }

    // Bug A 修复：MouseLeftButtonDown code-behind，不用 InputBindings
    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe && fe.DataContext is MergedModpack pack)
        {
            if (DataContext is ModpackViewModel vm)
                vm.SelectModpackCommand.Execute(pack);
        }
    }
}
