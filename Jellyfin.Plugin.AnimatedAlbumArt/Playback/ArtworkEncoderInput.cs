using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Playback;

/// <summary>
/// Adapts independently initialized MP4 sections for FFmpeg's concat demuxer.
/// </summary>
internal sealed class ArtworkEncoderInput : IDisposable
{
    private readonly string? _directory;

    private ArtworkEncoderInput(string[] paths, string[] arguments, string? directory = null)
    {
        Paths = paths;
        Arguments = arguments;
        _directory = directory;
    }

    public string[] Paths { get; }

    public string[] Arguments { get; }

    public static async Task<ArtworkEncoderInput> PrepareAsync(MotionArtFile source, string temporaryPath, CancellationToken cancellationToken)
    {
        if (source.ContentType is not "video/mp4" and not "video/quicktime")
        {
            return new ArtworkEncoderInput([source.Path], ["-i", source.Path]);
        }

        using var input = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        var starts = new List<long>();
        var header = new byte[16];
        long offset = 0;
        while (offset < input.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            input.Position = offset;
            if (input.Length - offset < 8)
            {
                throw new InvalidDataException("Incomplete MP4 box header.");
            }

            await input.ReadExactlyAsync(header.AsMemory(0, 8), cancellationToken).ConfigureAwait(false);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var minimum = 8;
            if (size == 1)
            {
                await input.ReadExactlyAsync(header.AsMemory(8, 8), cancellationToken).ConfigureAwait(false);
                size = checked((long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8)));
                minimum = 16;
            }
            else if (size == 0)
            {
                size = input.Length - offset;
            }

            if (size < minimum || size > input.Length - offset)
            {
                throw new InvalidDataException("Invalid MP4 box size.");
            }

            if (Encoding.ASCII.GetString(header, 4, 4) == "ftyp")
            {
                starts.Add(offset);
                if (starts.Count > 128)
                {
                    throw new InvalidDataException("Too many independently initialized MP4 sections.");
                }
            }

            offset += size;
        }

        if (starts.Count <= 1)
        {
            return new ArtworkEncoderInput([source.Path], ["-i", source.Path]);
        }

        starts[0] = 0;
        starts.Add(input.Length);
        var directory = temporaryPath + ".parts";
        Directory.CreateDirectory(directory);
        try
        {
            var manifest = new StringBuilder("ffconcat version 1.0\n");
            var buffer = new byte[65536];
            for (var index = 0; index < starts.Count - 1; index++)
            {
                var name = index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".mp4";
                input.Position = starts[index];
                using var output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);
                var remaining = starts[index + 1] - starts[index];
                while (remaining > 0)
                {
                    var count = (int)Math.Min(remaining, buffer.Length);
                    await input.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    remaining -= count;
                }

                manifest.Append("file '").Append(name).Append("'\n");
            }

            var list = Path.Combine(directory, "input.ffconcat");
            await File.WriteAllTextAsync(list, manifest.ToString(), cancellationToken).ConfigureAwait(false);
            return new ArtworkEncoderInput(System.Linq.Enumerable.Range(0, starts.Count - 1).Select(index => Path.Combine(directory, index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".mp4")).ToArray(), ["-f", "concat", "-safe", "1", "-i", list], directory);
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    public void Dispose()
    {
        if (_directory is not null)
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
