using TLDWorkshop.Core.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TLDWorkshop.App.Services;
using TLDWorkshop.Core.Contracts;
using TLDWorkshop.Core.Services;

namespace TLDWorkshop.App.ViewModels;

/// <summary>
/// TLDLoader 专用标签页 ViewModel。
/// 对应原始 exe 的 downpatcher_Click + button3_Click (uninstall) + button4_Click (launch) +
/// Checkupdate (启动检查) + CheckPatchStatus (状态检测) + Starter (智能更新派发)。
///
/// 严格按原始 exe 行为实现：
/// - 状态显示：6 种 PatchState 各自的中文描述 + 推荐操作
/// - 操作：智能更新（自动派发安装/更新/升级）、卸载、启动游戏
/// - 操作日志：实时显示 6 步安装进度
/// </summary>
public partial class TldLoaderViewModel : ViewModelBase
{
    private readonly ITldPatcherExtended _patcher;
    private readonly AppSettings _settings;
    private readonly DialogService _dialogs;
    private readonly FilePickerService _pickers;
    private readonly IPathDetector _pathDetector;

    /// <summary>操作日志（实时显示安装/卸载进度）。</summary>
    public ObservableCollection<string> LogEntries { get; } = new();

    [ObservableProperty] private string _tldPath = string.Empty;
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private string _stateColor = "Gray";
    [ObservableProperty] private string _loaderVersion = "未安装";
    [ObservableProperty] private string _gameVersion = "未检测";
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _canInstall = true;
    [ObservableProperty] private bool _canUninstall;
    [ObservableProperty] private bool _canLaunch;

    public TldLoaderViewModel(ITldPatcherExtended patcher, AppSettings settings,
        DialogService dialogs, FilePickerService pickers, IPathDetector pathDetector)
    {
        _patcher = patcher;
        _settings = settings;
        _dialogs = dialogs;
        _pickers = pickers;
        _pathDetector = pathDetector;
    }

    [RelayCommand]
    public Task LoadAsync() => RefreshStatusAsync();

