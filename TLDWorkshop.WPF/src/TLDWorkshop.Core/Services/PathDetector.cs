using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using TLDWorkshop.Core.Contracts;

namespace TLDWorkshop.Core.Services;

/// <summary>
/// 检测 TheLongDrive 安装路径。对应原项目 FindTldPath / RunPathDetectionFlow / LoadSavedTldPath。
///
/// 严格按原始 exe 行为实现：
/// - 持久化文件 TLDFolder.txt 放在 exe 同目录（不是 LocalApplicationData）
/// - Steam App ID = 1017180，游戏目录名 "The Long Drive"（带空格）
/// - 解析 libraryfolders.vdf 找所有 Steam 库
/// - 找不到才需要让用户手动选；用户取消只警告不退出
/// </summary>
public sealed class PathDetector : IPathDetector
{
    public const string TldSteamAppId = "1017180";
    public const string TldGameFolderName = "The Long Drive";
    public const string TldExeName = "TheLongDrive.exe";

    /// <summary>路径持久化文件名（与原 exe 完全一致，放在 exe 同目录）。</summary>
    public static readonly string TldFolderFilePath = Path.Combine(
        AppContext.BaseDirectory, "TLDFolder.txt");

    [SupportedOSPlatform("windows")]
    public string? TryDetect()
    {
        // 1) 优先使用持久化保存的路径
        var saved = LoadSaved();
        if (!string.IsNullOrEmpty(saved) && Directory.Exists(saved)) return saved;

        // 2) 注册表找 Steam 安装目录
        var steamPath = TryGetSteamInstallPath();
        if (!string.IsNullOrEmpty(steamPath) && Directory.Exists(steamPath))
        {
            var libFolders = FindSteamLibraryFolders(steamPath);
            foreach (var lib in libFolders)
            {
                var candidate = Path.Combine(lib, "steamapps", "common", TldGameFolderName);
                if (Directory.Exists(candidate) &&
                    File.Exists(Path.Combine(candidate, TldExeName)))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    public async Task<string?> LetUserSelectAsync() => await Task.FromResult<string?>(null);

    /// <summary>读取 TLDFolder.txt 中保存的路径。原 LoadSavedTldPath 逻辑。</summary>
    public static string? LoadSaved()
    {
        try
        {
            if (!File.Exists(TldFolderFilePath)) return null;
            var text = File.ReadAllText(TldFolderFilePath).Trim();
            if (string.IsNullOrEmpty(text)) return null;
            if (!Directory.Exists(text)) return null;
            if (!File.Exists(Path.Combine(text, TldExeName))) return null;
            return text;
        }
        catch
        {
            // 保存的路径已失效，删除配置文件
            try { if (File.Exists(TldFolderFilePath)) File.Delete(TldFolderFilePath); } catch { }
            return null;
        }
    }

    /// <summary>保存路径到 TLDFolder.txt。原 SaveTldPath 逻辑。</summary>
    public static void Save(string path)
    {
        try
        {
            File.WriteAllText(TldFolderFilePath, path?.Trim() ?? "");
        }
        catch
        {
            // 持久化失败不应影响主流程
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetSteamInstallPath()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        if (key?.GetValue("InstallPath") is string p && Directory.Exists(p)) return p;
        using var key2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
        if (key2?.GetValue("InstallPath") is string p2 && Directory.Exists(p2)) return p2;
        return null;
    }

    /// <summary>解析 Steam libraryfolders.vdf 找出所有 Steam 库目录。原 FindTldPath 内联逻辑。</summary>
    private static List<string> FindSteamLibraryFolders(string steamPath)
    {
        var result = new List<string> { steamPath };
        try
        {
            var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) return result;
            // 原实现按双引号 split，这里复刻相同逻辑
            var text = File.ReadAllText(vdf);
            var parts = text.Split('"');
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                if (!Directory.Exists(part)) continue;
                // 跳过明显不是路径的片段（vdf 里 "path" 后才是路径）
                if (part.Contains(':') && part.Contains('\\'))
                {
                    if (!result.Contains(part)) result.Add(part);
                }
            }
        }
        catch { /* 忽略解析失败 */ }
        return result;
    }
}
