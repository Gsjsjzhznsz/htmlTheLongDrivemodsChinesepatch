using TLDWorkshop.Core.Models;

namespace TLDWorkshop.Core.Contracts;

/// <summary>
/// mod 仓库接口。支持多源（官方/镜像/本地）+ 分别拉取 mod 列表和模组包列表。
/// </summary>
public interface IModRepository
{
    /// <summary>可用的 mod 列表数据源。</summary>
    IReadOnlyList<ModSource> ModSources { get; }

    /// <summary>可用的模组包列表数据源。</summary>
    IReadOnlyList<ModSource> ModpackSources { get; }

    /// <summary>从指定源拉取 mod 列表。</summary>
    Task<List<Mod>> FetchModsAsync(int sourceIndex = 0, CancellationToken ct = default);

    /// <summary>同时从所有源拉取并按 FileName 合并。</summary>
    Task<List<MergedMod>> FetchMergedModsAsync(CancellationToken ct = default);

    /// <summary>从指定源拉取模组包列表。</summary>
    Task<List<Modpack>> FetchModpacksAsync(int sourceIndex = 0, CancellationToken ct = default);

    /// <summary>
    /// 同时从所有源拉取模组包并按 Link 合并。
    /// 修复 Bug B：原实现只显示极狐源，且按 FileName 合并会丢失极狐源内部的同名 modpack。
    /// 改用 Link 作为合并键，因为 Link 是 .txt 清单文件 URL，每个 modpack 唯一。
    /// </summary>
    Task<List<MergedModpack>> FetchMergedModpacksAsync(CancellationToken ct = default);

    /// <summary>下载单个 mod 文件到指定路径。</summary>
    Task DownloadModAsync(Mod mod, string targetPath, IProgress<(long received, long total)>? progress, CancellationToken ct = default);

    /// <summary>下载 mod 预览图。</summary>
    Task<byte[]> DownloadImageAsync(string url, CancellationToken ct = default);

    /// <summary>下载模组包 .txt 清单文件，返回 DLL 文件名列表（每行一个，跳过空行和 # 注释）。</summary>
    Task<List<string>> DownloadModpackTxtAsync(string txtUrl, CancellationToken ct = default);
}
