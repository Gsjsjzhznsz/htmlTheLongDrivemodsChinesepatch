namespace TLDWorkshop.Core.Models;

/// <summary>
/// 版本检查响应。对应原项目 <c>CheckVer</c> 类。
/// </summary>
public sealed class VersionInfo
{
    public string CurrentVersion { get; set; } = "9.0.0";
    public string? LatestVersion { get; set; }
    public string? UpdaterUrl    { get; set; }
    public string? ChangelogUrl  { get; set; }

    public bool HasUpdate => !string.IsNullOrEmpty(LatestVersion)
                             && !string.Equals(CurrentVersion, LatestVersion, StringComparison.Ordinal);
}
