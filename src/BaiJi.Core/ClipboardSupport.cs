namespace BaiJi.Core;

/// <summary>
/// The platform-agnostic part of clipboard import: choosing which of the file
/// paths on the clipboard the pipeline should consume. The actual pasteboard
/// reading (file drops vs. raw screenshot bitmaps) lives in the WinUI layer.
/// </summary>
public static class ClipboardSupport
{
    /// <summary>
    /// Returns the first path whose type is a supported image/video, mirroring
    /// the macOS importer's "first URL conforming to image/video/movie" rule.
    /// </summary>
    public static string? FirstSupportedFile(IEnumerable<string> paths) =>
        paths.FirstOrDefault(p => MediaTask.KindOf(p) is MediaKind.Image or MediaKind.Video);
}
