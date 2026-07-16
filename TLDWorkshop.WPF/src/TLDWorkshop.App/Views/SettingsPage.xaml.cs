using TLDWorkshop.App.ViewModels;

namespace TLDWorkshop.App.Views;

public partial class SettingsPage : System.Windows.Controls.Page
{
    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) => vm.RefreshFromSettings();
    }
}
