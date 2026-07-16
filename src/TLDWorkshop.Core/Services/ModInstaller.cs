using System.IO.Compression;
using TLDWorkshop.Core.Contracts;

namespace TLDWorkshop.Core.Services;

/// <summary>
/// Mod 文件解压安装。对应原项目 <c>webClient_DownloadFileCompletedZip</c> 等。
/// 修改点：原代码用 Ionic.Zip，迁移到 System.IO.Compression（.NET 内置，免依赖）。
/// </summary>
public sealed class ModInstaller : IModInstaller
{
    public async Task InstallAsync(string downloadedZipPath, string modsDir, CancellationToken ct = default)
    {
        if (!File.Exists(downloadedZipPath))
            throw new FileNotFoundException("下载的 zip 文件不存在", downloadedZipPath);
        Directory.CreateDirectory(modsDir);

        await Task.Run(() =>
        {
            // 用 ZipFile 而不是 ZipArchive + 手动循环，自动处理子目录
            ZipFile.ExtractToDirectory(downloadedZipPath, modsDir, overwriteFiles: true);
        }, ct).ConfigureAwait(false);
    }

    public IReadOnlyList<InstalledMod> ListInstalled(string modsDir)
    {
        if (!Directory.Exists(modsDir)) return Array.Empty<InstalledMod>();
        var result = new List<InstalledMod>();
        foreach (var f in Directory.EnumerateFiles(modsDir, "*.dll"))
        {
            result.Add(new InstalledMod(
                FileName: Path.GetFileName(f),
                Enabled:  !f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase),
                FullPath: f));
        }
        foreach (var f in Directory.EnumerateFiles(modsDir, "*.disabled"))
        {
            result.Add(new InstalledMod(
                FileName: Path.GetFileName(f),
                Enabled:  false,
                FullPath: f));
        }
        return result;
    }
}
