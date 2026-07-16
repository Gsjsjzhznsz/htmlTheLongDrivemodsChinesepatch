using System.Diagnostics;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TLDWorkshop.Core.Contracts;
using TLDWorkshop.Core.Models;

namespace TLDWorkshop.Core.Services;

/// <summary>
/// TLDLoader 注入器。对应原项目 TLDPatcher.Patcher + Form1 的 PatchStarter/StartPatching/PatchThis/PatchThisLast/CheckPatchStatus/IsPatched/CopyReferences/CopyCoreAssets/DeleteReferences 全套逻辑。
///
/// 6 种补丁状态（原 CheckPatchStatus 字段）：
/// - Patched: Assembly-CSharp.dll 含 InitMainMenu 调用，DLL MD5 匹配
/// - NotPatched: 从未安装
/// - NeedsDllUpdate: 已打补丁但 TLDLoader.dll MD5 与本工具自带版本不一致
/// - OldFilesFound: 发现 0.1 版残留（Assembly-CSharp.original.dll）
/// - OldPatchFound: 发现旧版补丁痕迹
/// - GameUpdated: TLDLoader.dll + Assembly-CSharp.dll.backup 都在但没补丁（游戏被更新了）
/// </summary>
public sealed class TldPatcher : ITldPatcherExtended
{
    // ----- 常量（来自原始 exe IL 反编译）-----
    public const string LoaderAssemblyName = "TLDLoader";
    public const string LoaderDllFileName = "TLDLoader.dll";
    public const string LoaderMdbFileName = "TLDLoader.dll.mdb";
    public const string LoaderPdbFileName = "TLDLoader.pdb";
    public const string IonicZipFileName = "Ionic.Zip.dll";
    public const string UAudioFileName = "uAudio.dll";
    public const string GameDllName = "Assembly-CSharp.dll";
    public const string BackupSuffix = ".backup";
    public const string OldVersionBackupSuffix = ".original.dll";

    // 补丁目标签名（非 beta 版）
    public const string ReleaseTargetType = "mainmenuscript";
    public const string ReleaseTargetMethod = "Start";
    // 补丁目标签名（beta 版）
    public const string BetaTargetType = "menuhandler";
    public const string BetaTargetMethod = "FStart";
    // 第二处补丁（物品数据库）
    public const string DbTargetType = "itemdatabase";
    public const string DbTargetMethod = "Awake";

    public const string LoaderClassName = "TLDLoader.ModLoader";
    public const string InitMethodName = "InitMainMenu";
    public const string DbInitMethodName = "dbInit";

    // 路径片段
    public const string ManagedSubPath = "TheLongDrive_Data\\Managed";
    public const string CoreDataSubPath = "Assets\\TLDLoader_Core";
    public const string CoreAssetFileName = "core.unity3d";
    public const string SettingsDataSubPath = "Assets\\TLDLoader_Settings";
    public const string SettingsAssetFileName = "settingsui.unity3d";

    // ----- 依赖 DLL 下载相关（来自 DownloadFiles 方法 IL 反编译）-----
    public const string HarmonyDllFileName = "0Harmony.dll";
    public const string MonoCecilDllFileName = "Mono.Cecil.dll";
    public const string ExpectedLoaderVersion = "2.4.0.0";
    public const string ExpectedHarmonyVersion = "2.3.3.0";

    /// <summary>GitLab 下载 base URL（原 DownloadFiles 用 XLDev 仓库）。</summary>
    public const string GitLabDownloadBaseUrl =
        "https://gitlab.com/XLDev/workshop-tld/-/raw/" + ModRepository.Branch + "/Workshop/";

    // 旧版 DownloadFiles 的 URL（每个 DLL 来自不同仓库）：
    //   TLDLoader.dll  → gitlab.com/XLDev/workshop-tld
    //   Mono.Cecil.dll → gitlab.com/XLDev/tldcn
    //   0Harmony.dll   → gitlab.com/KolbenLP/WorkshopTLDMods
    public const string LoaderDownloadUrl  = GitLabDownloadBaseUrl + "TLDLoader.dll";
    public const string CecilDownloadUrl   =
        "https://gitlab.com/XLDev/tldcn/-/raw/" + ModRepository.Branch + "/Workshop/Mono.Cecil.dll";
    public const string HarmonyDownloadUrl =
        "https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/" + ModRepository.Branch + "/Workshop/0Harmony.dll";

