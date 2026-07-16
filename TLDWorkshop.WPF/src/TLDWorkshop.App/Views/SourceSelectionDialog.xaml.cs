using System.Windows;
using System.Windows.Controls;
using TLDWorkshop.Core.Models;

namespace TLDWorkshop.App.Views;

/// <summary>
/// 下载源选择对话框。当用户未设置默认下载源时弹出。
/// </summary>
public partial class SourceSelectionDialog : Window
{
    public int SelectedSourceIndex { get; private set; } = 0;
    public bool RememberChoice { get; private set; } = false;

    private readonly MergedMod _mod;

    public SourceSelectionDialog(MergedMod mod)
    {
        _mod = mod;
        InitializeComponent();
        LoadModInfo();
    }

    private void LoadModInfo()
    {
        ModNameText.Text = _mod.GetDisplayName(1);

        // 官方源信息
        if (_mod.Official != null)
        {
            OfficialNameText.Text = _mod.Official.Name;
            OfficialVersionText.Text = $"v{_mod.Official.Version}";
            OfficialPanel.Visibility = Visibility.Visible;
        }
        else
        {
            OfficialPanel.Visibility = Visibility.Collapsed;
        }

        // 极狐源信息
        if (_mod.Jihu != null)
        {
            JihuNameText.Text = _mod.Jihu.Name;
            JihuVersionText.Text = $"v{_mod.Jihu.Version}";
            JihuPanel.Visibility = Visibility.Visible;
        }
        else
        {
            JihuPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void OfficialButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedSourceIndex = 0;
        RememberChoice = RememberCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void JihuButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedSourceIndex = 1;
        RememberChoice = RememberCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
