using Newtonsoft.Json;

namespace TLDWorkshop.Core.Models;

/// <summary>
/// 整合包定义。对应 Modpacks/modlist_3.json 中 Mods 数组项。
/// 实际 JSON 结构（从 GitLab 下载验证）：
///   "Name", "Version", "Author", "Description", "Date",
///   "Link", "PictureLink", "FileName"
/// </summary>
public sealed class Modpack
{
    [JsonProperty("Name")]        public string Name        { get; set; } = string.Empty;
    [JsonProperty("Version")]     public string Version     { get; set; } = string.Empty;
    [JsonProperty("Author")]      public string Author      { get; set; } = string.Empty;
    [JsonProperty("Description")] public string Description { get; set; } = string.Empty;
    [JsonProperty("Date")]        public string Date        { get; set; } = string.Empty;
    /// <summary>.txt 清单文件下载 URL。</summary>
    [JsonProperty("Link")]        public string Link        { get; set; } = string.Empty;
    [JsonProperty("PictureLink")] public string PictureLink { get; set; } = string.Empty;
    [JsonProperty("FileName")]    public string FileName    { get; set; } = string.Empty;
}