    /// <summary>中文模组加载器 DLL 下载 URL（用户 GitHub 仓库）。</summary>
    public const string ChineseLoaderDownloadUrl =
        "https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch/raw/refs/heads/main/TLDLoader.dll";

    /// <summary>TLDPatcher zip 下载地址（包含 TLDLoader_Core 等资源）。</summary>
    public const string PatcherZipUrl =
        "https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/" + ModRepository.Branch + "/Workshop/TLDPatcher.zip";

    /// <summary>应用自身更新器下载地址。</summary>
    public const string UpdaterExeUrl =
        "https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/" + ModRepository.Branch + "/Workshop/TLDLoaderLauncher.exe?inline=false";

    /// <summary>版本检查 URL。</summary>
    public const string VersionCheckUrl =
        "https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/" + ModRepository.Branch + "/Workshop/versioncheck.txt?inline=false";

    // ----- 状态枚举见 Models/PatchState.cs -----

    // ----- 简化的 IsPatched -----
    public bool IsPatched(string tldPath) => CheckState(tldPath) == PatchState.Patched;

    public PatchState CheckState(string tldPath)
    {
        if (string.IsNullOrEmpty(tldPath)) return PatchState.NoPath;

        var managedDir = Path.Combine(tldPath, ManagedSubPath);
        var gameDll = Path.Combine(managedDir, GameDllName);
        if (!File.Exists(gameDll)) return PatchState.NotPatched;

        // 检查 0.1 版残留
        var oldVersionBackup = Path.Combine(managedDir, GameDllName.Replace(".dll", OldVersionBackupSuffix));
        if (File.Exists(oldVersionBackup)) return PatchState.OldFilesFound;

        // 检查是否已打补丁
        bool patched;
        try { patched = ScanForPatch(gameDll); }
        catch { patched = false; }

        var backupFile = gameDll + BackupSuffix;
        var loaderDllInManaged = Path.Combine(managedDir, LoaderDllFileName);

        if (!patched)
        {
            // 未打补丁但发现 TLDLoader.dll + backup -> 游戏被更新
            if (File.Exists(loaderDllInManaged) && File.Exists(backupFile))
                return PatchState.GameUpdated;
            return PatchState.NotPatched;
        }

        // 已打补丁，检查 DLL MD5 是否匹配
        var localLoaderDll = Path.Combine(AppContext.BaseDirectory, LoaderDllFileName);
        if (File.Exists(localLoaderDll) && File.Exists(loaderDllInManaged))
        {
            try
            {
                var localMd5 = MD5HashFile(localLoaderDll);
                var managedMd5 = MD5HashFile(loaderDllInManaged);
                if (!string.Equals(localMd5, managedMd5, StringComparison.OrdinalIgnoreCase))
                {
                    // MD5 不匹配，但可能是用户下载了中文版 TLDLoader.dll
                    // 只有当本地 TLDLoader.dll 和游戏里的不一样时才报 NeedsDllUpdate
                    // 但如果游戏里的 TLDLoader.dll 和本地的一样，就是 Patched
                    // 这里简化：如果 MD5 不匹配就报 NeedsDllUpdate（用户可以选择"更新"来同步）
                    return PatchState.NeedsDllUpdate;
                }
            }
            catch { /* MD5 比较失败不致命 */ }
        }
        return PatchState.Patched;
    }

    /// <summary>检测游戏版本是否为 beta。原 CheckGameVersion 逻辑。</summary>
    public bool IsBetaVersion(string tldPath)
    {
        try
        {
            var newsFile = Path.Combine(tldPath, "TheLongDrive_Data", "news.txt");
            if (!File.Exists(newsFile)) return false;
            var content = File.ReadAllText(newsFile);
            return content.Contains("V2024.10.18b_test:");
        }
        catch { return false; }
    }

