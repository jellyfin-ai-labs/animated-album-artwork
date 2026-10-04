using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AnimatedAlbumArt.Configuration;
using Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;
using Jellyfin.Plugin.AnimatedAlbumArt.Playback;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Tests;

public class PlaybackCopyCacheTests
{
    [Fact]
    public async Task PublishesAtomicallyDeduplicatesAndPinsOriginal()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var fixture = new Fixture(async (_, output, token) =>
        {
            Interlocked.Increment(ref calls);
            await File.WriteAllBytesAsync(output, [1, 2, 3], token);
            entered.SetResult();
            await release.Task.WaitAsync(token);
        });
        await fixture.Cache.StartAsync(default);
        Assert.Equal(fixture.Source, fixture.Cache.Select(fixture.Source));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(fixture.Source, fixture.Cache.Select(fixture.Source));
        var waiting = fixture.Cache.PrepareAsync(fixture.Source, default);
        release.SetResult();
        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        var copy = fixture.Cache.Select(fixture.Source)!;
        Assert.NotEqual(fixture.Source.Path, copy.Path);
        Assert.Equal(1, calls);
        Assert.Equal(fixture.Source, fixture.Cache.Select(fixture.Source, fixture.Source.Tag));
        Assert.Equal(copy, fixture.Cache.Select(fixture.Source, copy.Tag));
        Assert.Null(fixture.Cache.Select(fixture.Source, "obsolete"));
        Assert.Empty(Directory.GetFiles(fixture.Directory, "*.tmp.mp4"));
        await fixture.Cache.StopAsync(default);
    }

    [Fact]
    public async Task SourceReplacementInvalidatesCopyAndOldTags()
    {
        using var fixture = new Fixture((_, output, token) => File.WriteAllBytesAsync(output, [1, 2, 3], token));
        await fixture.Cache.StartAsync(default);
        Assert.True(await fixture.Cache.PrepareAsync(fixture.Source, default));
        var oldCopy = fixture.Cache.Select(fixture.Source)!;
        await File.WriteAllBytesAsync(fixture.Source.Path, [1, 2, 3, 4, 5]);
        var updated = fixture.ReadSource();
        Assert.Null(fixture.Cache.Select(updated, oldCopy.Tag));
        Assert.True(await fixture.Cache.PrepareAsync(updated, default));
        Assert.NotEqual(oldCopy.Path, fixture.Cache.Select(updated)!.Path);
        await fixture.Cache.StopAsync(default);
    }

    [Fact]
    public async Task ChangedSourceDuringConversionIsNotPublished()
    {
        using var fixture = new Fixture(async (source, output, token) =>
        {
            await File.WriteAllBytesAsync(output, [1, 2], token);
            await File.WriteAllBytesAsync(source.Path, [1, 2, 3, 4, 5], token);
        });
        await fixture.Cache.StartAsync(default);
        Assert.False(await fixture.Cache.PrepareAsync(fixture.Source, default));
        Assert.Empty(Directory.GetFiles(fixture.Directory));
        await fixture.Cache.StopAsync(default);
    }

    [Fact]
    public async Task FailuresBackOffAndRetainOriginal()
    {
        var calls = 0;
        using var fixture = new Fixture(async (_, output, token) =>
        {
            calls++;
            await File.WriteAllBytesAsync(output, [1], token);
            throw new IOException("invalid conversion");
        });
        await fixture.Cache.StartAsync(default);
        Assert.False(await fixture.Cache.PrepareAsync(fixture.Source, default));
        Assert.False(await fixture.Cache.PrepareAsync(fixture.Source, default));
        Assert.Equal(fixture.Source, fixture.Cache.Select(fixture.Source));
        Assert.Equal(1, calls);
        Assert.Empty(Directory.GetFiles(fixture.Directory));
        await fixture.Cache.StopAsync(default);
    }

    [Fact]
    public async Task ShutdownCancelsConversionAndRemovesTemporaryFile()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(async (_, output, token) =>
        {
            await File.WriteAllBytesAsync(output, [1], token);
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        await fixture.Cache.StartAsync(default);
        var waiting = fixture.Cache.PrepareAsync(fixture.Source, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Cache.StopAsync(default);
        Assert.False(await waiting);
        Assert.Empty(Directory.GetFiles(fixture.Directory));
    }

    [Fact]
    public async Task DisabledOptimizationDoesNotQueueAndIgnoresCachedCopy()
    {
        using var fixture = new Fixture((_, output, token) => File.WriteAllBytesAsync(output, [1], token));
        await fixture.Cache.StartAsync(default);
        Assert.True(await fixture.Cache.PrepareAsync(fixture.Source, default));
        fixture.Configuration.GeneratePlaybackCopies = false;
        Assert.False(await fixture.Cache.PrepareAsync(fixture.Source, default));
        Assert.Equal(fixture.Source, fixture.Cache.Select(fixture.Source));
        await fixture.Cache.StopAsync(default);
    }

    [Fact]
    public async Task EnforcesSizeLimitAndCleansExpiredAndInterruptedFiles()
    {
        using var fixture = new Fixture((_, output, token) => File.WriteAllBytesAsync(output, new byte[10 * 1024 * 1024], token));
        fixture.Configuration.PlaybackCacheMiB = 16;
        System.IO.Directory.CreateDirectory(fixture.Directory);
        await File.WriteAllTextAsync(Path.Combine(fixture.Directory, "interrupted.tmp.mp4"), "partial");
        var expired = Path.Combine(fixture.Directory, "expired.mp4");
        await File.WriteAllTextAsync(expired, "old");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-31));
        await fixture.Cache.StartAsync(default);
        Assert.True(await fixture.Cache.PrepareAsync(fixture.Source, default));
        var first = fixture.Cache.Select(fixture.Source)!;
        await File.WriteAllBytesAsync(fixture.Source.Path, [1, 2, 3, 4, 5]);
        Assert.True(await fixture.Cache.PrepareAsync(fixture.ReadSource(), default));
        Assert.False(File.Exists(first.Path));
        Assert.Single(Directory.GetFiles(fixture.Directory));
        Assert.True(Directory.GetFiles(fixture.Directory).Sum(path => new FileInfo(path).Length) <= 16 * 1024 * 1024);
        await fixture.Cache.StopAsync(default);
    }

    [Fact]
    public async Task ReadyCopySurvivesServiceRestart()
    {
        using var fixture = new Fixture((_, output, token) => File.WriteAllBytesAsync(output, [1], token));
        await fixture.Cache.StartAsync(default);
        Assert.True(await fixture.Cache.PrepareAsync(fixture.Source, default));
        var copy = fixture.Cache.Select(fixture.Source);
        await fixture.Cache.StopAsync(default);
        using var second = new PlaybackCopyCache(fixture.Directory, (_, _, _) => throw new InvalidOperationException("must not encode"), () => fixture.Configuration, NullLogger<PlaybackCopyCache>.Instance);
        await second.StartAsync(default);
        Assert.Equal(copy, second.Select(fixture.Source));
        await second.StopAsync(default);
    }

    [Fact]
    public async Task CancellingOneWaiterDoesNotCancelSharedConversion()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(async (_, output, token) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            await File.WriteAllBytesAsync(output, [1], token);
        });
        await fixture.Cache.StartAsync(default);
        using var cancellation = new CancellationTokenSource();
        var first = fixture.Cache.PrepareAsync(fixture.Source, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = fixture.Cache.PrepareAsync(fixture.Source, default);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult();
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(5)));
        await fixture.Cache.StopAsync(default);
    }

    [Fact]
    public async Task RejectsCopyLargerThanCacheBudget()
    {
        using var fixture = new Fixture((_, output, token) => File.WriteAllBytesAsync(output, new byte[17 * 1024 * 1024], token));
        fixture.Configuration.PlaybackCacheMiB = 16;
        await fixture.Cache.StartAsync(default);
        Assert.False(await fixture.Cache.PrepareAsync(fixture.Source, default));
        Assert.Equal(fixture.Source, fixture.Cache.Select(fixture.Source));
        Assert.Empty(Directory.GetFiles(fixture.Directory));
        await fixture.Cache.StopAsync(default);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "artwork-cache-test-" + Guid.NewGuid());

        public Fixture(Func<MotionArtFile, string, CancellationToken, Task> encode)
        {
            System.IO.Directory.CreateDirectory(_root);
            File.WriteAllBytes(Path.Combine(_root, "source.mp4"), [1, 2, 3, 4]);
            Source = ReadSource();
            Cache = new PlaybackCopyCache(Directory, encode, () => Configuration, NullLogger<PlaybackCopyCache>.Instance);
        }

        public string Directory => Path.Combine(_root, "cache");

        public PluginConfiguration Configuration { get; } = new();

        public PlaybackCopyCache Cache { get; }

        public MotionArtFile Source { get; }

        public MotionArtFile ReadSource()
        {
            var file = new FileInfo(Path.Combine(_root, "source.mp4"));
            return new MotionArtFile(file.FullName, "video/mp4", file.Length, file.LastWriteTimeUtc);
        }

        public void Dispose()
        {
            Cache.Dispose();
            System.IO.Directory.Delete(_root, recursive: true);
        }
    }
}
