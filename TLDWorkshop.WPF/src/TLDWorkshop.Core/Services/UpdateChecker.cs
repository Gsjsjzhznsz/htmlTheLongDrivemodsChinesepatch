using System.Net;
using TLDWorkshop.Core.Contracts;
using TLDWorkshop.Core.Models;

namespace TLDWorkshop.Core.Services;

/// <summary>
/// 应用自身更新检查。对应原项目 <c>vcontrol</c> 方法。
/// URL 常量从 TldPatcher 引用（原 exe 这些 URL 定义在 InitBaseParams / vcontrol 中）。
/// </summary>
public sealed class UpdateChecker : IUpdateChecker
{
    private readonly HttpClient _client;
    public UpdateChecker(HttpClient? client = null)
    {
        _client = client ?? new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        _client.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<VersionInfo> CheckAsync(CancellationToken ct = default)
    {
        var info = new VersionInfo { CurrentVersion = "9.0.0" };
        try
        {
            var text = await _client.GetStringAsync(TldPatcher.VersionCheckUrl, ct).ConfigureAwait(false);
            info.LatestVersion = text.Trim();
            info.UpdaterUrl    = TldPatcher.UpdaterExeUrl;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            info.LatestVersion = null;
        }
        return info;
    }
}
