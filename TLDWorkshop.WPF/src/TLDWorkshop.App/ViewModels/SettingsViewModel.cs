using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TLDWorkshop.App.Services;
using TLDWorkshop.Core.Contracts;
using TLDWorkshop.Core.Models;
using TLDWorkshop.Core.Services;

namespace TLDWorkshop.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IPathDetector _pathDetector;
    private readonly AppSettings _settings;
    private readonly FilePickerService _pickers;

    [ObservableProperty] private string _tldPath = string.Empty;
    [ObservableProperty] private bool _checkUpdatesOnStart;
    [ObservableProperty] private bool _useExperimentalBranch;
    [ObservableProperty] private int _itemsPerPage = 30;
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private int _displaySourceIndex = 1;
    [ObservableProperty] private int _downloadSourceIndex = -1;  // -1 = 每次询问, 0=Official, 1=极狐
    [ObservableProperty] private int _languageIndex = 0;  // 0=中文, 1=English

    /// <summary>
    /// ComboBox 专用索引：与 <see cref="DownloadSourceIndex"/> 的映射关系：<br/>
    /// 0 = 每次询问 (VM: -1)<br/>
    /// 1 = Official (VM: 0)<br/>
    /// 2 = 极狐 (VM: 1)<br/>
    /// 修复 Bug C：原本直接绑定 DownloadSourceIndex，导致 VM=0(Official) 时 ComboBox 显示 "每次询问"。
    /// </summary>
    public int DownloadSourceComboIndex
    {
        get => DownloadSourceIndex switch { -1 => 0, 0 => 1, 1 => 2, _ => 0 };
        set
        {
            var newVmValue = value switch { 0 => -1, 1 => 0, 2 => 1, _ => -1 };
            if (DownloadSourceIndex != newVmValue)
            {
                // 通过生成的 DownloadSourceIndex 属性 setter 赋值，自动触发 PropertyChanged
                // + OnDownloadSourceIndexChanged → 再触发 DownloadSourceComboIndex 的 PropertyChanged
                DownloadSourceIndex = newVmValue;
            }
        }
    }

    /// <summary>当 DownloadSourceIndex 变化时同步刷新 ComboBox 索引（外部调用 RefreshFromSettings 时必需）。</summary>
    partial void OnDownloadSourceIndexChanged(int value)
    {
        OnPropertyChanged(nameof(DownloadSourceComboIndex));
    }

    [ObservableProperty] private string _ftpUsername = string.Empty;
    [ObservableProperty] private string _ftpPassword = string.Empty;
    [ObservableProperty] private string _submissionEndpoint = string.Empty;

    /// <summary>全局操作日志。绑定到设置页底部。</summary>
    public ObservableCollection<string> LogEntries => AppLog.Entries;

    /// <summary>数据源名称列表。</summary>
    public ObservableCollection<string> SourceNames { get; } = new() { "Official (English)", "中文源 (镜像)" };

    public SettingsViewModel(IPathDetector pathDetector, AppSettings settings,
        FilePickerService pickers)
    {
        _pathDetector = pathDetector;
        _settings = settings;
        _pickers = pickers;
        LoadFromSettings();

        // 防抖保存定时器：500ms 内连续按键只保存一次，避免每次按键触发磁盘写
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            SaveToSettings();
        };

        // 任何属性变化时触发防抖保存（跳过由 RefreshFromSettings 触发的循环）
        // 仅 IsDarkTheme 变化时立即应用主题，其他属性变化只延迟保存不立即 ApplyTheme
        PropertyChanged += (_, e) =>
        {
            if (_isLoading) return;
            if (string.Equals(e.PropertyName, nameof(IsDarkTheme), StringComparison.Ordinal))
            {
                // 主题切换需要立即生效
                App.ApplyTheme(IsDarkTheme);
            }
            // 重启防抖定时器，延迟保存到磁盘
            _saveDebounce.Stop();
            _saveDebounce.Start();
        };
    }

    private readonly DispatcherTimer _saveDebounce;

    private bool _isLoading = false;

    /// <summary>从 AppSettings 重新加载（外部修改 DownloadSourceIndex 时调用）。</summary>
    public void RefreshFromSettings()
    {
        _isLoading = true;
        try
        {
            // 重新读取所有字段（不只是 DownloadSourceIndex）
            LoadFromSettings();
            // 显式触发 PropertyChanged，确保 ComboBox 重新绑定
            // 即使值没变也要触发，因为外部可能已经改了 _settings.DownloadSourceIndex
            OnPropertyChanged(nameof(DownloadSourceIndex));
            OnPropertyChanged(nameof(DownloadSourceComboIndex));
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void LoadFromSettings()
    {
        // 不在这里改 _isLoading，由调用方（构造器 / RefreshFromSettings）控制
        TldPath = _settings.TldPath ?? string.Empty;
        CheckUpdatesOnStart = _settings.CheckUpdatesOnStart;
        UseExperimentalBranch = _settings.UseExperimentalBranch;
        ItemsPerPage = _settings.ItemsPerPage;
        IsDarkTheme = !string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        DisplaySourceIndex = _settings.DisplaySourceIndex;
        DownloadSourceIndex = _settings.DownloadSourceIndex ?? -1;
        LanguageIndex = _settings.Language == "en" ? 1 : 0;
        FtpUsername = _settings.CustomFtpUsername ?? string.Empty;
        FtpPassword = _settings.CustomFtpPassword ?? string.Empty;
        SubmissionEndpoint = _settings.ModSubmissionEndpoint;
    }

    private void SaveToSettings()
    {
        _settings.TldPath = string.IsNullOrEmpty(TldPath) ? null : TldPath;
        _settings.CheckUpdatesOnStart = CheckUpdatesOnStart;
        _settings.UseExperimentalBranch = UseExperimentalBranch;
        _settings.ItemsPerPage = ItemsPerPage;
        _settings.Theme = IsDarkTheme ? "Dark" : "Light";
        _settings.DisplaySourceIndex = DisplaySourceIndex;
        _settings.DownloadSourceIndex = DownloadSourceIndex < 0 ? null : DownloadSourceIndex;
        _settings.Language = LanguageIndex == 1 ? "en" : "zh";
        I18nService.Instance.CurrentLang = _settings.Language;
        _settings.CustomFtpUsername = string.IsNullOrEmpty(FtpUsername) ? null : FtpUsername;
        _settings.CustomFtpPassword = string.IsNullOrEmpty(FtpPassword) ? null : FtpPassword;
        _settings.ModSubmissionEndpoint = SubmissionEndpoint;
        _settings.Save();
        // 注意：ApplyTheme 已移到 PropertyChanged 中，仅在 IsDarkTheme 变化时立即触发
        StatusMessage = I18nService.Instance.T("Msg.SettingsSaved");
    }

    [RelayCommand]
    private void BrowseTldPath()
    {
        var picked = _pickers.PickFolder(TldPath, "选择 The Long Drive 安装目录");
        if (!string.IsNullOrEmpty(picked))
        {
            TldPath = picked;  // PropertyChanged → 自动保存
            // 同步写入 TLDFolder.txt（原版路径持久化文件）
            PathDetector.Save(picked);
        }
    }

    [RelayCommand]
    private void AutoDetect()
    {
        var detected = _pathDetector.TryDetect();
        if (!string.IsNullOrEmpty(detected))
        {
            TldPath = detected;  // PropertyChanged → 自动保存
            PathDetector.Save(detected);
            StatusMessage = I18nService.Instance.T("Msg.AutoDetected", detected);
        }
        else
        {
            StatusMessage = I18nService.Instance.T("Msg.GameNotFoundManual");
        }
    }
}
