using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TLDWorkshop.App.Services;
using TLDWorkshop.Core.Contracts;
using TLDWorkshop.Core.Models;
using TLDWorkshop.Core.Services;

namespace TLDWorkshop.App.ViewModels;

/// <summary>
/// 浏览页面 ViewModel。合并两个源 + 3 个选项卡（全部/已安装/更新）+ 源选择下载。
/// </summary>
public partial class BrowseViewModel : ViewModelBase
{
    private readonly IModRepository _repo;
    private readonly DialogService _dialogs;
    private readonly NavigationService _nav;
    private readonly AppSettings _settings;

    private List<MergedMod> _allMods = new();

    public ObservableCollection<MergedMod> FilteredMods { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    /// <summary>
    /// 分类翻译映射。英文模式返回原文，中文模式返回中文翻译。
    /// 用 i18n key 查找，支持语言切换。
    /// </summary>
    public static string CategoryToZh(string en)
    {
        if (string.IsNullOrEmpty(en)) return en;
        // 用 i18n 查找，key 格式: Category.{en}
        var key = $"Category.{en}";
        var translated = I18nService.Instance.T(key);
        // 如果返回的是 key 本身（没找到），返回原文
        return translated == key ? en : translated;
    }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _categoryFilter = "";
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private int _totalMods;
    [ObservableProperty] private MergedMod? _selectedMod;
    [ObservableProperty] private bool _isDetailVisible;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;

    public bool IsListVisible => !IsDetailVisible;

    public int DisplaySource => _settings.DisplaySourceIndex;

    public BrowseViewModel(IModRepository repo, DialogService dialogs,
        NavigationService nav, AppSettings settings)
    {
        _repo = repo;
        _dialogs = dialogs;
        _nav = nav;
        _settings = settings;

        // 初始化分类默认值（用 i18n）
        _categoryFilter = I18nService.Instance.T("Browse.AllCategories");
        Categories.Add(_categoryFilter);

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SearchText) or nameof(CategoryFilter) or nameof(CurrentPage))
            {
                ApplyFilter();
            }
            if (e.PropertyName == nameof(IsDetailVisible))
                OnPropertyChanged(nameof(IsListVisible));
        };
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_allMods.Count > 0) { ApplyFilter(); return; }

        IsBusy = true;
        StatusMessage = I18nService.Instance.T("Msg.MergingMods");
        AppLog.Add("开始拉取两个源的模组列表");

        try
        {
            _allMods = await _repo.FetchMergedModsAsync();
            _allMods = _allMods.Where(m => !m.IsLegacy).ToList();

            // 设置显示属性 + 标记已安装
            var modsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TheLongDrive", "Mods");
            foreach (var m in _allMods)
            {
                m.SetDisplayProperties(DisplaySource);
                var dllPath = Path.Combine(modsDir, m.FileName);
                m.IsInstalled = File.Exists(dllPath);
                m.HasUpdate = false;
            }

            // 填充分类
            Categories.Clear();
            var allCat = I18nService.Instance.T("Browse.AllCategories");
            Categories.Add(allCat);
            // 统一用官方源的英文 Category 作为 key，避免中英文混在一起
            foreach (var cat in _allMods.Select(m => m.Official?.Category ?? m.Jihu?.Category ?? m.DisplayCategory)
                         .Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c))
                Categories.Add(CategoryToZh(cat));

            // 默认选中"全部分类"
            CategoryFilter = allCat;
            CurrentPage = 1;
            ApplyFilter();
            StatusMessage = I18nService.Instance.T("Msg.ModsLoaded", _allMods.Count);
            AppLog.Add($"加载完成：{_allMods.Count} 个模组");
        }
        catch (Exception ex)
        {
            StatusMessage = I18nService.Instance.T("Msg.LoadFailed", ex.Message);
            AppLog.Add($"加载失败：{ex.Message}");
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.LoadFailed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        _allMods.Clear();
        await LoadAsync();
    }

    private void ApplyFilter()
    {
        FilteredMods.Clear();
        if (_allMods.Count == 0) { TotalMods = 0; TotalPages = 1; return; }

        var pageSize = _settings.ItemsPerPage;
        var query = _allMods.AsEnumerable();

        // 选项卡过滤（移除——用户要求去掉选项卡）
        // query = CurrentTab switch { ... };

        // 分类过滤
        var allCategoriesText = I18nService.Instance.T("Browse.AllCategories");
        if (!string.IsNullOrEmpty(CategoryFilter) && CategoryFilter != allCategoriesText)
            query = query.Where(m => CategoryToZh(m.Official?.Category ?? m.Jihu?.Category ?? m.DisplayCategory) == CategoryFilter);

        // 搜索
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            query = query.Where(m =>
                m.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                m.DisplayAuthor.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = query.ToList();
        TotalMods = filtered.Count;
        TotalPages = Math.Max(1, (filtered.Count + pageSize - 1) / pageSize);
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;
        if (CurrentPage < 1) CurrentPage = 1;

        foreach (var m in filtered.Skip((CurrentPage - 1) * pageSize).Take(pageSize))
            FilteredMods.Add(m);

        PreloadAdjacentPageImages(filtered, pageSize);
    }

    private void PreloadAdjacentPageImages(List<MergedMod> filtered, int pageSize)
    {
        var pagesToPreload = new[] { CurrentPage + 1, CurrentPage - 1 };
        foreach (var pageNum in pagesToPreload)
        {
            if (pageNum < 1 || pageNum > TotalPages) continue;
            foreach (var m in filtered.Skip((pageNum - 1) * pageSize).Take(pageSize))
            {
                var url = m.GetDisplayPicture(DisplaySource);
                if (!string.IsNullOrEmpty(url))
                    Controls.AsyncImage.PreloadUrl(url);
            }
        }
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
    private void NextPage() { if (CurrentPage < TotalPages) CurrentPage++; }

    [RelayCommand]
    private void PreviousPage() { if (CurrentPage > 1) CurrentPage--; }

    /// <summary>下载/更新选中 mod。弹出源选择对话框（如果未设默认）。</summary>
    [RelayCommand]
    private async Task DownloadSelectedAsync()
    {
        if (SelectedMod == null) return;

        // 确定下载源
        int downloadSource;
        if (_settings.DownloadSourceIndex.HasValue)
        {
            downloadSource = _settings.DownloadSourceIndex.Value;
        }
        else
        {
            // 弹出源选择对话框
            var mod = SelectedMod.Official ?? SelectedMod.Jihu;
            if (SelectedMod.Official != null && SelectedMod.Jihu != null)
            {
                var dialog = new Views.SourceSelectionDialog(SelectedMod)
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
                    // 通知设置页刷新
                    try
                    {
                        var sv = App.Services.GetService<SettingsViewModel>();
                        sv?.RefreshFromSettings();
                    }
                    catch { /* 忽略 */ }
                }
            }
            else
            {
                // 只有一个源，直接用
                downloadSource = SelectedMod.Official != null ? 0 : 1;
            }
        }

        var modToDownload = downloadSource == 0 ? SelectedMod.Official : SelectedMod.Jihu;
        if (modToDownload == null) modToDownload = SelectedMod.Official ?? SelectedMod.Jihu;
        if (modToDownload == null) return;

        IsDownloading = true;
        DownloadProgress = 0;
        StatusMessage = I18nService.Instance.T("Msg.Downloading", modToDownload.Name, downloadSource == 0 ? I18nService.Instance.T("Source.OfficialShort") : I18nService.Instance.T("Source.ChineseShort"));
        AppLog.Add($"开始下载：{modToDownload.Name}（源：{(downloadSource == 0 ? "官方" : "中文源")}）");

        try
        {
            var modsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TheLongDrive", "Mods");
            Directory.CreateDirectory(modsDir);

            // 用 ModpackService 安装（含依赖自动安装 + zip 正确解压）
            var modpackService = App.Services.GetRequiredService<ModpackService>();
            // 需要构造 List<Mod> 用于依赖解析
            var allModsList = _allMods.SelectMany(m => new[] { m.Official, m.Jihu })
                .Where(m => m != null).Cast<Mod>().ToList();

            var progress = new Progress<string>(s =>
            {
                StatusMessage = s;
                AppLog.Add(s);
            });

            var (ok, msg, depResults) = await modpackService.InstallWithDepsAsync(
                modToDownload, allModsList, modsDir, progress);

            if (ok)
            {
                StatusMessage = I18nService.Instance.T("Msg.InstallDone", modToDownload.Name);
                AppLog.Add($"安装成功：{modToDownload.Name} v{msg}");
                SelectedMod.IsInstalled = true;
                await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.InstallSuccessShort"), I18nService.Instance.T("Msg.InstallSuccess", modToDownload.Name, msg));
                ApplyFilter(); // 刷新已安装标记
            }
            else
            {
                await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.InstallFailed"), msg);
                AppLog.Add($"安装失败：{modToDownload.Name} - {msg}");
            }
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.DownloadFailed"), ex.Message);
            AppLog.Add($"下载失败：{ex.Message}");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>卡片上的安装/更新按钮点击。</summary>
    [RelayCommand]
    private async Task QuickDownloadAsync(MergedMod mod)
    {
        SelectedMod = mod;
        await DownloadSelectedAsync();
    }

    [RelayCommand]
    private void GoMyMods() => _nav.Navigate<Views.MyModsPage>();
}
