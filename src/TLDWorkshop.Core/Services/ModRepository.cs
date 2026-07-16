using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using TLDWorkshop.Core.Contracts;
using TLDWorkshop.Core.Models;

namespace TLDWorkshop.Core.Services;

/// <summary>
/// mod 仓库默认实现。严格按 Python app.py 的多源架构实现。
/// 两个 JSON 文件都使用 "Mods" 作为顶层 key。
/// </summary>
public sealed class ModRepository : IModRepository
{
    public const string Branch = "WorkshopDatabase8.6";

    public static readonly IReadOnlyList<ModSource> DefaultModSources = new[]
    {
        new ModSource { Name = "Official source(English)", Kind = SourceKind.Online,
            Url = $"https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/{Branch}/modlist_3.json" },
        new ModSource { Name = "中文源(镜像)", Kind = SourceKind.Online,
            Url = $"https://jihulab.com/XLDev/workshop-tld-chinese/-/raw/{Branch}/modlist_3.json" },
    };

    public static readonly IReadOnlyList<ModSource> DefaultModpackSources = new[]
    {
        new ModSource { Name = "Official source(English)", Kind = SourceKind.Online,
            Url = $"https://gitlab.com/KolbenLP/WorkshopTLDMods/-/raw/{Branch}/Modpacks/modlist_3.json" },
        new ModSource { Name = "中文源(镜像)", Kind = SourceKind.Online,
            Url = $"https://jihulab.com/XLDev/workshop-tld-chinese/-/raw/{Branch}/Modpacks/modlist_3.json" },
    };

    public IReadOnlyList<ModSource> ModSources { get; }
    public IReadOnlyList<ModSource> ModpackSources { get; }

    private static readonly HttpClient DefaultClient = BuildClient();
    private readonly HttpClient _client;

    public ModRepository(HttpClient? client = null)
    {
        _client = client ?? DefaultClient;
        ModSources = DefaultModSources;
        ModpackSources = DefaultModpackSources;
    }

