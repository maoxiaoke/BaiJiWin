using System.Text.RegularExpressions;

namespace BaiJi.Core;

public static class OutputNaming
{
    private static readonly Regex SuffixPattern =
        new(@"-(compressed|converted)( \d+)?$", RegexOptions.Compiled);

    /// <summary>
    /// "name-compressed.ext", appending " 2", " 3", … Finder-style on collision.
    /// Recompressing an output must not stack suffixes ("a-compressed-compressed"),
    /// so any existing suffix (and its collision counter) is stripped first.
    /// </summary>
    public static string AvailableOutputPath(string inputPath, string directory, string suffix, string ext)
    {
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        while (true)
        {
            var stripped = SuffixPattern.Replace(stem, "");
            if (stripped == stem) break;
            stem = stripped;
        }

        var baseName = $"{stem}-{suffix}";
        var candidate = Path.Combine(directory, $"{baseName}.{ext}");
        var counter = 1;
        while (File.Exists(candidate))
        {
            counter++;
            candidate = Path.Combine(directory, $"{baseName} {counter}.{ext}");
        }
        return candidate;
    }

    public static long FileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
