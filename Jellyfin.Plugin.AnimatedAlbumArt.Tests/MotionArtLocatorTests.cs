using System;
using System.IO;
using Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;
using Xunit;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Tests;

public sealed class MotionArtLocatorTests : IDisposable
{
    private static readonly string[] _mp4ThenMov = [".mp4", ".mov"];
    private readonly string _dir = Directory.CreateTempSubdirectory("motion-art-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Touch(string name, int bytes = 1) => File.WriteAllBytes(Path.Combine(_dir, name), new byte[bytes]);

    [Fact]
    public void ReturnsNullWhenNoSidecar()
    {
        Touch("cover.jpg");
        Touch("01 - Song.mp3");
        Assert.Null(MotionArtLocator.FindInDirectory(_dir, _mp4ThenMov));
    }

    [Fact]
    public void ReturnsNullForMissingDirectory()
    {
        Assert.Null(MotionArtLocator.FindInDirectory(Path.Combine(_dir, "missing"), _mp4ThenMov));
    }

    [Theory]
    [InlineData("cover-motion.mp4", "video/mp4")]
    [InlineData("cover-motion.mov", "video/quicktime")]
    [InlineData("COVER-MOTION.MP4", "video/mp4")]
    [InlineData("Cover-Motion.Mov", "video/quicktime")]
    public void FindsSidecarCaseInsensitively(string name, string contentType)
    {
        Touch(name, bytes: 42);
        var file = MotionArtLocator.FindInDirectory(_dir, _mp4ThenMov);
        Assert.NotNull(file);
        Assert.Equal(contentType, file.ContentType);
        Assert.Equal(42, file.Length);
        Assert.Equal(Path.Combine(_dir, name), file.Path);
    }

    [Fact]
    public void PrefersEarlierExtension()
    {
        Touch("cover-motion.mov");
        Touch("cover-motion.mp4");
        Assert.EndsWith(".mp4", MotionArtLocator.FindInDirectory(_dir, _mp4ThenMov)!.Path, StringComparison.Ordinal);
        Assert.EndsWith(".mov", MotionArtLocator.FindInDirectory(_dir, [".mov", ".mp4"])!.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresExtensionsNotAllowed()
    {
        Touch("cover-motion.webm");
        Assert.Null(MotionArtLocator.FindInDirectory(_dir, _mp4ThenMov));
        Assert.Null(MotionArtLocator.FindInDirectory(_dir, []));
    }

    [Theory]
    [InlineData("cover-motion.backup.mp4")]
    [InlineData("my-cover-motion.mp4")]
    [InlineData("cover-motion.mp4.part")]
    public void IgnoresSimilarNames(string name)
    {
        Touch(name);
        Assert.Null(MotionArtLocator.FindInDirectory(_dir, _mp4ThenMov));
    }

    [Fact]
    public void IgnoresSubdirectories()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Disc 1"));
        File.WriteAllBytes(Path.Combine(_dir, "Disc 1", "cover-motion.mp4"), [0]);
        Assert.Null(MotionArtLocator.FindInDirectory(_dir, _mp4ThenMov));
    }

    [Fact]
    public void TagChangesWhenFileChanges()
    {
        Touch("cover-motion.mp4", bytes: 10);
        var before = MotionArtLocator.FindInDirectory(_dir, _mp4ThenMov)!.Tag;
        Touch("cover-motion.mp4", bytes: 11);
        Assert.NotEqual(before, MotionArtLocator.FindInDirectory(_dir, _mp4ThenMov)!.Tag);
    }
}
