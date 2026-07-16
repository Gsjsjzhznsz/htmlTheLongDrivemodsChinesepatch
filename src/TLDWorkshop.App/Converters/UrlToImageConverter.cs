using System.Globalization;
using System.Net.Http;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace TLDWorkshop.App.Converters;

/// <summary>
/// 把图片 URL 字符串转换为 BitmapImage。
///
/// 重要：IValueConverter.Convert 必须同步返回 ImageSource。
/// 不能返回 Task——WPF 绑定不会 await Task。
/// 所以这里用 .Result 同步阻塞下载。图片很小（200px 解码），通常 &lt;1 秒。
/// 用 AsyncCompletedEventHandler 模式也可以但更复杂，这里保持简单。
/// </summary>
public sealed class UrlToImageConverter : IValueConverter
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    });
    static UrlToImageConverter()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        Client.Timeout = TimeSpan.FromSeconds(15);
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 已弃用：此 Converter 同步阻塞下载会卡 UI 线程，且会 wrap AggregateException。
        // 项目已改用 AsyncImage 控件（异步加载，带缓存），请直接使用 <controls:AsyncImage Url="..."/> 代替。
        // 此处直接返回 null，避免阻塞 UI 线程。
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
