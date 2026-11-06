using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RecursiveMediaPlayer;
public sealed class PlayerWindow : Window
{
    private readonly Settings settings;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Grid surface = new() { Background = Brushes.Black };
    private readonly TextBlock status = new() { Foreground = Brushes.White, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image image = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly MediaElement[] videos = { new(), new() };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly Stopwatch imageClock = new(), mouseClock = Stopwatch.StartNew();
    private List<MediaItem> items = new();
    private List<MediaItem>? pendingItems;
    private PlaybackOrder? order;
    private readonly HashSet<string> failed = new(StringComparer.OrdinalIgnoreCase);
    private Task<Prepared?>? next;
    private Prepared? current;
    private int activeVideo = -1;
    private bool paused, advancing, closed;
    private sealed record Prepared(MediaItem Item, BitmapSource? Bitmap, int VideoSlot);

    public PlayerWindow(Settings settings)
    {
        this.settings = settings;
        Title = "递归媒体播放器"; Background = Brushes.Black;
        Width = settings.WindowWidth; Height = settings.WindowHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (settings.Fullscreen) { WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; }
        Content = surface; surface.Children.Add(image);
        foreach (var video in videos)
        {
            video.LoadedBehavior = MediaState.Manual; video.UnloadedBehavior = MediaState.Manual;
            video.Stretch = Stretch.Uniform; video.Opacity = 0; video.IsHitTestVisible = false;
            video.MediaEnded += (_, _) => { if (current?.VideoSlot == Array.IndexOf(videos, video)) _ = AdvanceAsync(); };
            video.MediaFailed += (_, _) => { if (current?.VideoSlot == Array.IndexOf(videos, video)) { failed.Add(current.Item.Path); _ = AdvanceAsync(); } };
            surface.Children.Add(video);
        }
        surface.Children.Add(status);
        MouseLeftButtonDown += (_, _) => Close();
        MouseMove += (_, _) => { mouseClock.Restart(); Cursor = Cursors.Arrow; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Space) { TogglePause(); e.Handled = true; }
        };
        timer.Tick += (_, _) =>
        {
            if (mouseClock.ElapsedMilliseconds >= 2000) Cursor = Cursors.None;
            if (!paused && current is { VideoSlot: -1 } && imageClock.ElapsedMilliseconds >= settings.ImageMilliseconds) _ = AdvanceAsync();
        };
        Loaded += async (_, _) => { timer.Start(); await InitializeAsync(); };
        Closed += (_, _) =>
        {
            closed = true; lifetime.Cancel(); timer.Stop();
            foreach (var video in videos) { video.Close(); video.Source = null; }
            image.Source = null;
            if (!settings.Fullscreen && WindowState == WindowState.Normal)
            {
                settings.WindowWidth = ActualWidth; settings.WindowHeight = ActualHeight;
                try { settings.Save(); } catch { }
            }
        };
    }
    private async Task InitializeAsync()
    {
        try
        {
            var scanner = new MediaScanner(settings);
            var cache = await scanner.LoadCacheAsync(lifetime.Token);
            if (closed) return;
            var playable = cache.Where(i => i.Matches(settings)).ToList();
            if (playable.Count > 0)
            {
                SetItems(playable);
                // Launch refresh before decoding the first item; playback never awaits it.
                _ = RefreshAsync(scanner, cache);
                await AdvanceAsync();
            }
            else
            {
                status.Text = "正在扫描… 已发现 0 个文件";
                var progress = new Progress<int>(n => { if (!closed) status.Text = $"正在扫描… 已发现 {n} 个文件"; });
                var scanned = await scanner.ScanAsync(cache, progress, lifetime.Token);
                if (closed) return;
                SetItems(scanned.Where(i => i.Matches(settings)).ToList()); await AdvanceAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed) status.Text = "扫描失败：" + ex.Message + "\n点击返回配置窗口"; }
    }
    private async Task RefreshAsync(MediaScanner scanner, List<MediaItem> cache)
    {
        try
        {
            var updated = await scanner.ScanAsync(cache, null, lifetime.Token);
            if (closed) return;
            var filtered = updated.Where(i => i.Matches(settings)).ToList();
            if (!items.SequenceEqual(filtered)) pendingItems = filtered;
            if (pendingItems == null) return;
            if (current == null && !advancing)
            {
                SetItems(pendingItems); pendingItems = null; await AdvanceAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Trace.WriteLine(ex); }
    }
    private void SetItems(List<MediaItem> list)
    {
        items = list;
        int avoid = current == null ? -1 : items.FindIndex(i => string.Equals(i.Path, current.Item.Path, StringComparison.OrdinalIgnoreCase));
        order = new(items.Count, settings.Random, settings.Loop, avoid);
    }
    private async Task<Prepared?> PrepareNextAsync()
    {
        if (pendingItems != null && order?.AtCycleEnd == true && settings.Loop)
        {
            SetItems(pendingItems); pendingItems = null;
        }
        int attempts = items.Count;
        while (!closed && attempts-- > 0)
        {
            int? index = order?.Next();
            if (index == null) return null;
            var item = items[index.Value];
            if (failed.Contains(item.Path)) continue;
            try
            {
                if (item.Video)
                {
                    int slot = activeVideo == 0 ? 1 : 0;
                    var video = videos[slot];
                    video.Close(); video.Opacity = 0; video.IsMuted = true;
                    var ready = new TaskCompletionSource<bool>();
                    RoutedEventHandler opened = (_, _) => ready.TrySetResult(true);
                    EventHandler<ExceptionRoutedEventArgs> error = (_, _) => ready.TrySetResult(false);
                    video.MediaOpened += opened; video.MediaFailed += error;
                    try
                    {
                        video.Source = new Uri(item.Path); video.Pause();
                        if (!await ready.Task.WaitAsync(TimeSpan.FromSeconds(12), lifetime.Token)) throw new IOException("视频无法打开");
                        video.Position = TimeSpan.Zero;
                        return new(item, null, slot);
                    }
                    finally { video.MediaOpened -= opened; video.MediaFailed -= error; }
                }
                int decodeWidth = Math.Max(1, (int)(Math.Max(ActualWidth, ActualHeight) * VisualTreeHelper.GetDpi(this).DpiScaleX));
                var bitmap = await Task.Run(() =>
                {
                    using var stream = File.OpenRead(item.Path);
                    var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = Math.Min(item.Width, decodeWidth); bmp.StreamSource = stream; bmp.EndInit(); bmp.Freeze(); return bmp;
                }, lifetime.Token).WaitAsync(TimeSpan.FromSeconds(12), lifetime.Token);
                return new(item, bitmap, -1);
            }
            catch (OperationCanceledException) { return null; }
            catch { failed.Add(item.Path); }
        }
        return null;
    }
    private async Task AdvanceAsync()
    {
        if (closed || advancing) return;
        advancing = true;
        try
        {
            var prepared = await (next ?? PrepareNextAsync()); next = null;
            if (closed) return;
            if (prepared == null && pendingItems != null && settings.Loop)
            {
                SetItems(pendingItems); pendingItems = null;
                prepared = await PrepareNextAsync();
                if (closed) return;
            }
            if (prepared == null)
            {
                current = null; imageClock.Reset();
                foreach (var video in videos) { video.Stop(); video.Opacity = 0; }
                image.Source = null; status.Text = items.Count == 0 || failed.Count >= items.Count ? "未找到可播放文件\n点击返回配置窗口" : "播放结束\n点击返回配置窗口";
                return;
            }
            int previousVideo = activeVideo;
            current = prepared; activeVideo = prepared.VideoSlot;
            status.Text = "";
            if (activeVideo >= 0)
            {
                var video = videos[activeVideo]; video.IsMuted = settings.Muted; video.Opacity = 1;
                if (!paused) video.Play();
            }
            else image.Source = prepared.Bitmap;
            if (previousVideo >= 0 && previousVideo != activeVideo) { videos[previousVideo].Opacity = 0; videos[previousVideo].Stop(); }
            image.Visibility = activeVideo < 0 ? Visibility.Visible : Visibility.Hidden;
            imageClock.Reset(); if (!paused) imageClock.Start();
            next = PrepareNextAsync();
        }
        catch (OperationCanceledException) { }
        finally { advancing = false; }
    }
    private void TogglePause()
    {
        paused = !paused;
        if (paused) { imageClock.Stop(); if (activeVideo >= 0) videos[activeVideo].Pause(); }
        else { if (current != null) imageClock.Start(); if (activeVideo >= 0) videos[activeVideo].Play(); }
    }
}
