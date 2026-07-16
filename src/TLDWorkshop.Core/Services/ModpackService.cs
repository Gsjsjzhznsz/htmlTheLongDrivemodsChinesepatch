using System.IO.Compression;
using TLDWorkshop.Core.Contracts;
using TLDWorkshop.Core.Models;

namespace TLDWorkshop.Core.Services;

/// <summary>
/// 模组包服务。对应 Python 项目的 install-modpack / import-modpack / export-modpack API。
///
/// 模组包是 .txt 文件，每行一个 DLL 文件名。安装时：
///   1. 读取 .txt → 获取 DLL 文件名列表
///   2. From mod 列表（modlist_3.json）中查找每个 DLL 对应的 Mod 元数据
///   3. 逐个下载并安装（如果是 .zip 则解压，如果是 .dll 则直接复制）
///   4. 支持自动安装依赖（Dependency 字段递归解析）
/// </summary>
public sealed class ModpackService
{
    private readonly IModRepository _repo;

    public ModpackService(IModRepository repo) => _repo = repo;

    /// <summary>
    /// 安装在线模组包。对应 Python api_install_modpack。
    /// 下载 modpack.Link 指向的 .txt → 逐行读 DLL 文件名 → 批量安装。
    /// </summary>
    public async Task<List<string>> InstallOnlineModpackAsync(Modpack pack, int modSourceIndex,
        string modsDir, IProgress<string>? log, CancellationToken ct = default)
    {
        log?.Report($"Downloading modpack manifest: {pack.Link}");
        var filenames = await _repo.DownloadModpackTxtAsync(pack.Link, ct);
        log?.Report($"Manifest contains {filenames.Count} mods");

        var allMods = await _repo.FetchModsAsync(modSourceIndex, ct);
        return await BatchInstallAsync(filenames, allMods, modsDir, log, ct);
    }

    /// <summary>
    /// From本地 .txt 文件导入模组包。对应 Python api_import_modpack。
    /// </summary>
    public async Task<List<string>> ImportLocalModpackAsync(string txtPath, int modSourceIndex,
        string modsDir, IProgress<string>? log, CancellationToken ct = default)
    {
        if (!File.Exists(txtPath))
            throw new FileNotFoundException($"文件不存在：{txtPath}");

        var content = await File.ReadAllTextAsync(txtPath, ct);
        var filenames = ModRepository.ParseTxtFileNames(content);
        log?.Report($"From {Path.GetFileName(txtPath)} read {filenames.Count} mods");

        var allMods = await _repo.FetchModsAsync(modSourceIndex, ct);
        return await BatchInstallAsync(filenames, allMods, modsDir, log, ct);
    }

    /// <summary>
    /// 解析 mod 的依赖。对应原 exe downloadmod IL_01eb-IL_02aa。
    ///
    /// 重要发现（原 exe IL 反编译确认）：
    ///   Dependency 字段存的是依赖模组的 **Link**（下载链接），不是 Name！
    ///   原 exe 遍历 modlistjson.ModList，找 mod.Link == selectedMod.Dependency 的那mods。
    /// </summary>
    public List<Mod> ResolveDependencies(Mod mod, List<Mod> allMods, HashSet<string>? resolved = null)
    {
        resolved ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Mod>();

        if (string.IsNullOrWhiteSpace(mod.Dependency))
            return result;

        // 按 Link 匹配依赖（原 exe 逻辑）
        var depMod = allMods.FirstOrDefault(m =>
            string.Equals(m.Link, mod.Dependency, StringComparison.OrdinalIgnoreCase));
        if (depMod == null) return result;

        if (resolved.Contains(depMod.Link)) return result;  // 避免循环
        resolved.Add(depMod.Link);

        result.Add(depMod);
        // 递归解析依赖的依赖
        result.AddRange(ResolveDependencies(depMod, allMods, resolved));
        return result;
    }

