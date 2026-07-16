using TLDWorkshop.Core.Models;

namespace TLDWorkshop.Core.Contracts;

/// <summary>
/// 路径检测策略。原项目通过硬编码注册表/Steam 路径探测 TheLongDrive 安装位置，
/// 此接口抽象出来便于单测注入。
/// </summary>
public interface IPathDetector
{
    /// <summary>尝试自动检测 TLD 安装目录，返回 null 表示未找到。</summary>
    string? TryDetect();

    /// <summary>让用户手动选择目录，返回选中的路径或 null。</summary>
    Task<string?> LetUserSelectAsync();
}

// IModRepository 已移到独立文件 IModRepository.cs

public interface ITldPatcher
{
    /// <summary>检查 TLDLoader 是否已注入到 Assembly-CSharp.dll。</summary>
    bool IsPatched(string tldPath);

    /// <summary>安装 TLDLoader（全新安装，6 步流程）。</summary>
    Task InstallAsync(string tldPath, IProgress<string>? log, CancellationToken ct = default);

    /// <summary>卸载 TLDLoader，恢复 .backup。</summary>
    Task UninstallAsync(string tldPath, IProgress<string>? log, CancellationToken ct = default);
}

/// <summary>扩展接口：TLDLoader 状态机 + 智能更新。新 WPF 版独有。</summary>
public interface ITldPatcherExtended : ITldPatcher
{
    /// <summary>检测当前补丁状态（6 种）。</summary>
    PatchState CheckState(string tldPath);

    /// <summary>智能更新：根据当前状态自动选择最小操作。</summary>
    Task SmartUpdateAsync(string tldPath, IProgress<string>? log, CancellationToken ct = default);

    /// <summary>检测游戏是否为 beta 版本。</summary>
    bool IsBetaVersion(string tldPath);

    /// <summary>读取已安装 TLDLoader.dll 的版本号。</summary>
    string? GetInstalledLoaderVersion(string tldPath);

    /// <summary>从 GitLab 下载 TLDLoader 相关依赖 DLL。</summary>
    Task DownloadDependenciesAsync(bool forceRefresh, IProgress<string>? log, CancellationToken ct = default);

    /// <summary>下载中文模组加载器 DLL（替换 TLDLoader.dll）。</summary>
    Task DownloadChineseLoaderAsync(IProgress<string>? log, CancellationToken ct = default);

    /// <summary>下载并解压 TLDPatcher.zip（含 core.unity3d 等核心资源）。</summary>
    Task DownloadCoreAssetsAsync(IProgress<string>? log, CancellationToken ct = default);
}

public interface IModInstaller
{
    /// <summary>把下载的 mod zip 解压到 TLD 的 Mods 目录。</summary>
    Task InstallAsync(string downloadedZipPath, string modsDir, CancellationToken ct = default);

    /// <summary>列出已安装的 mod（通过扫描 Mods 目录）。</summary>
    IReadOnlyList<InstalledMod> ListInstalled(string modsDir);
}

public interface IUpdateChecker
{
    /// <summary>检查应用自身更新。原 <c>vcontrol</c> 方法。</summary>
    Task<VersionInfo> CheckAsync(CancellationToken ct = default);
}

public readonly record struct InstalledMod(string FileName, bool Enabled, string FullPath);
