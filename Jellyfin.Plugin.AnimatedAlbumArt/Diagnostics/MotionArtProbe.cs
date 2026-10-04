using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Plugin.AnimatedAlbumArt.Diagnostics;

/// <summary>
/// Runs the server's ffprobe against a motion artwork file.
/// </summary>
public class MotionArtProbe
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private readonly IMediaEncoder _mediaEncoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="MotionArtProbe"/> class.
    /// </summary>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    public MotionArtProbe(IMediaEncoder mediaEncoder)
    {
        _mediaEncoder = mediaEncoder;
    }

    /// <summary>
    /// Probes a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed probe output.</returns>
    /// <exception cref="InvalidOperationException">ffprobe is unavailable or failed.</exception>
    public async Task<ProbeResult> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var probePath = _mediaEncoder.ProbePath;
        if (string.IsNullOrEmpty(probePath))
        {
            throw new InvalidOperationException("ffprobe is not configured on this server.");
        }

        var startInfo = new ProcessStartInfo(probePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("ffprobe could not be started.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("ffprobe failed: " + (await stderr.ConfigureAwait(false)).Trim());
            }

            return JsonSerializer.Deserialize<ProbeResult>(await stdout.ConfigureAwait(false))
                ?? throw new InvalidOperationException("ffprobe returned no output.");
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
