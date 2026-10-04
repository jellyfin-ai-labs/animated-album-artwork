using Jellyfin.Plugin.AnimatedAlbumArt.Web;
using Xunit;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Tests;

public class ScriptInjectorTests
{
    [Fact]
    public void InsertsBeforeClosingBody()
    {
        Assert.Equal(
            "<html><body><div></div>" + ScriptInjector.ScriptTag + "</body></html>",
            ScriptInjector.Inject("<html><body><div></div></body></html>"));
    }

    [Fact]
    public void InjectsOnlyOnce()
    {
        var once = ScriptInjector.Inject("<body></body>");
        Assert.Equal(once, ScriptInjector.Inject(once));
    }

    [Fact]
    public void LeavesPagesWithoutBodyUnchanged()
    {
        Assert.Equal("<p>fragment</p>", ScriptInjector.Inject("<p>fragment</p>"));
    }

    [Theory]
    [InlineData("/web/", true)]
    [InlineData("/web/index.html", true)]
    [InlineData("/jellyfin/web/index.html", true)]
    [InlineData("/WEB/INDEX.HTML", true)]
    [InlineData("/web/main.js", false)]
    [InlineData("/web/configurationpage", false)]
    [InlineData("/", false)]
    [InlineData(null, false)]
    public void RecognizesIndexPaths(string? path, bool expected)
    {
        Assert.Equal(expected, ScriptInjector.IsIndexPath(path));
    }
}
