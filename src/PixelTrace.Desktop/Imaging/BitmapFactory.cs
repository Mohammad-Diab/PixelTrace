using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenCvSharp;

namespace PixelTrace.Desktop.Imaging;

internal static class BitmapFactory
{
    public static WriteableBitmap Create(Mat bgr)
    {
        var bitmap = new WriteableBitmap(new PixelSize(bgr.Width, bgr.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        try
        {
            using var buffer = bitmap.Lock();
            using var pixels = Mat.FromPixelData(bgr.Height, bgr.Width, MatType.CV_8UC4, buffer.Address, buffer.RowBytes);
            Cv2.CvtColor(bgr, pixels, ColorConversionCodes.BGR2BGRA);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
