using System;
using System.Windows;

namespace RecursiveMediaPlayer;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = Settings.Load();
        var config = new ConfigWindow(settings);
        MainWindow = config;
        if (System.IO.Directory.Exists(settings.Root)) config.StartPlayback();
        else config.Show();
    }
}
