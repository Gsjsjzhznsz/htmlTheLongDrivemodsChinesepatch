using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TLDWorkshop.App.Services;
using TLDWorkshop.Core.Contracts;
using TLDWorkshop.Core.Models;
using TLDWorkshop.Core.Services;

namespace TLDWorkshop.App.ViewModels;

/// <summary>
/// 模组包页面 ViewModel。修复 Bug B：现在用 MergedModpack 双源合并（与 BrowseViewModel 一致），
/// 详情页同时显示官方源和极狐源，安装时支持源选择。
/// </summary>
public partial class ModpackViewModel : ViewModelBase
{
    private readonly IModRepository _repo;
    private readonly ModpackService _modpackService;
    private readonly AppSettings _settings;
    private readonly DialogService _dialogs;
    private readonly FilePickerService _pickers;

    /// <summary>双源合并后的模组包列表。</summary>
    public ObservableCollection<MergedModpack> OnlineModpacks { get; } = new();

    [ObservableProperty] private string _packName = string.Empty;
    [ObservableProperty] private int _totalMods;
    [ObservableProperty] private int _completedMods;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private MergedModpack? _selectedModpack;
    [ObservableProperty] private bool _isDetailVisible;

    public bool IsListVisible => !IsDetailVisible;

    public ModpackViewModel(IModRepository repo, ModpackService modpackService,
        AppSettings settings, DialogService dialogs, FilePickerService pickers)
    {
        _repo = repo;
        _modpackService = modpackService;
        _settings = settings;
        _dialogs = dialogs;
        _pickers = pickers;

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsDetailVisible))
                OnPropertyChanged(nameof(IsListVisible));
        };
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = I18nService.Instance.T("Msg.MergingModpacks");
        AppLog.Add("开始拉取模组包列表（双源合并）");

        try
        {
            OnlineModpacks.Clear();
            var merged = await _repo.FetchMergedModpacksAsync();
            // 用设置页的 DisplaySourceIndex 设置每个 modpack 的显示源
            foreach (var p in merged)
            {
                p.SetDisplaySource(_settings.DisplaySourceIndex);
                OnlineModpacks.Add(p);
            }

            var bothCount = merged.Count(m => m.HasBothSources);
            StatusMessage = I18nService.Instance.T("Msg.ModpacksLoaded", OnlineModpacks.Count, bothCount);
            AppLog.Add($"模组包加载完成：{OnlineModpacks.Count} 个（双源 {bothCount}）");
        }
        catch (Exception ex)
        {
            StatusMessage = I18nService.Instance.T("Msg.LoadFailed", ex.Message);
            AppLog.Add($"模组包加载失败：{ex.Message}");
        }
        finally { IsBusy = false; }
    }

    /// <summary>刷新模组包列表（强制重新拉取）。</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        OnlineModpacks.Clear();
        await LoadAsync();
    }

    /// <summary>点击模组包进入详情。</summary>
    [RelayCommand]
    private void SelectModpack(MergedModpack pack)
    {
        SelectedModpack = pack;
        IsDetailVisible = true;
    }

    [RelayCommand]
    private void BackToList()
    {
        IsDetailVisible = false;
        SelectedModpack = null;
    }

    /// <summary>安装模组包。修复 Bug B：双源时支持源选择（复用 BrowseViewModel 的源选择逻辑）。</summary>
    [RelayCommand]
    private async Task InstallModpackAsync(MergedModpack? pack)
    {
        if (pack == null) pack = SelectedModpack;
        if (pack == null) return;

        // 确定下载源
        int downloadSource;
        if (_settings.DownloadSourceIndex.HasValue)
        {
            downloadSource = _settings.DownloadSourceIndex.Value;
        }
        else if (pack.HasBothSources)
        {
            // 弹出源选择对话框
            var dialog = new Views.SourceSelectionDialogForModpack(pack)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            if (dialog.ShowDialog() != true) return;
            downloadSource = dialog.SelectedSourceIndex;
            if (dialog.RememberChoice)
            {
                _settings.DownloadSourceIndex = downloadSource;
                _settings.Save();
                AppLog.Add($"已设置默认下载源：{(downloadSource == 0 ? "官方" : "中文源")}");
                try
                {
                    App.Services.GetService<SettingsViewModel>()?.RefreshFromSettings();
                }
                catch { /* 忽略 */ }
            }
        }
        else
        {
            // 只有一个源，直接用
            downloadSource = pack.Official != null ? 0 : 1;
        }

        var packToInstall = pack.GetBySource(downloadSource);
        if (packToInstall == null || string.IsNullOrEmpty(packToInstall.Link))
        {
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.Hint"), I18nService.Instance.T("Msg.NoDownloadLink"));
            return;
        }

        AppLog.Add($"开始安装模组包：{packToInstall.Name}（源：{(downloadSource == 0 ? "官方" : "中文源")}）");
        CompletedMods = 0;
        Progress = 0;

        IsBusy = true;
        StatusMessage = I18nService.Instance.T("Msg.InstallingModpack", packToInstall.Name);

        try
        {
            var modsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TheLongDrive", "Mods");
            Directory.CreateDirectory(modsDir);

            var filenames = await _repo.DownloadModpackTxtAsync(packToInstall.Link);
            TotalMods = filenames.Count;

            var progress = new Progress<string>(s =>
            {
                StatusMessage = s;
                AppLog.Add(s);
                if (s.StartsWith("[") && s.Contains("/"))
                {
                    var endIdx = s.IndexOf(']');
                    if (endIdx > 0)
                    {
                        var parts = s[1..endIdx].Split('/');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var done))
                        {
                            CompletedMods = done - 1;
                            Progress = TotalMods > 0 ? (double)CompletedMods / TotalMods * 100 : 0;
                        }
                    }
                }
            });

            // 用合并的 mod 列表做依赖解析
            var allMods = await _repo.FetchMergedModsAsync();
            var allModsList = allMods.SelectMany(m => new[] { m.Official, m.Jihu })
                .Where(m => m != null).Cast<Mod>().ToList();

            var results = await _modpackService.BatchInstallAsync(filenames, allModsList, modsDir, progress);

            Progress = 100;
            CompletedMods = TotalMods;

            var successCount = results.Count(r => r.Contains("[OK]"));
            StatusMessage = I18nService.Instance.T("Msg.InstallComplete", successCount);
            AppLog.Add($"模组包安装完成：{successCount}/{results.Count} 成功");
            // 确保在 UI 线程弹框（BatchInstallAsync 内部用了线程池，await 后可能不在 UI 线程）
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _ = _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.InstallComplete"),
                    I18nService.Instance.T("Msg.ModpackInstallDone", packToInstall.Name, successCount, results.Count));
            });
        }
        catch (Exception ex)
        {
            AppLog.Add($"模组包安装失败：{ex.Message}");
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.InstallFailed"), ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ImportModpackAsync()
    {
        var txtPath = _pickers.PickOpenFile("txt files (*.txt)|*.txt|All files (*.*)|*.*", "选择模组包 .txt 文件");
        if (string.IsNullOrEmpty(txtPath)) return;

        AppLog.Add($"导入模组包：{Path.GetFileName(txtPath)}");
        IsBusy = true;
        StatusMessage = I18nService.Instance.T("Msg.Importing");

        try
        {
            var modsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TheLongDrive", "Mods");
            Directory.CreateDirectory(modsDir);

            var content = await File.ReadAllTextAsync(txtPath);
            var filenames = ModRepository.ParseTxtFileNames(content);
            TotalMods = filenames.Count;

            var allMods = await _repo.FetchMergedModsAsync();
            var allModsList = allMods.SelectMany(m => new[] { m.Official, m.Jihu })
                .Where(m => m != null).Cast<Mod>().ToList();

            var progress = new Progress<string>(s =>
            {
                StatusMessage = s;
                AppLog.Add(s);
                if (s.StartsWith("[") && s.Contains("/"))
                {
                    var endIdx = s.IndexOf(']');
                    if (endIdx > 0)
                    {
                        var parts = s[1..endIdx].Split('/');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var done))
                        {
                            CompletedMods = done - 1;
                            Progress = TotalMods > 0 ? (double)CompletedMods / TotalMods * 100 : 0;
                        }
                    }
                }
            });

            var results = await _modpackService.BatchInstallAsync(filenames, allModsList, modsDir, progress);

            Progress = 100;
            var successCount = results.Count(r => r.Contains("[OK]"));
            StatusMessage = I18nService.Instance.T("Msg.ImportComplete", successCount);
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.ImportComplete"), I18nService.Instance.T("Msg.ImportDone", successCount, results.Count));
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.ImportFailed"), ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ExportModpackAsync()
    {
        if (string.IsNullOrWhiteSpace(PackName))
        {
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.Hint"), I18nService.Instance.T("Msg.EnterName"));
            return;
        }

        var savePath = _pickers.PickSaveFile("txt files (*.txt)|*.txt", PackName + ".txt", "保存模组包");
        if (string.IsNullOrEmpty(savePath)) return;

        IsBusy = true;
        try
        {
            var modsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TheLongDrive", "Mods");
            await _modpackService.ExportModpackAsync(savePath, modsDir);
            AppLog.Add($"导出模组包：{savePath}");
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.Success"), I18nService.Instance.T("Msg.ExportDone", savePath));
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.ExportFailed"), ex.Message); }
        finally { IsBusy = false; }
    }
}
