using System.Collections.ObjectModel;

namespace TLDWorkshop.App.Services;

/// <summary>
/// 全局应用日志。所有页面的操作日志都收集到这里，在设置页底部显示。
/// </summary>
public static class AppLog
{
    public static ObservableCollection<string> Entries { get; } = new();

    private static readonly object _lock = new();
    private const int MaxEntries = 500;

    public static void Add(string message)
    {
        lock (_lock)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Entries.Add(entry);
            // 超出上限删最旧的
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        }
    }

    public static void Clear()
    {
        lock (_lock) { Entries.Clear(); }
    }
}
