using Microsoft.Win32;
using TLDWorkshop.Core.Contracts;

namespace TLDWorkshop.App.Services;

/// <summary>
/// 文件/目录选择器。Core 层的 <see cref="IPathDetector.LetUserSelectAsync"/> 是占位实现，
/// 实际弹窗由本类调用 WPF 的 OpenFileDialog / FolderBrowserDialog。
/// </summary>
public sealed class FilePickerService
{
    public string? PickFolder(string? startPath = null, string? title = null)
    {
        var dlg = new OpenFolderDialog
        {
            Title = title ?? "Select Folder",
            InitialDirectory = startPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyComputer)
        };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public string? PickOpenFile(string filter = "所有文件|*.*", string? title = null)
    {
        var dlg = new OpenFileDialog
        {
            Filter = filter,
            Title = title ?? "Select File"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? PickSaveFile(string filter = "Zip 文件|*.zip", string? defaultName = null, string? title = null)
    {
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            FileName = defaultName ?? "modpack",
            Title = title ?? "Save To"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
