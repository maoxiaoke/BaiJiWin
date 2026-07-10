using BaiJi.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BaiJi.App.Services;

/// <summary>
/// Windows clipboard import/export — the platform half of macOS's
/// ClipboardImporter/Exporter. Import handles two cases like macOS:
/// (1) files copied in Explorer, and (2) raw bitmap data (screenshots), which is
/// written to a temp PNG so the CLI tools can operate on a real path.
/// </summary>
public sealed class WindowsClipboard
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "BaiJi");

    /// <summary>Returns a file path for the clipboard's image/video content, or null.</summary>
    public async Task<string?> ImportAsync()
    {
        var content = Clipboard.GetContent();

        // Case 1: files copied in Explorer.
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            var items = await content.GetStorageItemsAsync();
            var paths = items.OfType<StorageFile>().Select(f => f.Path);
            var chosen = ClipboardSupport.FirstSupportedFile(paths);
            if (chosen is not null) return chosen;
        }

        // Case 2: raw bitmap (screenshot / image copied from a browser).
        if (content.Contains(StandardDataFormats.Bitmap))
        {
            var bitmapRef = await content.GetBitmapAsync();
            using var stream = await bitmapRef.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixels = await decoder.GetPixelDataAsync();

            Directory.CreateDirectory(_tempDir);
            var destination = Path.Combine(_tempDir, $"pasted-image-{Guid.NewGuid():N}.png");
            using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write);
            using var outStream = fileStream.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outStream);
            encoder.SetPixelData(
                decoder.BitmapPixelFormat, BitmapAlphaMode.Premultiplied,
                decoder.PixelWidth, decoder.PixelHeight, decoder.DpiX, decoder.DpiY,
                pixels.DetachPixelData());
            await encoder.FlushAsync();
            return destination;
        }

        return null;
    }

    /// <summary>Copies a finished image file onto the clipboard as a bitmap.</summary>
    public bool CopyImage(string path)
    {
        try
        {
            var package = new DataPackage();
            var file = StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
            package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            // Also expose it as a file so paste targets that want a file get one.
            package.SetStorageItems(new[] { file });
            Clipboard.SetContent(package);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
