using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;
using Jellyfin.Plugin.AnimatedAlbumArt.Playback;
using Xunit;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Tests;

public class ArtworkEncoderInputTests
{
    [Fact]
    public async Task SplitsOnlyTopLevelSectionsAndCleansTemporaryInputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "artwork-input-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            // The ftyp bytes inside mdat must not be mistaken for another section.
            byte[] section = [0, 0, 0, 8, 102, 116, 121, 112, 0, 0, 0, 16, 109, 100, 97, 116, 0, 0, 0, 8, 102, 116, 121, 112];
            var path = Path.Combine(root, "source.mp4");
            await File.WriteAllBytesAsync(path, section.Concat(section).ToArray());
            var file = new FileInfo(path);
            var temporary = Path.Combine(root, "copy.tmp.mp4");
            using (var prepared = await ArtworkEncoderInput.PrepareAsync(new MotionArtFile(path, "video/mp4", file.Length, file.LastWriteTimeUtc), temporary, default))
            {
                Assert.Equal(2, prepared.Paths.Length);
                foreach (var part in prepared.Paths)
                {
                    Assert.Equal(section, await File.ReadAllBytesAsync(part));
                }

                Assert.Contains("concat", prepared.Arguments);
                Assert.Contains("file '1.mp4'", await File.ReadAllTextAsync(prepared.Arguments[^1]), StringComparison.Ordinal);
            }

            Assert.False(Directory.Exists(temporary + ".parts"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsBoxesBeyondFileBoundsWithoutPublishingInputs()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [255, 255, 255, 255, 102, 116, 121, 112]);
            await Assert.ThrowsAsync<InvalidDataException>(() => ArtworkEncoderInput.PrepareAsync(new MotionArtFile(path, "video/mp4", 8, DateTimeOffset.UtcNow), path + ".tmp.mp4", default));
            Assert.False(Directory.Exists(path + ".tmp.mp4.parts"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
