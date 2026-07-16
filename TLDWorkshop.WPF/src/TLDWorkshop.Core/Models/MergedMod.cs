using Newtonsoft.Json;

namespace TLDWorkshop.Core.Models;

/// <summary>
/// 合并后的 mod——同时持有官方源和极狐源的数据。
/// 列表展示用 DisplaySource 指定的源；详情页展示两个源。
/// </summary>
public sealed class MergedMod
{
    /// <summary>官方源（GitLab 英文）数据，可能为 null。</summary>
    public Mod? Official { get; set; }

    /// <summary>极狐源（中文镜像）数据，可能为 null。</summary>
    public Mod? Jihu { get; set; }

    /// <summary>用于合并的唯一键（FileName）。</summary>
    public string FileName => Official?.FileName ?? Jihu?.FileName ?? "";

    /// <summary>是否已安装（由 UI 层设置）。</summary>
    public bool IsInstalled { get; set; }

    /// <summary>是否有更新（由 UI 层设置）。</summary>
    public bool HasUpdate { get; set; }

    /// <summary>按显示源索引获取 Mod。</summary>
    public Mod? GetDisplay(int displaySourceIdx)
    {
        // displaySourceIdx 0 = 官方, 1 = 极狐
        if (displaySourceIdx == 0) return Official ?? Jihu;
        return Jihu ?? Official;
    }

    /// <summary>显示名（用显示源）。</summary>
    public string GetDisplayName(int displaySourceIdx) => GetDisplay(displaySourceIdx)?.Name ?? FileName;

    /// <summary>显示描述。</summary>
    public string GetDisplayDescription(int displaySourceIdx) =>
        GetDisplay(displaySourceIdx)?.Description ?? "";

    /// <summary>显示图片 URL。</summary>
    public string GetDisplayPicture(int displaySourceIdx) =>
        GetDisplay(displaySourceIdx)?.PictureLink ?? "";

    /// <summary>显示作者。</summary>
    public string GetDisplayAuthor(int displaySourceIdx) =>
        GetDisplay(displaySourceIdx)?.Author ?? "";

    /// <summary>显示版本。</summary>
    public string GetDisplayVersion(int displaySourceIdx) =>
        GetDisplay(displaySourceIdx)?.Version ?? "";

    /// <summary>显示分类。</summary>
    public string GetDisplayCategory(int displaySourceIdx) =>
        GetDisplay(displaySourceIdx)?.Category ?? "";

    /// <summary>显示日期。</summary>
    public string GetDisplayDate(int displaySourceIdx) =>
        GetDisplay(displaySourceIdx)?.Date ?? "";

    /// <summary>显示依赖。</summary>
    public string GetDisplayDependency(int displaySourceIdx) =>
        GetDisplay(displaySourceIdx)?.Dependency ?? "";

    /// <summary>是否为 LEGACY。</summary>
    public bool IsLegacy =>
        (Official?.IsLegacy ?? false) && (Jihu?.IsLegacy ?? false) ||
        (Official == null && Jihu?.IsLegacy == true) ||
        (Jihu == null && Official?.IsLegacy == true);

    // ----- UI 绑定用的显示属性（由 VM 在加载后设置）-----
    public string DisplayName { get; set; } = "";
    public string DisplayDescription { get; set; } = "";
    public string DisplayPicture { get; set; } = "";
    public string DisplayAuthor { get; set; } = "";
    public string DisplayVersion { get; set; } = "";
    public string DisplayCategory { get; set; } = "";
    public string DisplayDate { get; set; } = "";

    /// <summary>设置显示属性（用指定源的数据填充 UI 绑定属性）。</summary>
    public void SetDisplayProperties(int displaySourceIdx)
    {
        var m = GetDisplay(displaySourceIdx);
        if (m == null) return;
        DisplayName = m.Name;
        DisplayDescription = m.Description;
        DisplayPicture = m.PictureLink;
        DisplayAuthor = m.Author;
        DisplayVersion = m.Version;
        DisplayCategory = m.Category;
        DisplayDate = m.Date;
    }
}
