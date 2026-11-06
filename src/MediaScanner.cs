using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace RecursiveMediaPlayer;
public sealed class MediaScanner
{
    private readonly string root, cachePath;
    private readonly bool recursive;
    public MediaScanner(Settings settings)
    {
        root = Path.GetFullPath(settings.Root); recursive = settings.Recursive;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant() + "|" + recursive)));
        cachePath = Path.Combine(Settings.DataDirectory, "Cache", key + ".json");
    }
    public Task<List<MediaItem>> LoadCacheAsync(CancellationToken token) => Task.Run(() =>
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<MediaItem>>(File.ReadAllText(cachePath)) ?? new();
            // Cache files are local data; reject entries outside the selected root.
            var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            return items.Where(i => i.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (recursive || string.Equals(Path.GetDirectoryName(i.Path), root, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        catch { return new List<MediaItem>(); }
    }, token);
    public Task<List<MediaItem>> ScanAsync(List<MediaItem> previous, IProgress<int>? progress, CancellationToken token) => Task.Run(() =>
    {
        var old = previous.DistinctBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
        var result = new List<MediaItem>();
        var directories = new Stack<string>(); directories.Push(root);
        int found = 0;
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = false };
        while (directories.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string dir = directories.Pop();
            try
            {
                if (recursive) foreach (var sub in Directory.EnumerateDirectories(dir, "*", options)) directories.Push(sub);
                foreach (var path in Directory.EnumerateFiles(dir, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext is not (".png" or ".jpg" or ".jpeg" or ".mp4")) continue;
                    if (++found % 32 == 0) progress?.Report(found);
                    try
                    {
                        var info = new FileInfo(path);
                        if (old.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Modified == info.LastWriteTimeUtc.Ticks) result.Add(cached);
                        else
                        {
                            bool video = ext == ".mp4";
                            var (w, h) = video ? Mp4Dimensions.Read(path) : ReadImage(path);
                            result.Add(new(path, info.Length, info.LastWriteTimeUtc.Ticks, video, w, h));
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { /* Unreadable media is retried next scan. */ }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        token.ThrowIfCancellationRequested();
        result.Sort((a, b) => NaturalPathComparer.Instance.Compare(a.Path, b.Path));
        try { AtomicJson.Write(cachePath, result); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        progress?.Report(found);
        return result;
    }, token);
    private static (int, int) ReadImage(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        return (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
    }
}
public static class Mp4Dimensions
{
    // ISO BMFF track header stores display dimensions and the rotation matrix.
    public static (int, int) Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        return Boxes(reader, stream.Length, 0);
    }
    private static uint U32(BinaryReader r) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(r.ReadBytes(4));
    private static (int, int) Boxes(BinaryReader r, long end, int depth)
    {
        if (depth > 5) return (0, 0);
        while (r.BaseStream.Position + 8 <= end)
        {
            long start = r.BaseStream.Position;
            ulong size = U32(r); string type = Encoding.ASCII.GetString(r.ReadBytes(4));
            int header = 8;
            if (size == 1) { size = ((ulong)U32(r) << 32) | U32(r); header = 16; }
            if (size == 0) size = (ulong)(end - start);
            if (size < (ulong)header || size > (ulong)(end - start)) break;
            long next = start + (long)size;
            if (type is "moov" or "trak")
            {
                var value = Boxes(r, next, depth + 1);
                if (value.Item1 > 0 && value.Item2 > 0) return value;
            }
            else if (type == "tkhd")
            {
                int version = r.ReadByte();
                int matrixOffset = version == 1 ? 52 : 40;
                if (size >= (ulong)(header + matrixOffset + 44))
                {
                    r.BaseStream.Position = start + header + matrixOffset;
                    int a = unchecked((int)U32(r)), b = unchecked((int)U32(r));
                    r.BaseStream.Position += 28;
                    int w = (int)(U32(r) >> 16), h = (int)(U32(r) >> 16);
                    if (a == 0 && b != 0) (w, h) = (h, w);
                    if (w > 0 && h > 0) return (w, h);
                }
            }
            r.BaseStream.Position = next;
        }
        return (0, 0);
    }
}
