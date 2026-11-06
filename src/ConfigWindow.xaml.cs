using System;
using System.IO;
using System.Windows;

namespace RecursiveMediaPlayer;
public partial class ConfigWindow : Window
{
    private Settings settings;
    private PlayerWindow? player;
    public ConfigWindow(Settings settings)
    {
        InitializeComponent(); this.settings = settings;
        PopulateSettings();
        Closed += (_, _) => Application.Current.Shutdown();
    }
    private void PopulateSettings()
    {
        Folder.Text = settings.Root; Duration.Text = settings.ImageMilliseconds.ToString();
        ImageAll.IsChecked = settings.ImageAspect == Aspect.All;
        ImageLandscape.IsChecked = settings.ImageAspect == Aspect.Landscape;
        ImagePortrait.IsChecked = settings.ImageAspect == Aspect.Portrait;
        ImageDisabled.IsChecked = settings.ImageAspect == Aspect.Disabled;
        VideoAll.IsChecked = settings.VideoAspect == Aspect.All;
        VideoLandscape.IsChecked = settings.VideoAspect == Aspect.Landscape;
        VideoPortrait.IsChecked = settings.VideoAspect == Aspect.Portrait;
        VideoDisabled.IsChecked = settings.VideoAspect == Aspect.Disabled;
        RandomOrder.IsChecked = settings.Random; Sequential.IsChecked = !settings.Random;
        Fullscreen.IsChecked = settings.Fullscreen; Windowed.IsChecked = !settings.Fullscreen;
        Loop.IsChecked = settings.Loop; Muted.IsChecked = settings.Muted; Recursive.IsChecked = settings.Recursive;
    }
    private void Browse(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "选择媒体根文件夹", UseDescriptionForTitle = true, SelectedPath = Folder.Text };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) Folder.Text = dialog.SelectedPath;
    }
    private void Start(object sender, RoutedEventArgs e)
    {
        Error.Text = "";
        Error.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28));
        if (!Directory.Exists(Folder.Text.Trim())) { Error.Text = "请选择有效的文件夹。"; return; }
        if (!int.TryParse(Duration.Text, out int ms) || ms < 100 || ms > 3600000) { Error.Text = "图片时长须为 100–3600000 毫秒的整数。"; return; }
        settings.Root = Path.GetFullPath(Folder.Text.Trim()); settings.ImageMilliseconds = ms;
        settings.ImageAspect = ImageDisabled.IsChecked == true ? Aspect.Disabled : ImageLandscape.IsChecked == true ? Aspect.Landscape : ImagePortrait.IsChecked == true ? Aspect.Portrait : Aspect.All;
        settings.VideoAspect = VideoDisabled.IsChecked == true ? Aspect.Disabled : VideoLandscape.IsChecked == true ? Aspect.Landscape : VideoPortrait.IsChecked == true ? Aspect.Portrait : Aspect.All;
        settings.Random = RandomOrder.IsChecked == true; settings.Fullscreen = Fullscreen.IsChecked == true;
        settings.Loop = Loop.IsChecked == true; settings.Muted = Muted.IsChecked == true; settings.Recursive = Recursive.IsChecked == true;
        try { settings.Save(); } catch (Exception ex) { Error.Text = "无法保存配置：" + ex.Message; return; }
        StartPlayback();
    }
    public void StartPlayback()
    {
        if (player != null) return;
        player = new PlayerWindow(settings);
        player.Closed += (_, _) => { player = null; Show(); Activate(); };
        player.Show(); Hide();
    }
    private void Exit(object sender, RoutedEventArgs e) => Close();
    private void ShowHelp(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();
    private void ClearData(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "这会删除软件保存的设置和媒体扫描缓存，并恢复默认选项。\n\n你的图片和视频文件不会被删除。清空后请选择媒体文件夹并重新开始播放，软件会重新扫描文件。\n\n确定清空吗？",
            "清空配置和缓存", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            Settings.ClearStoredData();
            settings = new Settings();
            PopulateSettings();
            Error.Text = "配置和缓存已清空。请选择媒体文件夹并重新开始播放。";
            Error.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
        }
        catch (Exception ex)
        {
            Error.Text = "清空失败：" + ex.Message;
            Error.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28));
        }
    }
}
