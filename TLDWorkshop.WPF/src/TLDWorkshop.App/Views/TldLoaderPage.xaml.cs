using TLDWorkshop.App.ViewModels;

namespace TLDWorkshop.App.Views;

public partial class TldLoaderPage : System.Windows.Controls.Page
{
    public TldLoaderPage(TldLoaderViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadAsync();
    }
}
