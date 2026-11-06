using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RecursiveMediaPlayer;
public enum Aspect { All, Landscape, Portrait, Disabled }
public sealed class Settings
{
    public string Root { get; set; } = "";
    public int ImageMilliseconds { get; set; } = 3000;
    public Aspect ImageAspect { get; set; }
    public Aspect VideoAspect { get; set; }
    public bool Random { get; set; }
    public bool Fullscreen { get; set; } = true;
    public bool Loop { get; set; } = true;
    public bool Muted { get; set; }
    public bool Recursive { get; set; } = true;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 720;
    public static string DataDirectory => Environment.GetEnvironmentVariable("RMP_DATA_DIRECTORY") is { Length: > 0 } path
        ? Path.GetFullPath(path) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecursiveMediaPlayer");
    public static Settings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(DataDirectory, "config.json"))) ?? new();
            s.Root ??= "";
            s.ImageMilliseconds = Math.Clamp(s.ImageMilliseconds, 100, 3600000);
            s.WindowWidth = double.IsFinite(s.WindowWidth) ? Math.Clamp(s.WindowWidth, 320, 7680) : 1280;
            s.WindowHeight = double.IsFinite(s.WindowHeight) ? Math.Clamp(s.WindowHeight, 240, 4320) : 720;
            return s;
        }
        catch { return new(); }
    }
    public void Save() => AtomicJson.Write(Path.Combine(DataDirectory, "config.json"), this);
    public static void ClearStoredData()
    {
        if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true);
    }
}
public static class AtomicJson
{
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public sealed record MediaItem(string Path, long Length, long Modified, bool Video, int Width, int Height)
{
    public bool Matches(Settings s)
    {
        var aspect = Video ? s.VideoAspect : s.ImageAspect;
        return Width > 0 && Height > 0 && aspect switch
        {
            Aspect.All => true,
            Aspect.Landscape => Width >= Height,
            Aspect.Portrait => Width <= Height,
            _ => false
        };
    }
}
public sealed class NaturalPathComparer : IComparer<string>
{
    public static readonly NaturalPathComparer Instance = new();
    public int Compare(string? x, string? y)
    {
        x ??= ""; y ??= "";
        int a = 0, b = 0;
        while (a < x.Length && b < y.Length)
        {
            if (char.IsAsciiDigit(x[a]) && char.IsAsciiDigit(y[b]))
            {
                int sa = a, sb = b;
                while (a < x.Length && char.IsAsciiDigit(x[a])) a++;
                while (b < y.Length && char.IsAsciiDigit(y[b])) b++;
                int za = sa, zb = sb;
                while (za < a && x[za] == '0') za++;
                while (zb < b && y[zb] == '0') zb++;
                int c = (a - za).CompareTo(b - zb);
                if (c != 0) return c;
                c = string.Compare(x, za, y, zb, a - za, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                c = (a - sa).CompareTo(b - sb);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(x[a++]).CompareTo(char.ToUpperInvariant(y[b++]));
                if (c != 0) return c;
            }
        }
        return (x.Length - a).CompareTo(y.Length - b);
    }
}
public sealed class PlaybackOrder
{
    private readonly int[] indices;
    private readonly bool random, loop;
    private readonly Random rng = new();
    private int position;
    public bool AtCycleEnd => position == indices.Length;
    public PlaybackOrder(int count, bool random, bool loop, int avoidFirst = -1)
    {
        indices = Enumerable.Range(0, count).ToArray(); this.random = random; this.loop = loop;
        if (random) Shuffle(avoidFirst);
    }
    private void Shuffle(int last)
    {
        rng.Shuffle(indices);
        if (indices.Length > 1 && indices[0] == last) (indices[0], indices[1]) = (indices[1], indices[0]);
    }
    public int? Next()
    {
        if (indices.Length == 0) return null;
        if (position == indices.Length)
        {
            if (!loop) return null;
            int last = indices[^1]; position = 0;
            if (random) Shuffle(last);
        }
        return indices[position++];
    }
}