    /// <summary>刷新状态。原 CheckPatchStatus + CheckTldLoaderVersion + CheckGameVersion。</summary>
    [RelayCommand]
    public async Task RefreshStatusAsync()
    {
        IsBusy = true;
        StatusMessage = I18nService.Instance.T("Msg.DetectingLoader");

        TldPath = _settings.TldPath ?? "(未配置)";

        if (string.IsNullOrEmpty(_settings.TldPath) || !Directory.Exists(_settings.TldPath))
        {
            StateText = I18nService.Instance.T("State.NoPath");
            StateColor = "Orange";
            IsInstalled = false;
            CanInstall = false;
            CanUninstall = false;
            CanLaunch = false;
            LoaderVersion = "未安装";
            GameVersion = "未检测";
            StatusMessage = I18nService.Instance.T("Msg.NeedPath");
            await Task.CompletedTask;
            return;
        }

        try
        {
            var state = await Task.Run(() => _patcher.CheckState(_settings.TldPath));
            UpdateStateDisplay(state);

            // 加载附加信息
            try { LoaderVersion = _patcher.GetInstalledLoaderVersion(_settings.TldPath) ?? "未安装"; }
            catch { LoaderVersion = "未安装"; }
            try { GameVersion = _patcher.IsBetaVersion(_settings.TldPath) ? "Beta 测试版" : "正式版"; }
            catch { GameVersion = "未检测"; }
        }
        catch (Exception ex)
        {
            StateText = I18nService.Instance.T("State.DetectFailed", ex.Message);
            StateColor = "Red";
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateStateDisplay(PatchState state)
    {
        switch (state)
        {
            case PatchState.Patched:
                StateText = I18nService.Instance.T("State.Patched");
                StateColor = "Green";
                IsInstalled = true;
                CanInstall = true;  // 允许重装
                CanUninstall = true;
                CanLaunch = true;
                StatusMessage = I18nService.Instance.T("Msg.LoaderInstalled");
                break;
            case PatchState.NotPatched:
                StateText = I18nService.Instance.T("State.NotPatched");
                StateColor = "Gray";
                IsInstalled = false;
                CanInstall = true;
                CanUninstall = false;
                CanLaunch = true;
                StatusMessage = I18nService.Instance.T("Msg.ClickInstall");
                break;
            case PatchState.NeedsDllUpdate:
                StateText = I18nService.Instance.T("State.NeedsDllUpdate");
                StateColor = "Orange";
                IsInstalled = true;
                CanInstall = true;
                CanUninstall = true;
                CanLaunch = true;
                StatusMessage = I18nService.Instance.T("Msg.NeedsUpdate");
                break;
            case PatchState.OldFilesFound:
                StateText = I18nService.Instance.T("State.OldFilesFound");
                StateColor = "Red";
                IsInstalled = false;
                CanInstall = true;
                CanUninstall = false;
                CanLaunch = false;
                StatusMessage = I18nService.Instance.T("Msg.WillCleanOld");
                break;
            case PatchState.OldPatchFound:
                StateText = I18nService.Instance.T("State.OldPatchFound");
                StateColor = "Orange";
                IsInstalled = false;
                CanInstall = true;
                CanUninstall = false;
                CanLaunch = false;
                StatusMessage = I18nService.Instance.T("Msg.WillUpgrade");
                break;
            case PatchState.GameUpdated:
                StateText = I18nService.Instance.T("State.GameUpdated");
                StateColor = "Red";
                IsInstalled = false;
                CanInstall = true;
                CanUninstall = false;
                CanLaunch = false;
                StatusMessage = I18nService.Instance.T("Msg.GameUpdated");
                break;
            default:
                StateText = I18nService.Instance.T("State.Unknown");
                StateColor = "Gray";
                CanInstall = false;
                CanUninstall = false;
                CanLaunch = false;
                break;
        }
    }

    /// <summary>一键下载所有 TLDLoader 资源（依赖 DLL + 核心资源合并为一个按钮）。</summary>
    [RelayCommand]
    private async Task DownloadAllResourcesAsync()
    {
        LogEntries.Clear();
        LogEntries.Add("==== 开始下载全部 TLDLoader 资源 ====");

        IsBusy = true;
        StatusMessage = I18nService.Instance.T("Msg.DownloadingAll");

        try
        {
            var progress = new Progress<string>(s =>
            {
                LogEntries.Add(s);
                StatusMessage = s;
                AppLog.Add(s);
            });

            // 1. 下载依赖 DLL
            LogEntries.Add("--- 1/2 下载依赖 DLL ---");
            await _patcher.DownloadDependenciesAsync(forceRefresh: true, progress);

            // 2. 下载核心资源
            LogEntries.Add("--- 2/2 下载核心资源 ---");
            await _patcher.DownloadCoreAssetsAsync(progress);

            LogEntries.Add("==== 全部资源下载完成 ====");
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.DownloadComplete"), I18nService.Instance.T("Msg.AllResourcesReady"));
        }
        catch (Exception ex)
        {
            LogEntries.Add($"==== 错误：{ex.Message} ====");
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.DownloadFailedShort"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            await RefreshStatusAsync();
        }
    }

    /// <summary>下载中文模组加载器 DLL（替换 TLDLoader.dll）。</summary>
    [RelayCommand]
    private async Task DownloadChineseLoaderAsync()
    {
        LogEntries.Clear();
        LogEntries.Add("==== 下载中文模组加载器 ====");

        IsBusy = true;
        StatusMessage = I18nService.Instance.T("Msg.DownloadingChinese");

        try
        {
            var progress = new Progress<string>(s =>
            {
                LogEntries.Add(s);
                StatusMessage = s;
                AppLog.Add(s);
            });
            await _patcher.DownloadChineseLoaderAsync(progress);
            LogEntries.Add("==== 中文加载器下载完成 ====");
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.DownloadComplete"),
                "中文 TLDLoader.dll 已下载。下次安装 TLDLoader 时将使用此中文版本。");
        }
        catch (Exception ex)
        {
            LogEntries.Add($"==== 错误：{ex.Message} ====");
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.DownloadFailedShort"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>检查并下载依赖 DLL。原 DownloadFiles 主动触发。</summary>
    [RelayCommand]
    private async Task DownloadDependenciesAsync()
    {
        LogEntries.Clear();
        LogEntries.Add("==== 开始下载/更新依赖 DLL ====");

        IsBusy = true;
        StatusMessage = I18nService.Instance.T("Msg.DownloadingDeps");

        try
        {
            var progress = new Progress<string>(s =>
            {
                LogEntries.Add(s);
                StatusMessage = s;
            });
            // forceRefresh=true 强制重新下载并比对版本
            await _patcher.DownloadDependenciesAsync(forceRefresh: true, progress);
            LogEntries.Add("==== 依赖 DLL 处理完成 ====");
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.DownloadComplete"), "依赖 DLL 已更新。");
        }
        catch (Exception ex)
        {
            LogEntries.Add($"==== 错误：{ex.Message} ====");
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.DownloadFailedShort"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            await RefreshStatusAsync();
        }
    }

    /// <summary>下载核心资源包。原 TLDPatcher.zip 下载。</summary>
    [RelayCommand]
    private async Task DownloadCoreAssetsAsync()
    {
        LogEntries.Clear();
        LogEntries.Add("==== 开始下载核心资源 ====");

        IsBusy = true;
        StatusMessage = I18nService.Instance.T("Msg.DownloadingCore");

        try
        {
            var progress = new Progress<string>(s =>
            {
                LogEntries.Add(s);
                StatusMessage = s;
            });
            await _patcher.DownloadCoreAssetsAsync(progress);
            LogEntries.Add("==== 核心资源下载完成 ====");
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.DownloadComplete"), "核心资源已就绪。");
        }
        catch (Exception ex)
        {
            LogEntries.Add($"==== 错误：{ex.Message} ====");
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.DownloadFailedShort"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>智能安装/更新。原 Starter + StartPatching。</summary>
    [RelayCommand]
    private async Task InstallOrUpdateAsync()
    {
        if (string.IsNullOrEmpty(_settings.TldPath) || !Directory.Exists(_settings.TldPath))
        {
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.NeedConfigPathShort"),
                I18nService.Instance.T("Msg.NeedConfigPath"));
            return;
        }

        // 检查游戏是否运行中（原 CheckRuntimeEnvironment）
        if (IsGameRunning())
        {
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.GameRunningShort"),
                I18nService.Instance.T("Msg.GameRunning"));
            return;
        }

        LogEntries.Clear();
        LogEntries.Add($"==== 开始操作：{_settings.TldPath} ====");

        IsBusy = true;
        CanInstall = false;
        CanUninstall = false;
        StatusMessage = I18nService.Instance.T("Msg.Operating");

        try
        {
            var progress = new Progress<string>(s =>
            {
                LogEntries.Add(s);
                StatusMessage = s;
            });
            await _patcher.SmartUpdateAsync(_settings.TldPath, progress);
            LogEntries.Add("==== 操作完成 ====");
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.OperationSuccess"), I18nService.Instance.T("Msg.OperationDone"));
        }
        catch (Exception ex)
        {
            LogEntries.Add($"==== 错误：{ex.Message} ====");
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.OperationFailed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            await RefreshStatusAsync();
        }
    }

    /// <summary>卸载。原 button3_Click。</summary>
    [RelayCommand]
    private async Task UninstallAsync()
    {
        if (string.IsNullOrEmpty(_settings.TldPath)) return;

        // 原始 exe 弹"您确定要从游戏中移除 TLDLoader 吗？"
        var result = await _dialogs.ShowConfirmAsync(I18nService.Instance.T("Msg.Confirm"),
            I18nService.Instance.T("Msg.UninstallConfirmLoader"));
        if (result != MessageBoxResult.OK) return;

        if (IsGameRunning())
        {
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.GameRunningShort"),
                I18nService.Instance.T("Msg.GameRunningUninstall"));
            return;
        }

        LogEntries.Clear();
        LogEntries.Add("==== 开始卸载 ====");

        IsBusy = true;
        CanInstall = false;
        CanUninstall = false;
        StatusMessage = I18nService.Instance.T("Msg.Uninstalling");

        try
        {
            var progress = new Progress<string>(s =>
            {
                LogEntries.Add(s);
                StatusMessage = s;
            });
            await _patcher.UninstallAsync(_settings.TldPath, progress);
            LogEntries.Add("==== 卸载完成 ====");
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.OperationSuccess"), I18nService.Instance.T("Msg.UninstalledLoader"));
        }
        catch (Exception ex)
        {
            LogEntries.Add($"==== 错误：{ex.Message} ====");
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.UninstallFailedShort"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            await RefreshStatusAsync();
        }
    }

    /// <summary>启动游戏。原 button4_Click + LaunchMSCsruSteam。</summary>
    [RelayCommand]
    private void LaunchGame()
    {
        try
        {
            // steam://rungameid/1017180
            Process.Start(new ProcessStartInfo
            {
                FileName = "steam://rungameid/1017180",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _ = _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.LaunchFailedShort"),
                I18nService.Instance.T("Msg.LaunchFailed", ex.Message));
        }
    }

    /// <summary>打开 Mods 目录。原 openfolder_Click。</summary>
    [RelayCommand]
    private void OpenModsFolder()
    {
        if (string.IsNullOrEmpty(_settings.TldPath)) return;
        var dir = Path.Combine(_settings.TldPath, "Mods");
        if (!Directory.Exists(dir))
        {
            // 也尝试 Documents\TheLongDrive\Mods（原 mdPath）
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TheLongDrive", "Mods");
        }
        if (Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        else
        {
            _ = _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.DirNotExistShort"),
                I18nService.Instance.T("Msg.DirNotExist"));
        }
    }

    /// <summary>选择/更改游戏路径。原 LetUserSelectPath + 自动检测。</summary>
    [RelayCommand]
    private async Task BrowsePathAsync()
    {
        var picked = _pickers.PickFolder(_settings.TldPath, "选择《The Long Drive》安装目录");
        if (string.IsNullOrEmpty(picked)) return;

        // 验证 TheLongDrive.exe 存在
        if (!File.Exists(Path.Combine(picked, "TheLongDrive.exe")))
        {
            await _dialogs.ShowErrorAsync(I18nService.Instance.T("Msg.PathNotFound"),
                I18nService.Instance.T("Msg.PathInvalid"));
            return;
        }

        _settings.TldPath = picked;
        _settings.Save();
        PathDetector.Save(picked);
        await RefreshStatusAsync();
    }

    /// <summary>自动检测游戏路径。原 FindTldPath。</summary>
    [RelayCommand]
    private async Task AutoDetectPathAsync()
    {
        StatusMessage = I18nService.Instance.T("Msg.ScanningSteam");
        var detected = await Task.Run(() => _pathDetector.TryDetect());
        if (!string.IsNullOrEmpty(detected))
        {
            _settings.TldPath = detected;
            _settings.Save();
            PathDetector.Save(detected);
            StatusMessage = I18nService.Instance.T("Msg.AutoDetected", detected);
            await RefreshStatusAsync();
        }
        else
        {
            StatusMessage = I18nService.Instance.T("Msg.GameNotFoundShort");
            await _dialogs.ShowInfoAsync(I18nService.Instance.T("Msg.GameNotFoundShortTitle"),
                I18nService.Instance.T("Msg.GameNotFoundShort"));
        }
    }

    private static bool IsGameRunning()
    {
        try { return Process.GetProcessesByName("TheLongDrive").Length > 0; }
        catch { return false; }
    }
}
