using OpenCvSharp;

namespace PixelTrace.Core.Imaging;

/// <summary>Owns the encoded-orientation BGR raster. Rotate only for viewing/export, keeping JPEG's 8px grid intact.</summary>
public sealed class ImageDocument : IDisposable
{
    public string Path { get; }
    public ImageHeader Header { get; }
    public Mat Pixels { get; }
    public string DecodeNote { get; }
    public int Width => Header.Orientation >= 5 ? Pixels.Height : Pixels.Width;
    public int Height => Header.Orientation >= 5 ? Pixels.Width : Pixels.Height;

    private ImageDocument(string path, ImageHeader header, Mat pixels, string note)
        => (Path, Header, Pixels, DecodeNote) = (path, header, pixels, note);

    public static ImageDocument Load(string path, CancellationToken token = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Image file was not found.", path);
        if (info.Length == 0) throw new InvalidDataException("The image file is empty.");
        if (info.Length > 256L * 1024 * 1024) throw new InvalidDataException("The encoded file exceeds the 256 MB safety limit.");
        token.ThrowIfCancellationRequested();
        byte[] bytes = File.ReadAllBytes(path);
        var header = ImageHeader.Read(bytes);
        if (header.Format == "TIFF" && header.Orientation != 1) ImageHeader.NeutralizeTiffOrientation(bytes);
        token.ThrowIfCancellationRequested();
        try
        {
            using var decoded = Cv2.ImDecode(bytes, ImreadModes.Unchanged);
            if (decoded.Empty()) throw new InvalidDataException("The image could not be decoded. It may be damaged or unsupported by OpenCV.");
            if (decoded.Width != header.Width || decoded.Height != header.Height)
                throw new InvalidDataException("Decoded dimensions do not match the image header.");
            using var eight = new Mat();
            string note = header.Format == "TIFF" ? "First TIFF page. " : "";
            if (decoded.Depth() == MatType.CV_16U) { decoded.ConvertTo(eight, MatType.CV_8U, 1.0 / 257); note += "16-bit input mapped to 8-bit for analysis. "; }
            else if (decoded.Depth() == MatType.CV_8U) decoded.CopyTo(eight);
            else throw new InvalidDataException("Only 8-bit and 16-bit integer image data are supported.");
            var bgr = new Mat();
            try
            {
                if (eight.Channels() == 1) Cv2.CvtColor(eight, bgr, ColorConversionCodes.GRAY2BGR);
                else if (eight.Channels() == 3) eight.CopyTo(bgr);
                else if (eight.Channels() == 4) { CompositeAlpha(eight, bgr, token); note += "Transparency composited on neutral gray. "; }
                else throw new InvalidDataException("Unsupported image channel layout.");
                token.ThrowIfCancellationRequested();
                return new(System.IO.Path.GetFullPath(path), header, bgr, note.Trim());
            }
            catch { bgr.Dispose(); throw; }
        }
        catch (OpenCVException e) { throw new InvalidDataException("OpenCV could not decode this image: " + e.Message, e); }
    }

    private static unsafe void CompositeAlpha(Mat source, Mat target, CancellationToken token)
    {
        int rows = source.Rows, cols = source.Cols;
        target.Create(rows, cols, MatType.CV_8UC3);
        for (int y = 0; y < rows; y++)
        {
            token.ThrowIfCancellationRequested();
            byte* s = (byte*)source.Ptr(y), t = (byte*)target.Ptr(y);
            for (int x = 0; x < cols; x++)
                for (int c = 0; c < 3; c++) t[x * 3 + c] = (byte)((s[x * 4 + c] * s[x * 4 + 3] + 127 * (255 - s[x * 4 + 3]) + 127) / 255);
        }
    }

    public static Mat Orient(Mat source, int orientation)
    {
        var result = new Mat();
        switch (orientation)
        {
            case 2: Cv2.Flip(source, result, FlipMode.Y); break;
            case 3: Cv2.Rotate(source, result, RotateFlags.Rotate180); break;
            case 4: Cv2.Flip(source, result, FlipMode.X); break;
            case 5: Cv2.Transpose(source, result); break;
            case 6: Cv2.Rotate(source, result, RotateFlags.Rotate90Clockwise); break;
            case 7: Cv2.Transpose(source, result); Cv2.Flip(result, result, FlipMode.XY); break;
            case 8: Cv2.Rotate(source, result, RotateFlags.Rotate90Counterclockwise); break;
            default: source.CopyTo(result); break;
        }
        return result;
    }

    public void Dispose() => Pixels.Dispose();
}
