using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;

namespace Yaya.App.Pet;

/// <summary>
/// 宠物图资产加载器：启动时一次性加载 6 张 PNG 并缓存。
/// 任何一张缺失/解码失败都回退到 XAML 矢量占位图（key=PetPlaceholderImage），
/// 程序绝不因缺图崩溃；缺哪张会在日志中点名。
/// </summary>
public sealed class PetAssets
{
    private readonly IReadOnlyDictionary<PetVisualState, ImageSource> _images;

    public PetAssets()
    {
        var placeholder = (ImageSource)Application.Current.FindResource("PetPlaceholderImage");
        var images = new Dictionary<PetVisualState, ImageSource>();
        foreach (var state in Enum.GetValues<PetVisualState>())
        {
            images[state] = LoadOrPlaceholder(state, placeholder);
        }

        _images = images;
        // 联调验收用的正向证明：真图几张、占位几张一目了然
        var realCount = images.Values.Count(image => !ReferenceEquals(image, placeholder));
        Log.Information("宠物图加载完成：真图 {RealCount}/{Total}，占位 {PlaceholderCount}",
            realCount, images.Count, images.Count - realCount);
    }

    /// <summary>按视觉状态取图；返回值永不为空（缺失时已回退占位图）。</summary>
    public ImageSource Get(PetVisualState state) => _images[state];

    private static ImageSource LoadOrPlaceholder(PetVisualState state, ImageSource placeholder)
    {
        var fileName = PetVisualStateCatalog.FileNameFor(state);
        try
        {
            // 相对 exe 目录解析（而非进程 CWD）：Content 发布的 Assets 与程序同目录
            var fullPath = Path.Combine(AppContext.BaseDirectory, "Assets", "pet", fileName);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            bitmap.DecodePixelWidth = 256;   // 窗内仅显示 200px，限制解码内存（资源红线 ≤300MB）
            bitmap.CacheOption = BitmapCacheOption.OnLoad;   // 立即读入并释放文件句柄
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            // IOException / Uri 异常等统一按缺图处理：回退占位 + 点名日志
            Log.Warning(ex, "宠物图缺失或加载失败，已回退矢量占位：{File}", fileName);
            return placeholder;
        }
    }
}
