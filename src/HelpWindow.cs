using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RecursiveMediaPlayer;

public sealed class HelpWindow : Window
{
    public HelpWindow()
    {
        Title = "使用教程 · 递归媒体播放器";
        Width = 720; Height = 680; MinWidth = 420; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(244, 246, 250));
        FontFamily = new FontFamily("Microsoft YaHei UI");

        var layout = new DockPanel { Margin = new Thickness(16) };
        var close = new Button
        {
            Content = "关闭教程", IsCancel = true, MinWidth = 100,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0)
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        layout.Children.Add(close);

        var document = new FlowDocument
        {
            FontFamily = FontFamily, FontSize = 15, PagePadding = new Thickness(24),
            Background = Brushes.White, Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
        };
        // The in-app tutorial and the Markdown file share one embedded source.
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RecursiveMediaPlayer.UserGuide");
        using var reader = stream == null ? null : new StreamReader(stream);
        string text = reader?.ReadToEnd() ?? "使用教程暂时无法读取，请查看项目目录中的《使用教程.md》。";
        foreach (string line in text.Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            int heading = 0;
            while (heading < line.Length && line[heading] == '#') heading++;
            bool isHeading = heading > 0 && heading < line.Length && line[heading] == ' ';
            string content = isHeading ? line[(heading + 1)..] : line.StartsWith("- ") ? "•  " + line[2..] : line;
            var paragraph = new Paragraph(new Run(content))
            {
                Margin = new Thickness(0, isHeading ? 18 : 0, 0, 10), LineHeight = 26
            };
            if (isHeading)
            {
                paragraph.FontSize = heading == 1 ? 26 : heading == 2 ? 20 : 17;
                paragraph.FontWeight = FontWeights.SemiBold;
            }
            document.Blocks.Add(paragraph);
        }
        layout.Children.Add(new FlowDocumentScrollViewer
        {
            Document = document, IsToolBarVisible = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        Content = layout;
    }
}
