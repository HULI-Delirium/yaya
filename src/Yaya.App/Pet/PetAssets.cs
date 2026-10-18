using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;

namespace Yaya.App.Pet;

/// <summary>
/// 宠物图资产加载器：启动时一次性加载 6 张图并缓存，三级回退，任何一级失败都不崩溃：
///   1) 磁盘文件（exe 旁 Assets/pet/，保留“放图覆盖”的自由度，换图不改代码）
///   2) 内嵌资源（单文件发布时 Content 不打包，从这里读）
///   3) XAML 矢量占位图（缺哪张在日志点名）
/// </summary>
public sealed class PetAssets
{
    private const string ResourcePrefix = "Yaya.App.Assets.pet.";

    private readonly IReadOnlyDictionary<PetVisualState, ImageSource> _images;

    public PetAssets()
    {
        var placeholder = (ImageSource)Application.Current.FindResource("PetPlaceholderImage");
        var images = new Dictionary<PetVisualState, ImageSource>();
        var diskCount = 0;
        var embeddedCount = 0;
        foreach (var state in Enum.GetValues<PetVisualState>())
        {
            var loaded = Load(state, placeholder, out var fromDisk);
            images[state] = loaded;
            if (ReferenceEquals(loaded, placeholder))
            {
                continue;
            }

            if (fromDisk)
            {
                diskCount++;
            }
            else
            {
                embeddedCount++;
            }
        }

        _images = images;
        // 联调/发布验收的正向证明：真图几张、来源分布、占位几张一目了然
        Log.Information("宠物图加载完成：真图 {RealCount}/{Total}（磁盘 {DiskCount} / 内嵌 {EmbeddedCount}），占位 {PlaceholderCount}",
            diskCount + embeddedCount, images.Count, diskCount, embeddedCount, images.Count - diskCount - embeddedCount);
    }

    /// <summary>按视觉状态取图；返回值永不为空（缺失时已回退占位图）。</summary>
    public ImageSource Get(PetVisualState state) => _images[state];

    private static ImageSource Load(PetVisualState state, ImageSource placeholder, out bool fromDisk)
    {
        fromDisk = false;
        var fileName = PetVisualStateCatalog.FileNameFor(state);
        try
        {
            // 1) 磁盘优先：允许用户/美术在 exe 旁放同名 PNG 覆盖内嵌版本
            var diskPath = Path.Combine(AppContext.BaseDirectory, "Assets", "pet", fileName);
            if (File.Exists(diskPath))
            {
                fromDisk = true;
                return CreateBitmap(bitmap => bitmap.UriSource = new Uri(diskPath, UriKind.Absolute));
            }

            // 2) 内嵌资源：单文件发布的真图来源
            var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + fileName);
            if (resource is not null)
            {
                using (resource)
                {
                    return CreateBitmap(bitmap => bitmap.StreamSource = resource);
                }
            }

            Log.Warning("宠物图缺失（磁盘与内嵌资源均无此文件）：{File}", fileName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "宠物图加载失败，已回退矢量占位：{File}", fileName);
        }

        // 3) 矢量占位
        return placeholder;
    }

    private static BitmapImage CreateBitmap(Action<BitmapImage> configure)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.DecodePixelWidth = 256;   // 窗内仅显示 200px，限制解码内存（资源红线 ≤300MB）
        bitmap.CacheOption = BitmapCacheOption.OnLoad;   // 立即读入并释放文件流/句柄
        configure(bitmap);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
