namespace Jellyfin.Plugin.AnimatedAlbumArt.Diagnostics;

/// <summary>
/// One advisory check of a motion artwork file against a delivery profile.
/// </summary>
/// <param name="Name">What is checked.</param>
/// <param name="Passed"><c>true</c> or <c>false</c>, or <c>null</c> when the probe did not report the value.</param>
/// <param name="Expected">The profile's requirement.</param>
/// <param name="Actual">The value found in the file.</param>
public sealed record ProfileCheck(string Name, bool? Passed, string Expected, string? Actual);
