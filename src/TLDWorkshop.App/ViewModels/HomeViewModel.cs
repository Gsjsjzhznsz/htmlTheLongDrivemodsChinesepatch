using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TLDWorkshop.App.Services;
using TLDWorkshop.Core.Contracts;
using TLDWorkshop.Core.Models;
using TLDWorkshop.Core.Services;

namespace TLDWorkshop.App.ViewModels;

/// <summary>
/// 首页 ViewModel。简化版：只显示欢迎信息和当前状态卡片，
/// TLDLoader 安装/卸载/更新全部移到 TldLoaderPage。
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    private readonly IPathDetector _pathDetector;
    private readonly ITldPatcherExtended _patcher;
    private readonly AppSettings _settings;
    private readonly NavigationService _nav;

    [ObservableProperty] private string _tldPath = string.Empty;
    [ObservableProperty] private string _loaderStatus = "未检测";
    [ObservableProperty] private string _loaderVersion = "未安装";
    [ObservableProperty] private string _gameVersion = "未检测";

    public HomeViewModel(IPathDetector pathDetector, ITldPatcherExtended patcher,
        AppSettings settings, NavigationService nav)
    {
        _pathDetector = pathDetector;
        _patcher = patcher;
        _settings = settings;
        _nav = nav;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        TldPath = _settings.TldPath ?? "(未配置)";
        await RefreshStatusAsync();
    }

    [RelayCommand]
    public async Task RefreshStatusAsync()
    {
        if (string.IsNullOrEmpty(_settings.TldPath) || !Directory.Exists(_settings.TldPath))
        {
            LoaderStatus = "未配置游戏路径";
            LoaderVersion = "未安装";
            GameVersion = "未检测";
            return;
        }
        try
        {
            var state = await Task.Run(() => _patcher.CheckState(_settings.TldPath));
            LoaderStatus = state switch
            {
                PatchState.Patched => "已安装，运行正常",
                PatchState.NotPatched => "未安装",
                PatchState.NeedsDllUpdate => "需更新 TLDLoader.dll",
                PatchState.OldFilesFound => "发现 0.1 版残留",
                PatchState.OldPatchFound => "发现旧版补丁",
                PatchState.GameUpdated => "游戏已更新，补丁失效",
                _ => "未知状态",
            };
            try { LoaderVersion = _patcher.GetInstalledLoaderVersion(_settings.TldPath) ?? "未安装"; }
            catch { LoaderVersion = "未安装"; }
            try { GameVersion = _patcher.IsBetaVersion(_settings.TldPath) ? "Beta 测试版" : "正式版"; }
            catch { GameVersion = "未检测"; }
        }
        catch (Exception ex)
        {
            LoaderStatus = $"检测失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void GoTldLoader() => _nav.Navigate<Views.TldLoaderPage>();

    [RelayCommand]
    private void GoBrowse() => _nav.Navigate<Views.BrowsePage>();

    [RelayCommand]
    private void GoMyMods() => _nav.Navigate<Views.MyModsPage>();

    [RelayCommand]
    private void GoSettings() => _nav.Navigate<Views.SettingsPage>();

    [RelayCommand]
    private async Task AutoDetectAsync()
    {
        StatusMessage = I18nService.Instance.T("Msg.ScanningSteam");
        var detected = await Task.Run(() => _pathDetector.TryDetect());
        if (!string.IsNullOrEmpty(detected))
        {
            _settings.TldPath = detected;
            _settings.Save();
            PathDetector.Save(detected);
            TldPath = detected;
            StatusMessage = I18nService.Instance.T("Msg.AutoDetected", detected);
            await RefreshStatusAsync();
        }
        else
        {
            StatusMessage = I18nService.Instance.T("Msg.GameNotFound");
        }
    }
}
