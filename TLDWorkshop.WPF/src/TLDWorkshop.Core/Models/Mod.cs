using Newtonsoft.Json;

namespace TLDWorkshop.Core.Models;

/// <summary>
/// 单个模组的元数据。字段名直接与 GitLab 上 modlist_3.json 的 JSON key 一致（PascalCase）。
/// 实际 JSON 结构（从 GitLab 下载验证）：
///   "Name", "Version", "Author", "Description", "Date",
///   "Link", "PictureLink", "FileName", "Category", "Changelog", "Dependency"
/// </summary>
public sealed class Mod
{
    [JsonProperty("Name")]        public string Name        { get; set; } = string.Empty;
    [JsonProperty("Version")]     public string Version     { get; set; } = string.Empty;
    [JsonProperty("Author")]      public string Author      { get; set; } = string.Empty;
    [JsonProperty("Description")] public string Description { get; set; } = string.Empty;
    [JsonProperty("Date")]        public string Date        { get; set; } = string.Empty;
    [JsonProperty("Link")]        public string Link        { get; set; } = string.Empty;
    [JsonProperty("PictureLink")] public string PictureLink { get; set; } = string.Empty;
    [JsonProperty("FileName")]    public string FileName    { get; set; } = string.Empty;
    [JsonProperty("Category")]    public string Category    { get; set; } = string.Empty;
    [JsonProperty("Changelog")]   public string Changelog   { get; set; } = string.Empty;
    [JsonProperty("Dependency")]  public string Dependency  { get; set; } = string.Empty;

    /// <summary>LEGACY 类别的模组默认不展示。</summary>
    public bool IsLegacy => string.Equals(Category, "LEGACY", StringComparison.OrdinalIgnoreCase);
}
