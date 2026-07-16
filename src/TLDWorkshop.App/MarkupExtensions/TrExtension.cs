using System.Windows;
using System.Windows.Markup;
using TLDWorkshop.App.Services;

namespace TLDWorkshop.App.MarkupExtensions;

/// <summary>
/// XAML 标记扩展：{i18n:Tr Nav.Home}
/// 现在用 ResourceDictionary + DynamicResource 实现切换，这个 TrExtension 已弃用。
/// 保留只是为了向后兼容，新代码应直接用 DynamicResource。
/// </summary>
public class TrExtension : MarkupExtension
{
    public string Key { get; set; }

    public TrExtension() { Key = ""; }
    public TrExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // 从 Application.Resources 查找
        return Application.Current?.TryFindResource(Key) ?? Key;
    }
}
