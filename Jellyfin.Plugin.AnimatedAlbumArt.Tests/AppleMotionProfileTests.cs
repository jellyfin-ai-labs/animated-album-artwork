using System.Linq;
using Jellyfin.Plugin.AnimatedAlbumArt.Diagnostics;
using Xunit;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Tests;

public class AppleMotionProfileTests
{
    private static ProbeResult Probe(
        int width = 3840,
        int height = 3840,
        string codec = "h264",
        string frameRate = "30000/1001",
        string duration = "20.0",
        string bitRate = "60000000",
        bool audio = false,
        string? colorPrimaries = "bt709",
        string? sampleAspectRatio = "1:1")
    {
        var video = new ProbeStreamInfo
        {
            CodecType = "video",
            CodecName = codec,
            Width = width,
            Height = height,
            AverageFrameRate = frameRate,
            ColorPrimaries = colorPrimaries,
            SampleAspectRatio = sampleAspectRatio,
        };
        return new ProbeResult
        {
            Streams = audio ? [video, new ProbeStreamInfo { CodecType = "audio", CodecName = "aac" }] : [video],
            Format = new ProbeFormat { FormatName = "mov,mp4,m4a,3gp,3g2,mj2", Duration = duration, BitRate = bitRate },
        };
    }

    private static bool? Check(ProbeResult probe, string name) =>
        AppleMotionProfile.Evaluate(probe).Single(c => c.Name == name).Passed;

    [Fact]
    public void ConformingSquareFilePassesEverything()
    {
        Assert.All(AppleMotionProfile.Evaluate(Probe()), c => Assert.True(c.Passed, c.Name));
    }

    [Fact]
    public void TallVariantPassesDimensions()
    {
        Assert.True(Check(Probe(width: 2048, height: 2732), "Dimensions"));
        Assert.False(Check(Probe(width: 1080, height: 1080), "Dimensions"));
    }

    [Theory]
    [InlineData("24000/1001", true)]
    [InlineData("25/1", true)]
    [InlineData("30", true)]
    [InlineData("60/1", false)]
    [InlineData("15/1", false)]
    public void ChecksFrameRate(string frameRate, bool expected)
    {
        Assert.Equal(expected, Check(Probe(frameRate: frameRate), "Frame rate"));
    }

    [Theory]
    [InlineData("7.9", false)]
    [InlineData("8", true)]
    [InlineData("35", true)]
    [InlineData("35.1", false)]
    public void ChecksDuration(string duration, bool expected)
    {
        Assert.Equal(expected, Check(Probe(duration: duration), "Duration"));
    }

    [Fact]
    public void FlagsAudioCodecAndBitRate()
    {
        Assert.False(Check(Probe(audio: true), "Audio"));
        Assert.False(Check(Probe(codec: "hevc"), "Video codec"));
        Assert.True(Check(Probe(codec: "prores"), "Video codec"));
        Assert.False(Check(Probe(bitRate: "8000000"), "Bit rate"));
    }

    [Fact]
    public void UnreportedValuesAreUnknownRatherThanFailures()
    {
        Assert.Null(Check(Probe(colorPrimaries: null), "Color primaries"));
        Assert.Null(Check(Probe(colorPrimaries: "unknown"), "Color primaries"));
        Assert.Null(Check(Probe(sampleAspectRatio: "0:1"), "Pixel aspect ratio"));
        Assert.Null(Check(Probe(frameRate: "0/0"), "Frame rate"));
        Assert.Null(Check(new ProbeResult(), "Dimensions"));
    }
}
