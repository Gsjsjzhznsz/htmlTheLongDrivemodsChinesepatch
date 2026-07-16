using Newtonsoft.Json;

namespace TLDWorkshop.Core.Models;

/// <summary>
/// 合并后的模组包——同时持有官方源和中文源的数据。
/// 与 MergedMod 类似，但合并键是 Link（.txt 文件下载 URL）而不是 FileName，
/// 因为中文源内部存在 FileName 冲突。
///
/// Display* 属性默认用中文源（Jihu），但调用 SetDisplayProperties(sourceIdx) 后
/// 会根据设置页的 DisplaySourceIndex 切换显示源。
/// </summary>
public sealed class MergedModpack
{
    /// <summary>官方源（GitLab 英文）数据，可能为 null。</summary>
    public Modpack? Official { get; set; }

    /// <summary>中文源（中文镜像）数据，可能为 null。</summary>
    public Modpack? Jihu { get; set; }

    /// <summary>用于合并的唯一键（Link = .txt 清单下载 URL）。</summary>
    public string Link => Official?.Link ?? Jihu?.Link ?? "";

    /// <summary>显示源索引（0=官方, 1=中文源），由 VM 设置。</summary>
    public int DisplaySourceIndex { get; set; } = 1;

    /// <summary>显示名（用 DisplaySourceIndex 指定的源）。</summary>
    public string DisplayName
    {
        get
        {
            var m = GetDisplay(DisplaySourceIndex);
            return m?.Name ?? "";
        }
    }

    public string DisplayDescription
    {
        get
        {
            var m = GetDisplay(DisplaySourceIndex);
            return m?.Description ?? "";
        }
    }

    public string DisplayAuthor
    {
        get
        {
            var m = GetDisplay(DisplaySourceIndex);
            return m?.Author ?? "";
        }
    }

    public string DisplayVersion
    {
        get
        {
            var m = GetDisplay(DisplaySourceIndex);
            return m?.Version ?? "";
        }
    }

    public string DisplayDate
    {
        get
        {
            var m = GetDisplay(DisplaySourceIndex);
            return m?.Date ?? "";
        }
    }

    public string DisplayPicture
    {
        get
        {
            var m = GetDisplay(DisplaySourceIndex);
            return m?.PictureLink ?? "";
        }
    }

    public string DisplayFileName
    {
        get
        {
            var m = GetDisplay(DisplaySourceIndex);
            return m?.FileName ?? "";
        }
    }

    /// <summary>按显示源索引获取 Modpack。</summary>
    public Modpack? GetDisplay(int displaySourceIdx)
    {
        if (displaySourceIdx == 0) return Official ?? Jihu;
        return Jihu ?? Official;
    }

    /// <summary>按下载源索引获取 Modpack。</summary>
    public Modpack? GetBySource(int sourceIdx)
    {
        if (sourceIdx == 0) return Official ?? Jihu;
        return Jihu ?? Official;
    }

    /// <summary>是否两个源都有数据。</summary>
    public bool HasBothSources => Official != null && Jihu != null;

    /// <summary>设置显示源索引（由 VM 在加载后调用）。</summary>
    public void SetDisplaySource(int sourceIdx)
    {
        DisplaySourceIndex = sourceIdx;
    }
}
