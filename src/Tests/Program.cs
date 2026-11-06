using System.Buffers.Binary;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RecursiveMediaPlayer;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    [STAThread]
    private static async Task Main()
    {
        var testRoot = Path.GetFullPath(Path.Combine("TestResults", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(testRoot);
        Environment.SetEnvironmentVariable("RMP_DATA_DIRECTORY", Path.Combine(testRoot, "data"));
        var sorted = new[] { "a10.jpg", "a002.jpg", "a2.jpg", "a1.jpg", "a999999999999999999999.jpg" }.OrderBy(x => x, NaturalPathComparer.Instance).ToArray();
        Check(sorted.SequenceEqual(new[] { "a1.jpg", "a2.jpg", "a002.jpg", "a10.jpg", "a999999999999999999999.jpg" }), "自然排序");
        foreach (var count in new[] { 0, 1, 2, 10000 })
        {
            var order = new PlaybackOrder(count, true, true);
            int? last = null;
            for (int round = 0; round < 8; round++)
            {
                var seen = new HashSet<int>();
                for (int i = 0; i < count; i++)
                {
                    int? n = order.Next(); Check(n.HasValue && seen.Add(n.Value), "随机轮内无重复");
                    if (i == 0 && count > 1) Check(n != last, "跨轮避免重复");
                    last = n;
                }
                if (count == 0) Check(order.Next() == null, "空列表");
            }
        }
        var finite = new PlaybackOrder(3, false, false);
        Check(finite.Next() == 0 && finite.Next() == 1 && finite.Next() == 2 && finite.Next() == null, "非循环结束");
        var media = Path.Combine(testRoot, "media"); Directory.CreateDirectory(Path.Combine(media, "sub"));
        WriteImage(Path.Combine(media, "2.PNG"), 80, 40);
        WriteImage(Path.Combine(media, "sub", "10.jpg"), 40, 80);
        File.WriteAllText(Path.Combine(media, "bad.jpeg"), "corrupt");
        WriteMp4(Path.Combine(media, "3.MP4"), 1920, 1080, true);
        Check(Mp4Dimensions.Read(Path.Combine(media, "3.MP4")) == (1080, 1920), "MP4旋转元数据");
        var s = new Settings { Root = media };
        var scanner = new MediaScanner(s);
        var initial = await scanner.ScanAsync(new(), null, default);
        Check(initial.Count == 3, "递归扫描、扩展名与坏文件");
        s.ImageAspect = Aspect.Landscape; s.VideoAspect = Aspect.Portrait;
        Check(initial.Count(i => i.Matches(s)) == 2, "图片视频独立比例筛选");
        s.ImageAspect = Aspect.Portrait; s.VideoAspect = Aspect.Landscape;
        Check(initial.Count(i => i.Matches(s)) == 1, "相反比例筛选");
        var cache = await scanner.LoadCacheAsync(default);
        Check(cache.SequenceEqual(initial), "缓存往返");
        foreach (var source in new[] { initial, cache })
        {
            s.ImageAspect = Aspect.Disabled; s.VideoAspect = Aspect.All;
            Check(source.Where(i => i.Matches(s)).SequenceEqual(source.Where(i => i.Video)), "禁用图片仅保留视频，含缓存路径");
            s.ImageAspect = Aspect.All; s.VideoAspect = Aspect.Disabled;
            Check(source.Where(i => i.Matches(s)).SequenceEqual(source.Where(i => !i.Video)), "禁用视频仅保留图片，含缓存路径");
            s.ImageAspect = s.VideoAspect = Aspect.Disabled;
            Check(!source.Any(i => i.Matches(s)), "同时禁用返回空列表");
        }
        s.Save();
        var disabledSettings = Settings.Load();
        Check(disabledSettings.ImageAspect == Aspect.Disabled && disabledSettings.VideoAspect == Aspect.Disabled, "禁用配置持久化");
        File.WriteAllText(Path.Combine(Settings.DataDirectory, "config.json"), "{\"ImageAspect\":1,\"VideoAspect\":2}");
        var legacySettings = Settings.Load();
        Check(legacySettings.ImageAspect == Aspect.Landscape && legacySettings.VideoAspect == Aspect.Portrait, "旧配置枚举值兼容");
        s.ImageAspect = Aspect.Portrait; s.VideoAspect = Aspect.Landscape;
        var again = await scanner.ScanAsync(cache, null, default);
        Check(ReferenceEquals(cache[0], again[0]), "未修改文件重用缓存记录");
        File.Delete(Path.Combine(media, "2.PNG")); WriteImage(Path.Combine(media, "4.png"), 50, 50);
        WriteImage(Path.Combine(media, "sub", "10.jpg"), 90, 30);
        var changed = await scanner.ScanAsync(cache, null, default);
        Check(changed.Count == 3 && changed.Any(i => i.Width == 90) && changed.All(i => !i.Path.EndsWith("2.PNG")), "增删改增量更新");
        s.Recursive = false;
        Check((await new MediaScanner(s).ScanAsync(new(), null, default)).Count == 2, "不包含子目录");
        s.Save(); Check(Settings.Load().VideoAspect == s.VideoAspect && Settings.Load().Root == media, "配置保存");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        bool cancelled = false;
        try { await scanner.ScanAsync(cache, null, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "扫描可取消");
        s.Save();
        var cacheDirectory = Path.Combine(Settings.DataDirectory, "Cache");
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(Path.Combine(cacheDirectory, "stale-cache.json"), "old");
        Check(Directory.Exists(Settings.DataDirectory), "清空前存在配置和缓存");
        Settings.ClearStoredData();
        Check(!Directory.Exists(Settings.DataDirectory), "清空配置和缓存目录");
        Settings.ClearStoredData();
        Check(!Directory.Exists(Settings.DataDirectory), "清空操作可重复执行");
        Check(Directory.Exists(media) && File.Exists(Path.Combine(media, "4.png")), "清空不会删除媒体文件夹");
        var clearedSettings = Settings.Load();
        Check(clearedSettings.Root == "" && clearedSettings.ImageMilliseconds == 3000 && clearedSettings.Loop && clearedSettings.Recursive, "清空后恢复默认配置");
        Console.WriteLine($"PASS: {assertions} assertions. Fixtures: {testRoot}");
    }
    private static void WriteImage(string path, int w, int h)
    {
        var pixels = new byte[w * h * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 180; pixels[i + 1] = 100; pixels[i + 2] = 35; pixels[i + 3] = 255; }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        BitmapEncoder encoder = Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ? new JpegBitmapEncoder() : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void WriteMp4(string path, int w, int h, bool rotated)
    {
        var tkhd = new byte[84];
        BinaryPrimitives.WriteInt32BigEndian(tkhd.AsSpan(40), rotated ? 0 : 65536);
        BinaryPrimitives.WriteInt32BigEndian(tkhd.AsSpan(44), rotated ? 65536 : 0);
        BinaryPrimitives.WriteInt32BigEndian(tkhd.AsSpan(76), w << 16);
        BinaryPrimitives.WriteInt32BigEndian(tkhd.AsSpan(80), h << 16);
        File.WriteAllBytes(path, Box("moov", Box("trak", Box("tkhd", tkhd))));
    }
    private static byte[] Box(string type, byte[] data)
    {
        byte[] b = new byte[data.Length + 8]; BinaryPrimitives.WriteInt32BigEndian(b, b.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(b, 4); data.CopyTo(b, 8); return b;
    }
}
