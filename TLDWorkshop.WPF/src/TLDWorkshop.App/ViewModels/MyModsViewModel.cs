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
/// 我的模组页面 ViewModel。合并两个源 + 大图标卡片 + 详情面板（卸载/重装/更新）。
/// </summary>
public partial class MyModsViewModel : ViewModelBase
{
    private readonly IModRepository _repo;
    private readonly AppSettings _settings;
    private readonly DialogService _dialogs;
    private readonly FilePickerService _pickers;

    public ObservableCollection<MergedMod> InstalledMods { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    [ObservableProperty] private MergedMod? _selectedMod;
    [ObservableProperty] private bool _isDetailVisible;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _categoryFilter = "";
    [ObservableProperty] private string _searchText = string.Empty;

    public bool IsListVisible => !IsDetailVisible;
    public int DisplaySource => _settings.DisplaySourceIndex;

    public MyModsViewModel(IModRepository repo, AppSettings settings,
        DialogService dialogs, FilePickerService pickers)
    {
        _repo = repo;
        _settings = settings;
        _dialogs = dialogs;
        _pickers = pickers;

        // 初始化分类默认值（用 i18n）
        _categoryFilter = I18nService.Instance.T("Browse.AllCategories");
        Categories.Add(_categoryFilter);

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SearchText) or nameof(CategoryFilter))
                ApplyFilter();
            if (e.PropertyName == nameof(IsDetailVisible))
                OnPropertyChanged(nameof(IsListVisible));
        };
    }

    private static string GetModsDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TheLongDrive", "Mods");

    private List<MergedMod> _allInstalled = new();

    [RelayCommand]
    public async Task LoadAsync() => await RefreshAsync();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var modsDir = GetModsDir();
        if (!Directory.Exists(modsDir))
        {
            StatusMessage = I18nService.Instance.T("Msg.ModsDirNotFound");
            InstalledMods.Clear();
            await Task.CompletedTask;
            return;
        }

        IsBusy = true;
        StatusMessage = I18nService.Instance.T("Msg.ScanningInstalled");

        try
        {
            // 拉取合并的在线 mod 列表
            List<MergedMod> onlineMods;
            try { onlineMods = await _repo.FetchMergedModsAsync(); }
            catch { onlineMods = new List<MergedMod>(); }

            var onlineByFileName = onlineMods
                .GroupBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // 扫描本地 dll
            var dllFiles = Directory.GetFiles(modsDir, "*.dll", SearchOption.TopDirectoryOnly);
            _allInstalled = new List<MergedMod>();

            foreach (var dll in dllFiles)
            {
                var fileName = Path.GetFileName(dll);
                if (onlineByFileName.TryGetValue(fileName, out var merged))
                {
                    merged.SetDisplayProperties(DisplaySource);
                    merged.IsInstalled = true;
                    _allInstalled.Add(merged);
                }
                else
                {
                    var local = new MergedMod
                    {
                        Official = new Mod { Name = Path.GetFileNameWithoutExtension(dll), FileName = fileName, Author = "(本地)", Category = "Local" },
                    };
                    local.SetDisplayProperties(DisplaySource);
                    local.IsInstalled = true;
                    _allInstalled.Add(local);
                }
            }

            // 填充分类
            Categories.Clear();
            var allCat = I18nService.Instance.T("Browse.AllCategories");
            Categories.Add(allCat);
            foreach (var cat in _allInstalled.Select(m => m.Official?.Category ?? m.Jihu?.Category ?? m.DisplayCategory)
                         .Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c))
                Categories.Add(BrowseViewModel.CategoryToZh(cat));

            // 默认选中"全部分类"
            CategoryFilter = allCat;

            ApplyFilter();
            StatusMessage = I18nService.Instance.T("Msg.InstalledCount", _allInstalled.Count);
            AppLog.Add($"扫描已安装 mod：{_allInstalled.Count} 个");
        }
        catch (Exception ex)
        {
            StatusMessage = I18nService.Instance.T("Msg.ScanFailed", ex.Message);
            AppLog.Add($"扫描已安装 mod 失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        InstalledMods.Clear();
        var query = _allInstalled.AsEnumerable();

        var allCategoriesText = I18nService.Instance.T("Browse.AllCategories");
        if (!string.IsNullOrEmpty(CategoryFilter) && CategoryFilter != allCategoriesText)
            query = query.Where(m => BrowseViewModel.CategoryToZh(m.Official?.Category ?? m.Jihu?.Category ?? m.DisplayCategory) == CategoryFilter);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            query = query.Where(m =>
                m.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                m.DisplayAuthor.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var m in query) InstalledMods.Add(m);
    }

    [RelayCommand]
    private void SelectMod(MergedMod mod)
    {
        SelectedMod = mod;
        IsDetailVisible = true;
    }

    [RelayCommand]
    private void BackToList()
    {
        IsDetailVisible = false;
        SelectedMod = null;
    }

    [RelayCommand]
    private async Task UninstallModAsync()
    {
        if (SelectedMod == null) return;
        var result = await _dialogs.ShowConfirmAsync(I18nService.Instance.T("Msg.Confirm"),
            I18nService.Instance.T("Msg.UninstallConfirm", SelectedMod.DisplayName, SelectedMod.FileName));
        if (result != System.Windows.MessageBoxResult.OK) return;

        try
        {
            var modsDir = GetModsDir();
            var dllPath = Path.Combine(modsDir, SelectedMod.FileName);
            if (File.Exists(dllPath)) File.Delete(dllPath);

            var subDir = Path.Combine(modsDir, Path.GetFileNameWithoutExtension(SelectedMod.FileName));
            if (Directory.Exists(subDir)) Directory.Delete(subDir, recursive: true);

            StatusMessage = I18nService.Instance.T("Msg.Uninstalled", SelectedMod.DisplayName);
            AppLog.Add($"卸载 mod：{SelectedMod.DisplayName}");
            BackToList();
            await RefreshAsync();
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.UninstallFailed"), ex.Message); }
    }

    [RelayCommand]
    private async Task ReinstallModAsync()
    {
        if (SelectedMod == null) return;
        var modToDownload = DisplaySource == 0 ? SelectedMod.Official : SelectedMod.Jihu;
        if (modToDownload == null) modToDownload = SelectedMod.Official ?? SelectedMod.Jihu;
        if (modToDownload == null) return;

        IsDownloading = true;
        StatusMessage = I18nService.Instance.T("Msg.Reinstalling", SelectedMod.DisplayName);

        try
        {
            var modsDir = GetModsDir();
            var dllPath = Path.Combine(modsDir, SelectedMod.FileName);
            if (File.Exists(dllPath)) File.Delete(dllPath);

            var modpackService = App.Services.GetRequiredService<ModpackService>();
            var allModsList = (await _repo.FetchMergedModsAsync())
                .SelectMany(m => new[] { m.Official, m.Jihu }).Where(m => m != null).Cast<Mod>().ToList();

            var (ok, msg, _) = await modpackService.InstallWithDepsAsync(
                modToDownload, allModsList, modsDir, new Progress<string>(s => StatusMessage = s));

            if (ok) { StatusMessage = I18nService.Instance.T("Msg.ReinstallDone", SelectedMod.DisplayName, msg); AppLog.Add($"重装成功：{SelectedMod.DisplayName}"); }
            else await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.ReinstallFailed"), msg);
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.ReinstallFailed"), ex.Message); }
        finally { IsDownloading = false; }
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        var dir = GetModsDir();
        if (Directory.Exists(dir))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true, Verb = "open" });
    }

    [RelayCommand]
    private async Task DisableAllAsync() => await ToggleAllAsync(false);

    [RelayCommand]
    private async Task EnableAllAsync() => await ToggleAllAsync(true);

    private async Task ToggleAllAsync(bool enable)
    {
        var modsDir = GetModsDir();
        if (!Directory.Exists(modsDir)) return;
        await Task.Run(() =>
        {
            foreach (var f in Directory.GetFiles(modsDir))
            {
                if (enable && f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    File.Move(f, f[..^".disabled".Length]);
                else if (!enable && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    File.Move(f, f + ".disabled");
            }
        });
        await RefreshAsync();
    }
}
