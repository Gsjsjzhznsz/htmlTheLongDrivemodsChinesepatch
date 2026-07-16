using TLDWorkshop.App.ViewModels;

namespace TLDWorkshop.App.Views;

public partial class SubmitPage : System.Windows.Controls.Page
{
    public SubmitPage(SubmitViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