    /// <summary>读取已安装 TLDLoader.dll 的版本号。原 CheckTldLoaderVersion 逻辑。</summary>
    public string? GetInstalledLoaderVersion(string tldPath)
    {
        try
        {
            var loaderDll = Path.Combine(tldPath, ManagedSubPath, LoaderDllFileName);
            if (!File.Exists(loaderDll)) return null;
            var info = FileVersionInfo.GetVersionInfo(loaderDll);
            if (info.FileBuildPart > 0)
                return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
            return $"{info.FileMajorPart}.{info.FileMinorPart}";
        }
        catch { return null; }
    }

    /// <summary>
    /// 从 GitLab 下载 TLDLoader 相关依赖 DLL。照抄 长途中文车间最新修复版 DownloadFiles。
    /// 关键：0Harmony.dll 下载后必须复制到游戏 Managed 目录（游戏运行时需要）。
    /// </summary>
    public async Task DownloadDependenciesAsync(bool forceRefresh, IProgress<string>? log,
        CancellationToken ct = default)
    {
        var exeDir = AppContext.BaseDirectory;
        var localLoaderPath = Path.Combine(exeDir, LoaderDllFileName);
        var localHarmonyPath = Path.Combine(exeDir, HarmonyDllFileName);
        var localCecilPath = Path.Combine(exeDir, MonoCecilDllFileName);

        using var http = new HttpClient(new HttpClientHandler
        {
            Credentials = new System.Net.NetworkCredential("admin", "1331"),
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
        });
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        http.Timeout = TimeSpan.FromMinutes(5);

        // 1. 下载 0Harmony.dll（如果不存在或强制刷新）
        if (forceRefresh || !File.Exists(localHarmonyPath))
        {
            log?.Report("[Download] Downloading 0Harmony.dll...");
            try
            {
                var bytes = await http.GetByteArrayAsync(HarmonyDownloadUrl, ct);
                await File.WriteAllBytesAsync(localHarmonyPath, bytes, ct);
                log?.Report("[Download] 0Harmony.dll downloaded");
            }
            catch (Exception ex)
            {
                log?.Report($"[Download] 0Harmony.dll failed: {ex.Message}");
            }
        }

        // 2. 下载 TLDLoader.dll（如果不存在或强制刷新）
        // 但如果已有中文版 TLDLoader.dll，不覆盖
        if (forceRefresh || !File.Exists(localLoaderPath))
        {
            if (forceRefresh && File.Exists(localLoaderPath))
            {
                // 检查是否是中文版（< 400KB），如果是则不删除
                var fi = new FileInfo(localLoaderPath);
                if (fi.Length < 400000)
                {
                    log?.Report("Chinese TLDLoader.dll detected, skipping re-download");
                }
                else
                {
                    log?.Report("[Download] Downloading TLDLoader.dll...");
                    var bytes = await http.GetByteArrayAsync(LoaderDownloadUrl, ct);
                    await File.WriteAllBytesAsync(localLoaderPath, bytes, ct);
                    log?.Report("[Download] TLDLoader.dll downloaded");
                }
            }
            else
            {
                log?.Report("[Download] Downloading TLDLoader.dll...");
                try
                {
                    var bytes = await http.GetByteArrayAsync(LoaderDownloadUrl, ct);
                    await File.WriteAllBytesAsync(localLoaderPath, bytes, ct);
                    log?.Report("[Download] TLDLoader.dll downloaded");
                }
                catch (Exception ex)
                {
                    log?.Report($"[Download] TLDLoader.dll failed: {ex.Message}");
                    throw new InvalidOperationException($"Download TLDLoader.dll failed: {ex.Message}", ex);
                }
            }
        }

        // 3. 下载 Mono.Cecil.dll（如果本地不存在）
        if (!File.Exists(localCecilPath))
        {
            log?.Report("[Download] Downloading Mono.Cecil.dll...");
            try
            {
                var bytes = await http.GetByteArrayAsync(CecilDownloadUrl, ct);
                await File.WriteAllBytesAsync(localCecilPath, bytes, ct);
                log?.Report("[Download] Mono.Cecil.dll downloaded");
            }
            catch (Exception ex)
            {
                log?.Report($"[Download] Mono.Cecil.dll failed: {ex.Message}");
            }
        }
    }


