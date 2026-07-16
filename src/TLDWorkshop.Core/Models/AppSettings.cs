using Newtonsoft.Json;

namespace TLDWorkshop.Core.Models;

/// <summary>
/// 用户配置。原项目通过 WinForms 控件直接读写，此处集中持久化。
/// </summary>
public sealed class AppSettings
{
    public string? TldPath { get; set; }
    public bool CheckUpdatesOnStart { get; set; } = true;
    public bool UseExperimentalBranch { get; set; }
    public int  ItemsPerPage { get; set; } = 30;
    /// <summary>"Dark" 或 "Light"。新 exe 也有"暗色主题"选项。</summary>
    public string Theme { get; set; } = "Dark";
    /// <summary>列表显示用哪个源：0=官方, 1=极狐（默认）。</summary>
    public int DisplaySourceIndex { get; set; } = 1;
    /// <summary>下载用哪个源：null=每次询问, 0=官方, 1=极狐。</summary>
    public int? DownloadSourceIndex { get; set; } = null;
    /// <summary>"zh" 或 "en"。</summary>
    public string Language { get; set; } = "zh";
    /// <summary>是否使用中文模组加载器 DLL。</summary>
    public bool UseChineseLoader { get; set; } = false;
    public string? CustomFtpUsername { get; set; } = "kolben1000";
    public string? CustomFtpPassword { get; set; } = "Kolben1000";

    /// <summary>
    /// 默认 FTP 提交端点。从原 exe 反编译提取：
    /// ftp://files.000webhost.com/htdocs/Submissions/Submissions.zip
    /// 用户名 kolben1000 / 密码 Kolben1000
    /// </summary>
    public string ModSubmissionEndpoint { get; set; } = "ftp://files.000webhost.com/htdocs/Submissions/Submissions.zip";

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            if (File.Exists(SettingsFilePath))
                settings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsFilePath))
                       ?? new AppSettings();
            else
                settings = new AppSettings();
        }
        catch { settings = new AppSettings(); }

        // 如果旧 settings.json 里 FTP 字段是 null（之前版本保存的空值），
        // 用默认值填充，这样设置页能显示填好的值
        if (string.IsNullOrEmpty(settings.CustomFtpUsername))
            settings.CustomFtpUsername = "kolben1000";
        if (string.IsNullOrEmpty(settings.CustomFtpPassword))
            settings.CustomFtpPassword = "Kolben1000";
        if (string.IsNullOrEmpty(settings.ModSubmissionEndpoint) ||
            settings.ModSubmissionEndpoint.Contains("example.com"))
            settings.ModSubmissionEndpoint = "ftp://files.000webhost.com/htdocs/Submissions/Submissions.zip";

        return settings;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsFilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
        catch { /* 持久化失败不致命 */ }
    }

    public static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TLDWorkshop", "settings.json");
}
