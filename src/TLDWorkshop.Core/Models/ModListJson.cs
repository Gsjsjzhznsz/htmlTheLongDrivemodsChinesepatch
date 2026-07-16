using Newtonsoft.Json;

namespace TLDWorkshop.Core.Models;

/// <summary>
/// JSON 根对象。Python app.py 确认：modlist_3.json 和 Modpacks/modlist_3.json
/// 都用 "Mods" 作为顶层 key，包含一个数组。
/// </summary>
public sealed class ModListJson
{
    [JsonProperty("Mods")]
    public List<Mod> Mods { get; set; } = new();
}

/// <summary>模组包列表 JSON 根对象。同样用 "Mods" 作为顶层 key。</summary>
public sealed class ModpackListJson
{
    [JsonProperty("Mods")]
    public List<Modpack> Modpacks { get; set; } = new();
}
