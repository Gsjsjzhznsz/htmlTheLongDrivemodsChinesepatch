namespace TLDWorkshop.Core.Models;

/// <summary>数据源类型。</summary>
public enum SourceKind { Online, Local }

/// <summary>
/// mod 列表数据源。对应 Python 项目的 MODLIST_SOURCES / MODPACK_SOURCES。
/// 支持官方源、极狐镜像、本地文件三种。
/// </summary>
public sealed class ModSource
{
    public string Name { get; init; } = string.Empty;
    public SourceKind Kind { get; init; }
    /// <summary>在线 URL（Kind=Online 时有效）。</summary>
    public string? Url { get; init; }
    /// <summary>本地文件路径（Kind=Local 时有效）。</summary>
    public string? LocalPath { get; init; }

    public override string ToString() => Name;
}
