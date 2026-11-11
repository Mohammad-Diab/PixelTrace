using System.Buffers.Binary;

namespace PixelTrace.Core.Imaging;

public sealed record ImageHeader(string Format, int Width, int Height, int Orientation = 1, int? JpegQuality = null)
{
    public const long MaximumPixels = 40_000_000;

    // Inspect dimensions before asking a native decoder to allocate a raster.
    public static ImageHeader Read(byte[] data)
    {
        try
        {
            var d = data.AsSpan();
            ImageHeader h;
            if (d.Length >= 24 && d[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                h = new("PNG", checked((int)BinaryPrimitives.ReadUInt32BigEndian(d[16..])), checked((int)BinaryPrimitives.ReadUInt32BigEndian(d[20..])));
            else if (d.Length >= 4 && d[0] == 255 && d[1] == 216)
                h = ReadJpeg(d);
            else if (d.Length >= 26 && d[0] == 'B' && d[1] == 'M')
            {
                var dib = BinaryPrimitives.ReadUInt32LittleEndian(d[14..]);
                h = dib == 12
                    ? new("BMP", BinaryPrimitives.ReadUInt16LittleEndian(d[18..]), BinaryPrimitives.ReadUInt16LittleEndian(d[20..]))
                    : new("BMP", BinaryPrimitives.ReadInt32LittleEndian(d[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(d[22..])));
            }
            else if (d.Length >= 8 && (d[..4].SequenceEqual("II*\0"u8) || d[..4].SequenceEqual("MM\0*"u8)))
                h = ReadTiff(d);
            else if (d.Length >= 30 && d[..4].SequenceEqual("RIFF"u8) && d.Slice(8, 4).SequenceEqual("WEBP"u8))
                h = ReadWebP(d);
            else throw new InvalidDataException("Unsupported image. Choose a JPEG, PNG, BMP, TIFF or WebP file.");
            if (h.Width < 1 || h.Height < 1) throw new InvalidDataException("The image has invalid dimensions.");
            if ((long)h.Width * h.Height > MaximumPixels || Math.Max(h.Width, h.Height) > 32000)
                throw new InvalidDataException("This image exceeds the 40 megapixel / 32,000 pixel-side safety limit. Open a smaller copy.");
            return h;
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("The image header is damaged or incomplete.", e);
        }
    }

    private static ImageHeader ReadJpeg(ReadOnlySpan<byte> d)
    {
        int width = 0, height = 0, orientation = 1;
        int? quality = null;
        for (int p = 2; p + 3 < d.Length;)
        {
            if (d[p++] != 255) continue;
            while (p < d.Length && d[p] == 255) p++;
            if (p >= d.Length) break;
            int marker = d[p++];
            if (marker is 0xD9 or 0xDA) break;
            if (marker is 0x00 or 0x01 || marker is >= 0xD0 and <= 0xD8) continue;
            int length = BinaryPrimitives.ReadUInt16BigEndian(d[p..]);
            if (length < 2 || p + length > d.Length) throw new InvalidDataException("Truncated JPEG metadata.");
            var segment = d.Slice(p + 2, length - 2);
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
            }
            if (marker == 0xE1 && segment.StartsWith("Exif\0\0"u8))
            {
                try { orientation = ReadTiff(segment[6..]).Orientation; }
                catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or OverflowException) { /* optional metadata */ }
            }
            if (marker == 0xDB && segment.Length >= 65 && segment[0] == 0)
                quality = EstimateQuality(segment.Slice(1, 64));
            p += length;
        }
        return new("JPEG", width, height, orientation, quality);
    }

    private static ImageHeader ReadTiff(ReadOnlySpan<byte> d)
    {
        bool le = d[0] == 'I';
        int offset = checked((int)U32(d, 4, le));
        int entries = U16(d, offset, le), width = 0, height = 0, orientation = 1;
        for (int i = 0; i < entries; i++)
        {
            int p = checked(offset + 2 + i * 12);
            int tag = U16(d, p, le), type = U16(d, p + 2, le);
            if (tag is not (256 or 257 or 274) || U32(d, p + 4, le) != 1) continue;
            int value = type == 3 ? U16(d, p + 8, le) : type == 4 ? checked((int)U32(d, p + 8, le)) : 0;
            if (tag == 256) width = value;
            if (tag == 257) height = value;
            if (tag == 274 && value is >= 1 and <= 8) orientation = value;
        }
        return new("TIFF", width, height, orientation);
    }

    private static int U16(ReadOnlySpan<byte> d, int p, bool le) => le ? BinaryPrimitives.ReadUInt16LittleEndian(d[p..]) : BinaryPrimitives.ReadUInt16BigEndian(d[p..]);
    private static uint U32(ReadOnlySpan<byte> d, int p, bool le) => le ? BinaryPrimitives.ReadUInt32LittleEndian(d[p..]) : BinaryPrimitives.ReadUInt32BigEndian(d[p..]);

    internal static void NeutralizeTiffOrientation(byte[] encoded)
    {
        // libtiff applies its orientation even with OpenCV's Unchanged flag. Reset only the in-memory
        // IFD tag so all formats share the encoded-raster convention; retain the real tag in Header.
        var d = encoded.AsSpan(); bool le = d[0] == 'I';
        int offset = checked((int)U32(d, 4, le)), count = U16(d, offset, le);
        for (int i = 0; i < count; i++)
        {
            int p = offset + 2 + 12 * i;
            if (U16(d, p, le) != 274 || U32(d, p + 4, le) != 1) continue;
            int type = U16(d, p + 2, le);
            if (type == 3)
            {
                if (le) BinaryPrimitives.WriteUInt16LittleEndian(d[(p + 8)..], 1);
                else BinaryPrimitives.WriteUInt16BigEndian(d[(p + 8)..], 1);
            }
            else if (type == 4)
            {
                if (le) BinaryPrimitives.WriteUInt32LittleEndian(d[(p + 8)..], 1);
                else BinaryPrimitives.WriteUInt32BigEndian(d[(p + 8)..], 1);
            }
        }
    }

    private static ImageHeader ReadWebP(ReadOnlySpan<byte> d)
    {
        int p = 12;
        while (p + 8 <= d.Length)
        {
            int len = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(d[(p + 4)..]));
            var id = d.Slice(p, 4);
            var body = d.Slice(p + 8, len);
            if (id.SequenceEqual("VP8X"u8)) return new("WebP", 1 + U24(body[4..]), 1 + U24(body[7..]));
            if (id.SequenceEqual("VP8 "u8) && body.Length >= 10)
                return new("WebP", BinaryPrimitives.ReadUInt16LittleEndian(body[6..]) & 0x3fff, BinaryPrimitives.ReadUInt16LittleEndian(body[8..]) & 0x3fff);
            if (id.SequenceEqual("VP8L"u8) && body.Length >= 5)
            {
                uint bits = BinaryPrimitives.ReadUInt32LittleEndian(body[1..]);
                return new("WebP", (int)(bits & 0x3fff) + 1, (int)((bits >> 14) & 0x3fff) + 1);
            }
            p = checked(p + 8 + len + (len & 1));
        }
        throw new InvalidDataException("No decodable WebP frame was found.");
    }

    private static int U24(ReadOnlySpan<byte> d) => d[0] | d[1] << 8 | d[2] << 16;

    private static int? EstimateQuality(ReadOnlySpan<byte> table)
    {
        // IJG luminance table in JPEG zigzag order. Nonstandard tables are left unlabelled.
        int[] standard = [16,11,12,14,12,10,16,14,13,14,18,17,16,19,24,40,26,24,22,22,24,49,35,37,29,40,58,51,61,60,57,51,56,55,64,72,92,78,64,68,87,69,55,56,80,109,81,87,95,98,103,104,103,62,77,113,121,112,100,120,92,101,103,99];
        double best = double.MaxValue; int result = 0;
        for (int q = 1; q <= 100; q++)
        {
            int scale = q < 50 ? 5000 / q : 200 - 2 * q;
            double error = 0;
            for (int i = 0; i < 64; i++) error += Math.Abs(table[i] - Math.Clamp((standard[i] * scale + 50) / 100, 1, 255));
            if (error < best) { best = error; result = q; }
        }
        return best / 64 <= 3 ? result : null;
    }
}
