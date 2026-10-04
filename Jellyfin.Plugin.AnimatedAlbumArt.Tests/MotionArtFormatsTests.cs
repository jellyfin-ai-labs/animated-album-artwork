using Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;
using Xunit;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Tests;

public class MotionArtFormatsTests
{
    [Theory]
    [InlineData(".mp4,.mov", new[] { ".mp4", ".mov" })]
    [InlineData(" MOV , mp4 ", new[] { ".mov", ".mp4" })]
    [InlineData(".mp4,.mp4,.MP4", new[] { ".mp4" })]
    [InlineData(".mp4,.exe,.gif,,", new[] { ".mp4" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void ParsesExtensions(string? value, string[] expected)
    {
        Assert.Equal(expected, MotionArtFormats.ParseExtensions(value));
    }

    [Theory]
    [InlineData("cover-motion.mp4", true)]
    [InlineData("Cover-Motion.WEBM", true)]
    [InlineData("cover-motion.jpg", false)]
    [InlineData("cover.mp4", false)]
    [InlineData("cover-motion", false)]
    public void RecognizesSidecarNames(string name, bool expected)
    {
        Assert.Equal(expected, MotionArtFormats.IsSidecarName(name));
    }
}
