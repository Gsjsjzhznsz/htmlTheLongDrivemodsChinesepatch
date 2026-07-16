using System.Collections.Concurrent;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace TLDWorkshop.App.Controls;

/// <summary>
/// 异步图片加载控件 + 全局缓存。
/// - 不阻塞 UI 线程，多张图片并发加载
/// - 全局缓存：同一 URL 只下载一次，翻页/返回不重新下载
/// </summary>
public sealed class AsyncImage : Image
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    });
    static AsyncImage()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        Client.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>全局图片缓存：URL → BitmapImage。返回详情页不重新下载。</summary>
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();

    /// <summary>缓存上限，超过时清理最早的一半，防止无界增长导致内存泄漏。</summary>
    private const int MaxCacheSize = 200;

    public static readonly DependencyProperty UrlProperty =
        DependencyProperty.Register(nameof(Url), typeof(string), typeof(AsyncImage),
            new PropertyMetadata(null, OnUrlChanged));

    public string? Url
    {
        get => (string?)GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    public static readonly DependencyProperty DecodePixelWidthProperty =
        DependencyProperty.Register(nameof(DecodePixelWidth), typeof(int), typeof(AsyncImage),
            new PropertyMetadata(200));

    public int DecodePixelWidth
    {
        get => (int)GetValue(DecodePixelWidthProperty);
        set => SetValue(DecodePixelWidthProperty, value);
    }

    private CancellationTokenSource? _cts;

    private static void OnUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AsyncImage img)
            img.LoadImageAsync(e.NewValue as string);
    }

    private async void LoadImageAsync(string? url)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        if (string.IsNullOrWhiteSpace(url))
        {
            Source = null;
            return;
        }

        // 1) 先查缓存——命中则同步设置（不闪）
        if (Cache.TryGetValue(url, out var cached))
        {
            Source = cached;
            return;
        }

        try
        {
            var bytes = await Client.GetByteArrayAsync(url, token);
            if (token.IsCancellationRequested) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new System.IO.MemoryStream(bytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = DecodePixelWidth;
            bmp.EndInit();
            bmp.Freeze();

            // 存入缓存（先检查容量，超出则清理最早的一半）
            if (Cache.Count >= MaxCacheSize)
            {
                foreach (var k in Cache.Keys.Take(MaxCacheSize / 2).ToList())
                    Cache.TryRemove(k, out _);
            }
            Cache[url] = bmp;
            Source = bmp;
        }
        catch
        {
            Source = null;
        }
    }

    /// <summary>清空全局图片缓存（切换数据源时调用）。</summary>
    public static void ClearCache()
    {
        Cache.Clear();
    }

    /// <summary>预加载 URL 到缓存（不绑定到控件）。翻页前提前下载。</summary>
    public static void PreloadUrl(string url)
    {
        if (string.IsNullOrEmpty(url) || Cache.ContainsKey(url)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                if (Cache.ContainsKey(url)) return;
                var bytes = await Client.GetByteArrayAsync(url);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new System.IO.MemoryStream(bytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 200;
                bmp.EndInit();
                bmp.Freeze();
                if (Cache.Count >= MaxCacheSize)
                {
                    foreach (var k in Cache.Keys.Take(MaxCacheSize / 2).ToList())
                        Cache.TryRemove(k, out _);
                }
                Cache[url] = bmp;
            }
            catch { /* 预加载失败忽略 */ }
        });
    }
}