    /// <summary>下载中文模组加载器 DLL（替换 exe 同目录的 TLDLoader.dll）。</summary>
    public async Task DownloadChineseLoaderAsync(IProgress<string>? log, CancellationToken ct = default)
    {
        var exeDir = AppContext.BaseDirectory;
        var loaderPath = Path.Combine(exeDir, LoaderDllFileName);

        log?.Report("[Chinese Loader] Downloading Chinese TLDLoader.dll...");
        try
        {
            using var http = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            });
            http.Timeout = TimeSpan.FromMinutes(5);
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var bytes = await http.GetByteArrayAsync(ChineseLoaderDownloadUrl, ct);
            await File.WriteAllBytesAsync(loaderPath, bytes, ct);
            log?.Report("【中文加载器】中文 TLDLoader.dll download complete！");
            log?.Report($"  Saved to: {loaderPath}");
            log?.Report("  Will be used on next TLDLoader install.");
        }
        catch (Exception ex)
        {
            log?.Report($"【中文加载器】download failed：{ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 下载 TLDPatcher.zip 并解压到 exe 同目录的 Extract/ 子目录。
    /// 这个 zip 包含 core.unity3d、settingsui.unity3d 等核心资源文件，
    /// 后续 CopyCoreAssets 会从这里复制到游戏目录。
    /// </summary>
    public async Task DownloadCoreAssetsAsync(IProgress<string>? log, CancellationToken ct = default)
    {
        var exeDir = AppContext.BaseDirectory;
        var zipPath = Path.Combine(exeDir, "TLDPatcher_downloaded.zip");
        var extractDir = Path.Combine(exeDir, "Extract");

        log?.Report("[Assets] Downloading TLDPatcher.zip...");
        try
        {
            using var http = new HttpClient(new HttpClientHandler
            {
                Credentials = new System.Net.NetworkCredential("admin", "1331"),
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            });
            http.Timeout = TimeSpan.FromMinutes(10);

            // 下载 zip
            using var resp = await http.GetAsync(PatcherZipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using (var fs = File.Create(zipPath))
            {
                await resp.Content.CopyToAsync(fs, ct);
            }
            log?.Report("【资源下载】TLDPatcher.zip download complete");

            // 解压到 Extract 目录
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
            Directory.CreateDirectory(extractDir);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);
            log?.Report($"[Assets] Extracted to {extractDir}");

            // 清理 zip
            try { File.Delete(zipPath); } catch { /* 忽略 */ }
        }
        catch (Exception ex)
        {
            log?.Report($"[Assets] Download/extract failed: {ex.Message}");
            throw;
        }
    }

    // ----- 安装/卸载/更新 -----
    // 严格照抄 长途中文车间最新修复版 TLDPatcher.Form1 的 PatchStarter/StartPatching 逻辑
    public async Task InstallAsync(string tldPath, IProgress<string>? log, CancellationToken ct = default)
    {
        var managedDir = Path.Combine(tldPath, ManagedSubPath);
        var assembly = Path.Combine(managedDir, GameDllName);
        var backup = assembly + BackupSuffix;

        // 1. 确保有 TLDLoader.dll（exe 同目录）
        var loaderSrc = Path.Combine(AppContext.BaseDirectory, LoaderDllFileName);
        if (!File.Exists(loaderSrc))
        {
            log?.Report("TLDLoader.dll not found, downloading...");
            try { await DownloadDependenciesAsync(false, log, ct); }
            catch (Exception ex) { log?.Report($"Download failed: {ex.Message}"); }
            if (!File.Exists(loaderSrc))
                throw new FileNotFoundException($"TLDLoader.dll not found: {loaderSrc}");
        }

        // 2. 确保有 Extract 目录（核心资源）
        var extractCorePath = Path.Combine(AppContext.BaseDirectory, "Extract", "TLDLoader_Core", CoreAssetFileName);
        if (!File.Exists(extractCorePath))
        {
            log?.Report("Core assets missing, downloading...");
            try { await DownloadCoreAssetsAsync(log, ct); }
            catch (Exception ex) { log?.Report($"Download failed: {ex.Message}"); }
        }

        // === StartPatching 逻辑 ===
        // 3. CopyReferences: 先删除旧文件，再从 exe 同目录复制
        log?.Report("Copying references...");
        CopyReferences(tldPath, log);

        // 4. 备份 Assembly-CSharp.dll
        log?.Report($"Backing up {GameDllName}...");
        if (File.Exists(backup)) SafeRemove(backup, log);
        File.Copy(assembly, backup, overwrite: true);
        log?.Report($"  Created {GameDllName}{BackupSuffix}");

        // 5. PatchThis: 注入 InitMainMenu 到 mainmenuscript.Start（非beta）或 menuhandler.FStart（beta）
        var isBeta = IsBetaVersion(tldPath);
        log?.Report($"Patching... (beta={isBeta})");
        try
        {
            var targetType = isBeta ? BetaTargetType : ReleaseTargetType;
            var targetMethod = isBeta ? BetaTargetMethod : ReleaseTargetMethod;
            PatchThis(managedDir, GameDllName, targetType, targetMethod,
                LoaderDllFileName, LoaderClassName, InitMethodName,
                InsertPosition.BeforeFirst);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error patching loader: {ex.Message}", ex);
        }

        // 6. PatchThisLast: 注入 dbInit 到 itemdatabase.Awake（仅非beta）
        if (!isBeta)
        {
            try
            {
                PatchThis(managedDir, GameDllName, DbTargetType, DbTargetMethod,
                    LoaderDllFileName, LoaderClassName, DbInitMethodName,
                    InsertPosition.AfterSecondToLast);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error patching itemdatabase: {ex.Message}", ex);
            }
        }

        // 7. CopyCoreAssets: 复制 core.unity3d + settingsui.unity3d 到 Mods/Assets/
        var modPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TheLongDrive", "Mods");
        Directory.CreateDirectory(modPath);
        log?.Report("Copying core assets...");
        CopyCoreAssets(modPath, isBeta, log);

        log?.Report("Install complete.");
        await Task.CompletedTask;
    }

    public async Task UninstallAsync(string tldPath, IProgress<string>? log, CancellationToken ct = default)
    {
        log?.Report("Removing TLDLoader from game");

        var managedDir = Path.Combine(tldPath, ManagedSubPath);
        var gameDll = Path.Combine(managedDir, GameDllName);
        var backupFile = gameDll + BackupSuffix;

        if (!File.Exists(backupFile))
            throw new FileNotFoundException(
                "Error! Backup file not found. Please verify game file integrity in Steam.");

        // 删除当前 dll，恢复 backup
        if (File.Exists(gameDll))
        {
            SafeRemove(gameDll, log);
        }
        File.Move(backupFile, gameDll);
        log?.Report($"正在恢复.....{GameDllName}{BackupSuffix}");

        // 清理 Loader dll + 依赖
        DeleteReferences(tldPath, log);

        log?.Report("TLDLoader removed successfully!");
        await Task.CompletedTask;
    }

    /// <summary>智能更新：照抄 长途中文车间最新修复版 TLDPatcher.Form1.PatchStarter 逻辑。</summary>
    public async Task SmartUpdateAsync(string tldPath, IProgress<string>? log, CancellationToken ct = default)
    {
        var state = CheckState(tldPath);
        var managedDir = Path.Combine(tldPath, ManagedSubPath);
        var assembly = Path.Combine(managedDir, GameDllName);
        var backup = assembly + BackupSuffix;
        var legacyBackup = Path.Combine(managedDir, GameDllName.Replace(".dll", OldVersionBackupSuffix));

        log?.Report($"Current state: {state}");

        switch (state)
        {
            case PatchState.Patched:
                log?.Report("Newest patch found, no need to patch again");
                break;

            case PatchState.NotPatched:
                await InstallAsync(tldPath, log, ct);
                break;

            case PatchState.NeedsDllUpdate:
                // 照抄 PatchStarter 的 tldloaderUpdate 分支
                log?.Report("TLDLoader.dll update!");
                CopyReferences(tldPath, log);
                var modPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "TheLongDrive", "Mods");
                Directory.CreateDirectory(modPath);
                CopyCoreAssets(modPath, IsBetaVersion(tldPath), log);
                log?.Report("TLDLoader.dll update successful!");
                break;

            case PatchState.OldFilesFound:
                // 照抄 PatchStarter 的 oldFilesFound 分支
                log?.Report("Cleaning old files!");
                SafeRemove(legacyBackup, log);
                SafeRemove(Path.Combine(managedDir, "Mono.Cecil.dll"), log);
                SafeRemove(Path.Combine(managedDir, "Mono.Cecil.Rocks.dll"), log);
                SafeRemove(Path.Combine(managedDir, LoaderDllFileName), log);
                SafeRemove(Path.Combine(managedDir, "TLDPatcher.exe"), log);
                SafeRemove(Path.Combine(managedDir, "System.Xml.dll"), log);
                await InstallAsync(tldPath, log, ct);
                break;

            case PatchState.GameUpdated:
                // 照抄 PatchStarter 的 isgameUpdated 分支
                log?.Report("Removing old backup!");
                SafeRemove(backup, log);
                await InstallAsync(tldPath, log, ct);
                break;

            case PatchState.OldPatchFound:
                // 照抄 PatchStarter 的 oldPatchFound 分支
                log?.Report("Old patch found, ready to upgrade");
                if (File.Exists(legacyBackup))
                {
                    if (File.Exists(assembly))
                    {
                        log?.Report("Recovering backup file!");
                        SafeRemove(assembly, log);
                    }
                    File.Move(legacyBackup, assembly);
                    log?.Report("Recovering.....Assembly-CSharp.original.dll");

                    log?.Report("Cleaning old files!");
                    SafeRemove(Path.Combine(managedDir, "Mono.Cecil.dll"), log);
                    SafeRemove(Path.Combine(managedDir, "Mono.Cecil.Rocks.dll"), log);
                    SafeRemove(Path.Combine(managedDir, LoaderDllFileName), log);
                    SafeRemove(Path.Combine(managedDir, "TLDPatcher.exe"), log);
                    SafeRemove(Path.Combine(managedDir, "System.Xml.dll"), log);
                    await InstallAsync(tldPath, log, ct);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"0.1 backup file not found: {legacyBackup}\nCan't continue with upgrade\nPlease check integrity files in steam, to recover original file.");
                }
                break;

            default:
                throw new InvalidOperationException($"Cannot handle state: {state}");
        }
    }

    // ----- 内部方法 -----
    private enum InsertPosition { BeforeFirst, AfterSecondToLast }

    /// <summary>
    /// 用 Mono.Cecil 注入一个 call 指令。对应原 PatchThis / PatchThisLast。
    /// BeforeFirst: 在目标方法第一条指令前插入（用于 InitMainMenu）
    /// AfterSecondToLast: 在倒数第二条指令后插入（用于 dbInit）
    /// </summary>
    private static void PatchThis(string managedDir, string targetAssemblyName,
        string targetType, string targetMethod,
        string loaderAssemblyName, string loaderClass, string loaderMethod,
        InsertPosition insertPosition)
    {
        var targetAssemblyPath = Path.Combine(managedDir, targetAssemblyName);
        var loaderAssemblyPath = Path.Combine(managedDir, loaderAssemblyName);

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);

        var readerParams = new ReaderParameters
        {
            ReadWrite = true,
            AssemblyResolver = resolver,
        };

        using var targetModule = ModuleDefinition.ReadModule(targetAssemblyPath, readerParams);
        using var loaderModule = ModuleDefinition.ReadModule(loaderAssemblyPath);

        var loaderType = loaderModule.GetType(loaderClass);
        if (loaderType == null)
            throw new InvalidOperationException($"Loader type not found: {loaderClass}");

        // 找 loaderMethod（无参数静态方法）
        var loaderMethodRef = loaderType.Methods.FirstOrDefault(m =>
            m.Name == loaderMethod && m.Parameters.Count == 0);
        if (loaderMethodRef == null)
            throw new InvalidOperationException($"Loader method not found: {loaderClass}::{loaderMethod}");

        // 在 targetModule 中找 targetType.targetMethod
        var targetTypeDef = targetModule.GetType(targetType);
        if (targetTypeDef == null)
            throw new InvalidOperationException($"Target type not found: {targetType}");

        var targetMethodDef = targetTypeDef.Methods.FirstOrDefault(m => m.Name == targetMethod);
        if (targetMethodDef == null)
            throw new InvalidOperationException($"Target method not found: {targetType}::{targetMethod}");

        // Import loader method into target module
        var importedMethod = targetModule.ImportReference(loaderMethodRef);

        // 创建 call 指令
        var processor = targetMethodDef.Body.GetILProcessor();
        var callInstruction = processor.Create(OpCodes.Call, importedMethod);

        if (insertPosition == InsertPosition.BeforeFirst)
        {
            var firstInstruction = targetMethodDef.Body.Instructions[0];
            processor.InsertBefore(firstInstruction, callInstruction);
        }
        else // AfterSecondToLast
        {
            var instructions = targetMethodDef.Body.Instructions;
            if (instructions.Count < 2)
                throw new InvalidOperationException($"目标方法 {targetType}::{targetMethod} Too few instructions");
            var secondToLast = instructions[instructions.Count - 2];
            processor.InsertAfter(secondToLast, callInstruction);
        }

        targetModule.Write();
    }

    /// <summary>扫描 Assembly-CSharp.dll 是否含 InitMainMenu 调用。原 IsPatched 逻辑。</summary>
    private static bool ScanForPatch(string gameDllPath)
    {
        try
        {
            using var module = ModuleDefinition.ReadModule(gameDllPath);
            var expectedPrefix = $"System.Void {LoaderClassName}::{InitMethodName}()";

            foreach (var type in module.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (method?.Body == null) continue;
                    foreach (var instr in method.Body.Instructions)
                    {
                        if (instr.OpCode != OpCodes.Call) continue;
                        var operandStr = instr.Operand?.ToString();
                        if (operandStr == null) continue;
                        // operand 形如 "System.Void TLDLoader.ModLoader::InitMainMenu()"
                        if (operandStr.Contains(LoaderClassName) &&
                            operandStr.Contains(InitMethodName))
                            return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>复制 Loader DLL 到 Managed 目录。照抄 长途中文车间最新修复版 Patcher.CopyReferences。</summary>
    private void CopyReferences(string tldPath, IProgress<string>? log)
    {
        var managedDir = Path.Combine(tldPath, ManagedSubPath);
        var exeDir = AppContext.BaseDirectory;

        // 先删除旧的（照抄旧版：TLDLoader.dll, TLDLoader.dll.mdb, TLDLoader.pdb, uAudio.dll, Ionic.Zip.dll）
        foreach (var f in new[] { LoaderDllFileName, LoaderMdbFileName, LoaderPdbFileName, "uAudio.dll", IonicZipFileName })
        {
            SafeRemove(Path.Combine(managedDir, f), log);
        }

        // 再从 exe 同目录复制（照抄旧版：TLDLoader.dll, .mdb, .pdb, Ionic.Zip.dll）
        foreach (var f in new[] { LoaderDllFileName, LoaderMdbFileName, LoaderPdbFileName, IonicZipFileName })
        {
            var src = Path.Combine(exeDir, f);
            if (!File.Exists(src)) continue;
            File.Copy(src, Path.Combine(managedDir, f), overwrite: true);
            log?.Report($"  Copying new file.....{f}");
        }

        // 照抄旧版 DownloadFiles：0Harmony.dll 也复制到 Managed（游戏运行时需要）
        var harmonySrc = Path.Combine(exeDir, HarmonyDllFileName);
        if (File.Exists(harmonySrc))
        {
            var harmonyDst = Path.Combine(managedDir, HarmonyDllFileName);
            SafeRemove(harmonyDst, log);
            File.Copy(harmonySrc, harmonyDst, overwrite: true);
            log?.Report($"  Copying new file.....{HarmonyDllFileName}");
        }
    }

    /// <summary>复制核心资源到 Mods/Assets 目录。照抄 长途中文车间最新修复版 Patcher.CopyCoreAssets。</summary>
    private void CopyCoreAssets(string modPath, bool isBeta, IProgress<string>? log)
    {
        var exeDir = AppContext.BaseDirectory;

        // core.unity3d → Mods/Assets/TLDLoader_Core/
        log?.Report("  Copying Core Assets.....TLDLoader_Core");
        var coreDir = Path.Combine(modPath, "Assets", "TLDLoader_Core");
        if (!Directory.Exists(coreDir)) Directory.CreateDirectory(coreDir);
        else
        {
            var oldCore = Path.Combine(coreDir, CoreAssetFileName);
            if (File.Exists(oldCore)) File.Delete(oldCore);
        }
        var coreSrc = Path.Combine(exeDir, "Extract", "TLDLoader_Core", CoreAssetFileName);
        if (File.Exists(coreSrc))
        {
            File.Copy(coreSrc, Path.Combine(coreDir, CoreAssetFileName), overwrite: true);
        }

        // settingsui.unity3d → Mods/Assets/TLDLoader_Settings/（beta 跳过）
        if (!isBeta)
        {
            log?.Report("  Copying Core Assets.....TLDLoader_Settings");
            var settingsDir = Path.Combine(modPath, "Assets", "TLDLoader_Settings");
            if (!Directory.Exists(settingsDir)) Directory.CreateDirectory(settingsDir);
            else
            {
                var oldSettings = Path.Combine(settingsDir, SettingsAssetFileName);
                if (File.Exists(oldSettings)) File.Delete(oldSettings);
            }
            var settingsSrc = Path.Combine(exeDir, "Extract", "TLDLoader_Settings", SettingsAssetFileName);
            if (File.Exists(settingsSrc))
            {
                File.Copy(settingsSrc, Path.Combine(settingsDir, SettingsAssetFileName), overwrite: true);
            }
        }
        log?.Report("  Copying Core Assets Completed!");
    }

    /// <summary>安全删除文件。照抄 Python _safe_remove。</summary>
    private static void SafeRemove(string path, IProgress<string>? log)
    {
        if (!File.Exists(path)) return;
        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            log?.Report($"  - Deleted {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            log?.Report($"  ! Delete failed {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    /// <summary>删除 Managed 目录下所有 TLDLoader 相关文件。照抄 Python remove_loader_files。</summary>
    private void DeleteReferences(string tldPath, IProgress<string>? log)
    {
        var managedDir = Path.Combine(tldPath, ManagedSubPath);
        foreach (var f in new[] { LoaderDllFileName, LoaderMdbFileName, LoaderPdbFileName, "uAudio.dll", IonicZipFileName })
        {
            SafeRemove(Path.Combine(managedDir, f), log);
        }
    }

    /// <summary>清理 0.1 版残留文件。原 PatchStarter 中 oldFilesFound 分支。</summary>
    private void DeleteOldVersionFiles(string tldPath, IProgress<string>? log)
    {
        var managedDir = Path.Combine(tldPath, ManagedSubPath);
        var oldFiles = new[]
        {
            GameDllName.Replace(".dll", OldVersionBackupSuffix), // Assembly-CSharp.original.dll
            MonoCecilDllFileName,
            "Mono.Cecil.Rocks.dll",
            LoaderDllFileName,
            "TLDPatcher.exe",
            "System.Xml.dll",
        };
        foreach (var f in oldFiles)
        {
            var path = Path.Combine(managedDir, f);
            if (File.Exists(path))
            {
                File.Delete(path);
                log?.Report($"  Removing.....{f}");
            }
        }
    }

    private static string MD5HashFile(string path)
    {
        using var md5 = MD5.Create();
        var bytes = File.ReadAllBytes(path);
        var hash = md5.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