    /// <summary>
    /// 安装单mods 并自动安装其依赖。对应 Python install_mod_with_deps。
    /// </summary>
    public async Task<(bool Success, string Message, List<string> DepResults)> InstallWithDepsAsync(
        Mod mod, List<Mod> allMods, string modsDir, IProgress<string>? log, CancellationToken ct = default)
    {
        var depResults = new List<string>();
        var deps = ResolveDependencies(mod, allMods);

        // 先安装依赖
        foreach (var dep in deps)
        {
            try
            {
                log?.Report($"  Installing dependency: {dep.Name}...");
                await InstallSingleModAsync(dep, modsDir, log, ct);
                depResults.Add($"[Dep] {dep.Name} v{dep.Version}");
            }
            catch (Exception ex)
            {
                depResults.Add($"[Dep Failed] {dep.Name}: {ex.Message}");
            }
        }

        // 安装主 mod
        try
        {
            await InstallSingleModAsync(mod, modsDir, log, ct);
            return (true, mod.Version, depResults);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, depResults);
        }
    }

    /// <summary>
    /// 批量安装 mod 列表（含依赖自动安装）。对应 Python batch_install_mods。
    /// 使用 Parallel.ForEachAsync 实现并发下载，最大 4 个并发。
    /// </summary>
    public async Task<List<string>> BatchInstallAsync(List<string> filenames, List<Mod> allMods,
        string modsDir, IProgress<string>? log, CancellationToken ct = default)
    {
        Directory.CreateDirectory(modsDir);
        var results = new System.Collections.Concurrent.ConcurrentBag<string>();
        var completed = 0;
        var total = filenames.Count;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(filenames.Where(f => !string.IsNullOrWhiteSpace(f)), options, async (fn, token) =>
        {
            var idx = System.Threading.Interlocked.Increment(ref completed);
            fn = fn.Trim();
            log?.Report($"[{idx}/{total}] Installing {fn}...");

            var mod = allMods.FirstOrDefault(m =>
                string.Equals(m.FileName, fn, StringComparison.OrdinalIgnoreCase));
            if (mod == null)
            {
                mod = allMods.FirstOrDefault(m =>
                    string.Equals(Path.GetFileName(m.FileName), Path.GetFileName(fn),
                        StringComparison.OrdinalIgnoreCase));
            }
            if (mod == null)
            {
                results.Add($"[Not Found] {fn}: not found in mod list");
                log?.Report($"  [Not Found] {fn}");
                return;
            }

            // 安装主 mod + 依赖
            var (ok, msg, depResults) = await InstallWithDepsAsync(mod, allMods, modsDir, log, token);
            foreach (var dr in depResults) results.Add(dr);

            if (ok)
            {
                results.Add($"[OK] {mod.Name} v{msg}");
                log?.Report($"  [OK] {mod.Name}");
            }
            else
            {
                results.Add($"[Failed] {mod.Name}: {msg}");
                log?.Report($"  [Failed] {mod.Name}: {msg}");
            }
        });

        return results.ToList();
    }

    /// <summary>
    /// 安装单mods。
    /// 严格照搬原 exe 逻辑（downloadmod IL + webClient_DownloadFileCompletedZip）：
    ///   - .dll 链接：直接下载到 Mods/{FileName}
    ///   - .zip 链接：下载到 temp，然后 ExtractAll(Mods, overwrite=true)——直接解压到 Mods 根目录，保留 zip 内部完整目录结构
    /// </summary>
    private async Task InstallSingleModAsync(Mod mod, string modsDir,
        IProgress<string>? log, CancellationToken ct)
    {
        // 下载到临时文件
        var tempPath = Path.Combine(Path.GetTempPath(), $"tld_mod_{Guid.NewGuid():N}");
        try
        {
            await _repo.DownloadModAsync(mod, tempPath, null, ct);

            if (mod.Link.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                IsZipFile(tempPath))
            {
                // zip：直接解压到 Mods 根目录（原 exe ExtractAll(Mods, overwrite=true)）
                // 保留 zip 内部完整目录结构——如果 zip 里有 Assets/foo.png 就解压成 Mods/Assets/foo.png
                log?.Report($"  Extracting {mod.FileName} -> Mods/...");
                using var archive = System.IO.Compression.ZipFile.OpenRead(tempPath);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;  // 跳过纯目录条目
                    // 用 FullName 保留目录结构
                    var dst = Path.Combine(modsDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    var dir = Path.GetDirectoryName(dst);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    // 路径穿越校验：禁止解压到 Mods 目录之外
                    var fullDst = Path.GetFullPath(dst);
                    var fullModsDir = Path.GetFullPath(modsDir) + Path.DirectorySeparatorChar;
                    if (!fullDst.StartsWith(fullModsDir, StringComparison.OrdinalIgnoreCase))
                    {
                        log?.Report($"  ! Skipping unsafe path: {entry.FullName}");
                        continue;
                    }

                    entry.ExtractToFile(dst, overwrite: true);
                }
            }
            else
            {
                // dll：直接复制到 Mods 根目录
                var targetPath = Path.Combine(modsDir, mod.FileName);
                if (File.Exists(targetPath))
                {
                    File.SetAttributes(targetPath, FileAttributes.Normal);
                    File.Delete(targetPath);
                }
                File.Copy(tempPath, targetPath, overwrite: true);
            }
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>检测文件是否是 zip（读 magic bytes）。</summary>
    private static bool IsZipFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[4];
            return fs.Read(buf, 0, 4) == 4 && buf[0] == 0x50 && buf[1] == 0x4B;
        }
        catch { return false; }
    }

    /// <summary>
    /// 导出已安装 mod 列表为 .txt 文件。对应 Python api_export_modpack。
    /// 扫描 Mods 目录下的 .dll 文件名，每行一个写入 txt。
    /// </summary>
    public async Task ExportModpackAsync(string txtPath, string modsDir, CancellationToken ct = default)
    {
        if (!Directory.Exists(modsDir))
            throw new DirectoryNotFoundException($"Mods 目录不存在：{modsDir}");

        var dlls = Directory.GetFiles(modsDir, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Select(n => n!)
            .ToList();

        await File.WriteAllLinesAsync(txtPath, dlls, ct);
    }
}