    private static HttpClient BuildClient()
    {
        // .NET 10: ServicePointManager 已过时，HttpClientHandler 直接配置 SSL
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    public async Task<List<Mod>> FetchModsAsync(int sourceIndex = 0, CancellationToken ct = default)
    {
        var text = await FetchTextAsync(ModSources, sourceIndex, ct);
        var data = JsonConvert.DeserializeObject<ModListJson>(text)
                   ?? throw new InvalidOperationException("Mod JSON 解析为 null");
        return data.Mods ?? new List<Mod>();
    }

    /// <summary>
    /// 同时从所有源拉取 mod 列表并按 FileName 合并。
    /// 修复 Bug 3：FileName 在两源可能不一致（如官方 M_WorldTweaker.dll vs 极狐 WorldTweaker.dll），
    /// 此时按 FileName 合并会丢失中文版本。改为多级匹配：
    ///   1. 先按 FileName 精确匹配（覆盖 95% 情况）
    ///   2. 剩余未匹配的，按「归一化 FileName」（去掉 M_ / RUNDEN_ 等前缀，忽略大小写）匹配
    ///   3. 仍未匹配的，作为独立记录添加（用户能在列表里看到中文版本）
    /// </summary>
    public async Task<List<MergedMod>> FetchMergedModsAsync(CancellationToken ct = default)
    {
        var officialMods = new List<Mod>();
        var jihuMods = new List<Mod>();

        // 并发拉取所有源
        var tasks = ModSources.Select(async (source, idx) =>
        {
            try
            {
                var text = await FetchTextAsync(ModSources, idx, ct);
                var data = JsonConvert.DeserializeObject<ModListJson>(text);
                return (idx, data?.Mods ?? new List<Mod>());
            }
            catch { return (idx, new List<Mod>()); }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        foreach (var (idx, mods) in results)
        {
            if (idx == 0) officialMods = mods;
            else if (idx == 1) jihuMods = mods;
        }

        // 第 1 级：按 FileName 精确匹配
        var merged = new Dictionary<string, MergedMod>(StringComparer.OrdinalIgnoreCase);
        var unmatchedJihu = new List<Mod>();

        foreach (var m in officialMods)
        {
            if (string.IsNullOrEmpty(m.FileName)) continue;
            if (!merged.ContainsKey(m.FileName))
                merged[m.FileName] = new MergedMod();
            merged[m.FileName].Official = m;
        }
        foreach (var m in jihuMods)
        {
            if (string.IsNullOrEmpty(m.FileName)) continue;
            if (merged.TryGetValue(m.FileName, out var existing) && existing.Official != null)
            {
                existing.Jihu = m;
            }
            else
            {
                unmatchedJihu.Add(m);
            }
        }

        // 第 2 级：未匹配的极狐源 mod，按「归一化 FileName」匹配官方源
        // 归一化：去 M_ / RUNDEN_ 前缀，去 .dll 后缀，忽略大小写
        static string NormalizeFileName(string fn)
        {
            if (string.IsNullOrEmpty(fn)) return "";
            var n = fn.Trim().ToLowerInvariant();
            n = System.Text.RegularExpressions.Regex.Replace(n, @"\.dll$", "");
            n = System.Text.RegularExpressions.Regex.Replace(n, @"^(m_|runden_|runden-m_)", "");
            return n;
        }

        var officialByNorm = new Dictionary<string, Mod>(StringComparer.Ordinal);
        foreach (var m in officialMods)
        {
            var n = NormalizeFileName(m.FileName);
            if (!string.IsNullOrEmpty(n) && !officialByNorm.ContainsKey(n))
                officialByNorm[n] = m;
        }

        var stillUnmatchedJihu = new List<Mod>();
        foreach (var jihuMod in unmatchedJihu)
        {
            var norm = NormalizeFileName(jihuMod.FileName);
            if (!string.IsNullOrEmpty(norm) && officialByNorm.TryGetValue(norm, out var offMod))
            {
                // 找到对应的官方 mod，合并到 merged[官方 FileName]
                if (!merged.ContainsKey(offMod.FileName))
                    merged[offMod.FileName] = new MergedMod();
                merged[offMod.FileName].Official = offMod;
                merged[offMod.FileName].Jihu = jihuMod;
            }
            else
            {
                stillUnmatchedJihu.Add(jihuMod);
            }
        }

        // 第 3 级：剩余极狐源 mod 作为独立记录添加
        foreach (var jihuMod in stillUnmatchedJihu)
        {
            var key = "jihu_" + jihuMod.FileName;
            if (!merged.ContainsKey(key))
                merged[key] = new MergedMod();
            merged[key].Jihu = jihuMod;
        }

        return merged.Values.ToList();
    }

    public async Task<List<Modpack>> FetchModpacksAsync(int sourceIndex = 0, CancellationToken ct = default)
    {
        var text = await FetchTextAsync(ModpackSources, sourceIndex, ct);
        var data = JsonConvert.DeserializeObject<ModpackListJson>(text)
                   ?? throw new InvalidOperationException("Modpack JSON 解析为 null");
        return data.Modpacks ?? new List<Modpack>();
    }

    /// <summary>
    /// 同时从所有源拉取模组包列表并按 Link 合并。
    /// 修复 Bug B：原 ModpackViewModel.LoadAsync 只显示极狐源 + 按 FileName 去重（会丢极狐源内部同名 modpack）。
    /// 现在改为：用 Link 作为唯一合并键，同时持有两个源的 Modpack 数据，与 BrowseViewModel 的 MergedMod 一致。
    /// </summary>
    public async Task<List<MergedModpack>> FetchMergedModpacksAsync(CancellationToken ct = default)
    {
        var officialPacks = new List<Modpack>();
        var jihuPacks = new List<Modpack>();

        // 并发拉取两个源
        var tasks = ModpackSources.Select(async (source, idx) =>
        {
            try
            {
                var text = await FetchTextAsync(ModpackSources, idx, ct);
                var data = JsonConvert.DeserializeObject<ModpackListJson>(text);
                return (idx, data?.Modpacks ?? new List<Modpack>());
            }
            catch { return (idx, new List<Modpack>()); }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        foreach (var (idx, packs) in results)
        {
            if (idx == 0) officialPacks = packs;
            else if (idx == 1) jihuPacks = packs;
        }

        // 按 Link 合并（Link = .txt 清单 URL，每个 modpack 唯一）
        var merged = new Dictionary<string, MergedModpack>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in officialPacks)
        {
            if (string.IsNullOrEmpty(p.Link)) continue;
            if (!merged.ContainsKey(p.Link))
                merged[p.Link] = new MergedModpack();
            merged[p.Link].Official = p;
        }
        foreach (var p in jihuPacks)
        {
            if (string.IsNullOrEmpty(p.Link)) continue;
            if (!merged.ContainsKey(p.Link))
                merged[p.Link] = new MergedModpack();
            merged[p.Link].Jihu = p;
        }

        // 排序：有中文名的优先（按 DisplayName 排序）
        return merged.Values
            .OrderBy(m => m.HasBothSources ? 0 : 1)  // 双源的排前面
            .ThenBy(m => m.DisplayName)
            .ToList();
    }

    private async Task<string> FetchTextAsync(IReadOnlyList<ModSource> sources, int sourceIndex, CancellationToken ct)
    {
        if (sourceIndex < 0 || sourceIndex >= sources.Count)
            sourceIndex = 0;

        Exception? lastError = null;

        // 尝试从指定源开始，失败则依次尝试后续源
        for (int i = 0; i < sources.Count; i++)
        {
            var idx = (sourceIndex + i) % sources.Count;
            var source = sources[idx];
            try
            {
                if (source.Kind == SourceKind.Local && !string.IsNullOrEmpty(source.LocalPath))
                {
                    if (File.Exists(source.LocalPath))
                        return await File.ReadAllTextAsync(source.LocalPath, ct);
                }
                else if (!string.IsNullOrEmpty(source.Url))
                {
                    var bytes = await _client.GetByteArrayAsync(source.Url, ct).ConfigureAwait(false);
                    var text = System.Text.Encoding.UTF8.GetString(bytes).TrimStart();
                    if (text.StartsWith("{"))
                        return text;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
                System.Diagnostics.Debug.WriteLine($"[ModRepository] 源 {source.Name} 失败：{ex.Message}");
            }
        }

        throw new InvalidOperationException(
            $"所有数据源均不可用。{(lastError?.Message ?? "未知错误")}", lastError);
    }

    public async Task DownloadModAsync(Mod mod, string targetPath,
        IProgress<(long received, long total)>? progress, CancellationToken ct = default)
    {
        using var resp = await _client.GetAsync(mod.Link, HttpCompletionOption.ResponseHeadersRead, ct)
                                 .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(targetPath);
        var buf = new byte[81920];
        long received = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
            received += n;
            progress?.Report((received, total));
        }
    }

    public async Task<byte[]> DownloadImageAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return Array.Empty<byte>();
        return await _client.GetByteArrayAsync(url, ct).ConfigureAwait(false);
    }

    public async Task<List<string>> DownloadModpackTxtAsync(string txtUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(txtUrl))
            throw new ArgumentException("txt URL 为空", nameof(txtUrl));
        var bytes = await _client.GetByteArrayAsync(txtUrl, ct).ConfigureAwait(false);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        return ParseTxtFileNames(text);
    }

    public static List<string> ParseTxtFileNames(string content)
    {
        var result = new List<string>();
        foreach (var line in content.Split('\n', '\r'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed.StartsWith("#")) continue;
            result.Add(trimmed);
        }
        return result;
    }
}
