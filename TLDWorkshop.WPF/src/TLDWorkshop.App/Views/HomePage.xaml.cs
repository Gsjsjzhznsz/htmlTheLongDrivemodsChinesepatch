using TLDWorkshop.App.ViewModels;

namespace TLDWorkshop.App.Views;

public partial class HomePage : System.Windows.Controls.Page
{
    public HomePage(HomeViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadAsync();
    }
}
