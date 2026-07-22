using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PokeTokenBar.App.Platform;

internal static class SpriteBitmap
{
    public static BitmapSource Decode(byte[] bytes, bool cropTransparent = false)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];
        if (!cropTransparent)
        {
            source.Freeze();
            return source;
        }

        BitmapSource pixels = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = pixels.PixelWidth * 4;
        var buffer = new byte[stride * pixels.PixelHeight];
        pixels.CopyPixels(buffer, stride, 0);

        var left = pixels.PixelWidth;
        var top = pixels.PixelHeight;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < pixels.PixelHeight; y++)
        {
            for (var x = 0; x < pixels.PixelWidth; x++)
            {
                if (buffer[(y * stride) + (x * 4) + 3] == 0)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        BitmapSource cropped = right >= left && bottom >= top
            ? new CroppedBitmap(pixels, new Int32Rect(
                left,
                top,
                right - left + 1,
                bottom - top + 1))
            : pixels;
        return ToBitmapImage(cropped);
    }

    private static BitmapImage ToBitmapImage(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        output.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = output;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
