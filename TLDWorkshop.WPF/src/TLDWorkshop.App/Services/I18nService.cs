using System.Windows;

namespace TLDWorkshop.App.Services;

/// <summary>
/// 国际化服务。用 ResourceDictionary 切换中英文，所有 XAML 用 DynamicResource 绑定，
/// 语言切换时自动刷新所有 UI。
/// ViewModel 里的字符串用 I18nService.Instance["Key"] 查找。
/// </summary>
public sealed class I18nService
{
    public static I18nService Instance { get; } = new();

    public event Action? LanguageChanged;

    private string _currentLang = "zh";
    public string CurrentLang
    {
        get => _currentLang;
        set
        {
            if (_currentLang != value)
            {
                _currentLang = value;
                SwitchResourceDictionary(value);
                LanguageChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// 从 Application.Resources 查找字符串（供 ViewModel 用）。
    /// </summary>
    public string this[string key]
    {
        get
        {
            var val = Application.Current?.TryFindResource(key);
            return val as string ?? key;
        }
    }

    /// <summary>快捷方法。</summary>
    public string T(string key) => this[key];

    /// <summary>带格式化的快捷方法（支持 {0}, {1} 等）。</summary>
    public string T(string key, params object[] args)
    {
        var template = this[key];
        try { return string.Format(template, args); }
        catch { return template; }
    }

    /// <summary>
    /// 切换 Application.Resources 里的语言 ResourceDictionary。
    /// </summary>
    private void SwitchResourceDictionary(string lang)
    {
        try
        {
            var app = Application.Current;
            if (app == null) return;

            var dictionaries = app.Resources.MergedDictionaries;
            var newSource = new Uri($"pack://application:,,,/Resources/Strings.{lang}.xaml");

            for (int i = 0; i < dictionaries.Count; i++)
            {
                var dict = dictionaries[i];
                if (dict.Source != null &&
                    (dict.Source.OriginalString.Contains("Strings.zh") ||
                     dict.Source.OriginalString.Contains("Strings.en")))
                {
                    dictionaries[i] = new ResourceDictionary { Source = newSource };
                    return;
                }
            }

            dictionaries.Add(new ResourceDictionary { Source = newSource });
        }
        catch
        {
            /* 切换失败不致命 */
        }
    }
}
