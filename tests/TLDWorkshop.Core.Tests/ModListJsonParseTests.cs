using System.Text;
using TLDWorkshop.Core.Models;
using TLDWorkshop.Core.Services;
using Xunit;

namespace TLDWorkshop.Core.Tests;

public class ModListJsonParseTests
{
    [Fact]
    public void ParsesStandardJsonWithoutCrash()
    {
        var mod = new Mod { Name = "Test Mod", Category = "Vehicle" };
        Assert.False(mod.IsLegacy);

        var legacy = new Mod { Name = "Old", Category = "LEGACY" };
        Assert.True(legacy.IsLegacy);
    }

    [Fact]
    public void BrowseMode_Enum_HasExpectedValues()
    {
        Assert.Equal(BrowseMode.Online,  Enum.Parse<BrowseMode>("Online"));
        Assert.Equal(BrowseMode.MyMods,  Enum.Parse<BrowseMode>("MyMods"));
        Assert.Equal(BrowseMode.Modpack, Enum.Parse<BrowseMode>("Modpack"));
        Assert.Equal(BrowseMode.Detail,  Enum.Parse<BrowseMode>("Detail"));
    }

    [Fact]
    public void ModSources_HaveSeparateUrlsForModsAndModpacks()
    {
        // 原 refreshList 拉两个独立 JSON：modlist_3.json 和 Modpacks/modlist_3.json
        // Python app.py 确认：MODLIST_SOURCES 和 MODPACK_SOURCES 是不同的 URL 列表
        Assert.NotEmpty(ModRepository.DefaultModSources);
        Assert.NotEmpty(ModRepository.DefaultModpackSources);

        // mod 源 URL 不含 /Modpacks/
        foreach (var s in ModRepository.DefaultModSources)
        {
            Assert.DoesNotContain("/Modpacks/", s.Url ?? "");
        }

        // modpack 源 URL 含 /Modpacks/
        foreach (var s in ModRepository.DefaultModpackSources)
        {
            Assert.Contains("/Modpacks/", s.Url ?? "");
        }
    }

    [Fact]
    public void ParseTxtFileNames_SkipsBlankAndComments()
    {
        var txt = "mod1.dll\n\n# comment\nmod2.dll\n  mod3.dll  \n";
        var result = ModRepository.ParseTxtFileNames(txt);
        Assert.Equal(3, result.Count);
        Assert.Equal("mod1.dll", result[0]);
        Assert.Equal("mod2.dll", result[1]);
        Assert.Equal("mod3.dll", result[2]);
    }
}
