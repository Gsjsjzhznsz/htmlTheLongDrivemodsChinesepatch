using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TLDWorkshop.App.Services;
using TLDWorkshop.Core.Contracts;
using TLDWorkshop.Core.Models;
using TLDWorkshop.Core.Services;

namespace TLDWorkshop.App.ViewModels;

/// <summary>
/// 主窗口 ViewModel。
///
/// 修复 Bug #5 + 用户要求"启动时不要弹窗提示选路径"：
/// - 启动时只做静默自动检测，不弹任何 MessageBox
/// - 路径未找到也不打断用户使用，让用户自己进设置页或 TLDLoader 页配置
/// - 启动时仍可后台启动更新检查（失败不弹窗）
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IPathDetector _pathDetector;
    private readonly IUpdateChecker _updateChecker;
    private readonly AppSettings _settings;
    private readonly NavigationService _nav;

    public MainViewModel(IPathDetector pathDetector, IUpdateChecker updateChecker,
        AppSettings settings, NavigationService nav)
    {
        _pathDetector = pathDetector;
        _updateChecker = updateChecker;
        _settings = settings;
        _nav = nav;
    }

    /// <summary>窗口 Loaded 时调用。原 Form1_Load（原本是空的）+ CheckRuntimeEnvironment + RunPathDetectionFlow。</summary>
    public async Task InitializeAsync()
    {
        try
        {
            // 静默自动检测路径，不弹窗
            // 1) 优先用持久化保存的路径
            var saved = PathDetector.LoadSaved();
            if (!string.IsNullOrEmpty(saved))
            {
                _settings.TldPath = saved;
            }
            else
            {
                // 2) 自动扫描 Steam 库
                var detected = _pathDetector.TryDetect();
                if (!string.IsNullOrEmpty(detected))
                {
                    _settings.TldPath = detected;
                    PathDetector.Save(detected);
                }
                // 找不到也不弹窗，让用户自己进设置/TLDLoader 页面配置
            }

            // 后台检查应用更新，失败静默
            if (_settings.CheckUpdatesOnStart)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var info = await _updateChecker.CheckAsync();
                        if (info.HasUpdate)
                        {
                            // 不主动弹窗，让用户在 TLDLoader 页面看到提示
                        }
                    }
                    catch { /* 更新检查失败不阻塞主流程 */ }
                });
            }
        }
        catch (Exception ex)
        {
            // Bug #7 修复：异常显式上报，但这里不再用 DialogService（避免循环依赖）
            // 只写到状态栏
            StatusMessage = I18nService.Instance.T("Msg.InitFailed", ex.Message);
        }
        await Task.CompletedTask;
    }
}
