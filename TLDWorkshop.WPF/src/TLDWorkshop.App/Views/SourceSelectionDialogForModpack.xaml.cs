using System.Windows;
using TLDWorkshop.App.Services;
using TLDWorkshop.Core.Models;

namespace TLDWorkshop.App.Views;

/// <summary>
/// 模组包下载源选择对话框。当用户未设置默认下载源且模组包双源可用时弹出。
/// 修复 Bug B：与 BrowseViewModel 的 SourceSelectionDialog 对应，但接受 MergedModpack 类型。
/// </summary>
public partial class SourceSelectionDialogForModpack : Window
{
    public int SelectedSourceIndex { get; private set; } = 0;
    public bool RememberChoice { get; private set; } = false;

    private readonly MergedModpack _pack;

    public SourceSelectionDialogForModpack(MergedModpack pack)
    {
        _pack = pack;
        InitializeComponent();
        LoadModpackInfo();
    }

    private void LoadModpackInfo()
    {
        ModNameText.Text = _pack.DisplayName;

        if (_pack.Official != null)
        {
            OfficialNameText.Text = _pack.Official.Name;
            OfficialVersionText.Text = $"v{_pack.Official.Version}  " + I18nService.Instance.T("SourceDialog.Author", _pack.Official.Author);
            OfficialPanel.Visibility = Visibility.Visible;
        }
        else
        {
            OfficialPanel.Visibility = Visibility.Collapsed;
        }

        if (_pack.Jihu != null)
        {
            JihuNameText.Text = _pack.Jihu.Name;
            JihuVersionText.Text = $"v{_pack.Jihu.Version}  " + I18nService.Instance.T("SourceDialog.Author", _pack.Jihu.Author);
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
