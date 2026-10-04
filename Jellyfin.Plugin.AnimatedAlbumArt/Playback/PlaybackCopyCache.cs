using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.AnimatedAlbumArt.Configuration;
using Jellyfin.Plugin.AnimatedAlbumArt.MotionArt;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Playback;

/// <summary>
/// Selects persistent playback copies and generates them through one bounded worker.
/// </summary>
public sealed class PlaybackCopyCache : BackgroundService
{
    private const string ProfileVersion = "h264-720-crf23-4m-v3";
    private const int QueueCapacity = 128;
    private readonly string _directory;
    private readonly Func<MotionArtFile, string, CancellationToken, Task> _encode;
    private readonly Func<PluginConfiguration> _configuration;
    private readonly ILogger<PlaybackCopyCache> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _failures = new(StringComparer.Ordinal);
    private readonly Channel<Job> _queue = Channel.CreateBounded<Job>(QueueCapacity);

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackCopyCache"/> class.
    /// </summary>
    /// <param name="paths">Jellyfin's application paths.</param>
    /// <param name="encoder">The playback copy encoder.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackCopyCache(IApplicationPaths paths, PlaybackCopyEncoder encoder, ILogger<PlaybackCopyCache> logger)
        : this(Path.Combine(paths.CachePath, "animated-album-art"), encoder.EncodeAsync, () => Plugin.Instance?.Configuration ?? new PluginConfiguration(), logger)
    {
    }

    internal PlaybackCopyCache(string directory, Func<MotionArtFile, string, CancellationToken, Task> encode, Func<PluginConfiguration> configuration, ILogger<PlaybackCopyCache> logger)
    {
        _directory = directory;
        _encode = encode;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Selects a ready copy or queues generation and returns the original.
    /// Supplied tags pin playback to a revision; stale tags return null.
    /// </summary>
    /// <param name="source">The current original.</param>
    /// <param name="tag">The requested playback revision.</param>
    /// <returns>The selected file, or null for a stale revision.</returns>
    public MotionArtFile? Select(MotionArtFile source, string? tag = null)
    {
        var enabled = _configuration().GeneratePlaybackCopies;
        var copy = enabled ? ReadCopy(Key(source)) : null;
        if (enabled && copy is null)
        {
            Queue(source);
        }

        return tag is null ? copy ?? source : tag == source.Tag ? source : copy?.Tag == tag ? copy : null;
    }

    /// <summary>
    /// Prepares a copy, sharing any queued conversion. Caller cancellation only stops waiting.
    /// </summary>
    /// <param name="source">The original artwork.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>Whether a copy is available.</returns>
    public async Task<bool> PrepareAsync(MotionArtFile source, CancellationToken cancellationToken)
    {
        while (_configuration().Enabled && _configuration().GeneratePlaybackCopies)
        {
            if (ReadCopy(Key(source)) is not null)
            {
                return true;
            }

            var job = Queue(source);
            if (job is not null)
            {
                return await job.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            lock (_gate)
            {
                if (_failures.ContainsKey(Key(source)))
                {
                    return false;
                }
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            foreach (var path in Directory.EnumerateFiles(_directory, "*.tmp.mp4"))
            {
                File.Delete(path);
            }

            foreach (var path in Directory.EnumerateDirectories(_directory, "*.tmp.mp4.parts"))
            {
                Directory.Delete(path, recursive: true);
            }

            Prune(0);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(error, "Could not clean the artwork playback cache");
        }

        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                var success = false;
                var temporary = Path.Combine(_directory, job.Key + ".tmp.mp4");
                try
                {
                    if (_configuration().Enabled && _configuration().GeneratePlaybackCopies)
                    {
                        Directory.CreateDirectory(_directory);
                        await _encode(job.Source, temporary, stoppingToken).ConfigureAwait(false);
                        var current = new FileInfo(job.Source.Path);
                        if (!current.Exists || current.Length != job.Source.Length || current.LastWriteTimeUtc != job.Source.LastModified.UtcDateTime)
                        {
                            throw new IOException("Original artwork changed during conversion.");
                        }

                        var size = new FileInfo(temporary).Length;
                        if (size <= 0 || size > Limit())
                        {
                            throw new IOException("Playback copy is empty or exceeds the cache limit.");
                        }

                        if (_configuration().Enabled && _configuration().GeneratePlaybackCopies)
                        {
                            Prune(size);
                            File.Move(temporary, Path.Combine(_directory, job.Key + ".mp4"), overwrite: true);
                            success = true;
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    // Optional conversion must never break original playback.
                    _logger.LogWarning(error, "Could not prepare artwork playback copy {Key}", job.Key);
                    lock (_gate)
                    {
                        if (_failures.Count >= QueueCapacity)
                        {
                            _failures.Remove(_failures.MinBy(entry => entry.Value).Key);
                        }

                        _failures[job.Key] = DateTimeOffset.UtcNow.AddMinutes(30);
                    }
                }
                finally
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        _logger.LogWarning(error, "Could not remove temporary artwork copy");
                    }

                    lock (_gate)
                    {
                        _jobs.Remove(job.Key);
                        job.Completion.TrySetResult(success);
                    }
                }
            }
        }
        finally
        {
            _queue.Writer.TryComplete();
            lock (_gate)
            {
                foreach (var job in _jobs.Values)
                {
                    job.Completion.TrySetResult(false);
                }

                _jobs.Clear();
            }
        }
    }

    private Job? Queue(MotionArtFile source)
    {
        var key = Key(source);
        lock (_gate)
        {
            if (_jobs.TryGetValue(key, out var existing))
            {
                return existing;
            }

            if (_failures.TryGetValue(key, out var retryAfter))
            {
                if (retryAfter > DateTimeOffset.UtcNow)
                {
                    return null;
                }

                _failures.Remove(key);
            }

            // A conversion may have finished after the caller checked the cache.
            if (ReadCopy(key) is not null)
            {
                return null;
            }

            var job = new Job(key, source);
            if (_jobs.Count >= QueueCapacity || !_queue.Writer.TryWrite(job))
            {
                return null;
            }

            _jobs.Add(key, job);
            return job;
        }
    }

    private MotionArtFile? ReadCopy(string key)
    {
        try
        {
            var file = new FileInfo(Path.Combine(_directory, key + ".mp4"));
            return file.Exists && file.Length > 0 ? new MotionArtFile(file.FullName, "video/mp4", file.Length, file.LastWriteTimeUtc) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Prune(long incomingSize)
    {
        var files = Directory.EnumerateFiles(_directory, "*.mp4")
            .Where(path => !path.EndsWith(".tmp.mp4", StringComparison.Ordinal))
            .Select(path => new FileInfo(path)).OrderBy(file => file.LastWriteTimeUtc).ToList();
        var total = files.Sum(file => file.Length) + incomingSize;
        foreach (var file in files)
        {
            if (total <= Limit() && file.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-30))
            {
                continue;
            }

            total -= file.Length;
            file.Delete();
        }
    }

    private long Limit() => Math.Clamp(_configuration().PlaybackCacheMiB, 16, 102400) * 1024L * 1024L;

    private static string Key(MotionArtFile source) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.Path + "\n" + source.Tag + "\n" + ProfileVersion)));

    private sealed record Job(string Key, MotionArtFile Source)
    {
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
