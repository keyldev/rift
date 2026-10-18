using System.Buffers.Binary;

namespace Rift.Core;

/// <summary>Reads an image's pixel size from its header, without decoding the picture.</summary>
public static class ImageInfo
{
    /// <returns>Width and height, or null for formats it doesn't know or broken files.</returns>
    public static (int Width, int Height)? TryGetSize(Stream stream)
    {
        Span<byte> head = stackalloc byte[32];
        if (stream.ReadAtLeast(head, 26, throwOnEndOfStream: false) < 26)
            return null;

        // PNG: signature, then the IHDR chunk with big-endian width and height.
        if (head[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
            return (BinaryPrimitives.ReadInt32BigEndian(head[16..]), BinaryPrimitives.ReadInt32BigEndian(head[20..]));

        // GIF: little-endian 16-bit logical screen size.
        if (head[0] == 'G' && head[1] == 'I' && head[2] == 'F')
            return (BinaryPrimitives.ReadUInt16LittleEndian(head[6..]), BinaryPrimitives.ReadUInt16LittleEndian(head[8..]));

        // BMP: BITMAPINFOHEADER after the 14-byte file header; height is negative for top-down bitmaps.
        if (head[0] == 'B' && head[1] == 'M')
            return (BinaryPrimitives.ReadInt32LittleEndian(head[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(head[22..])));

        // JPEG: walk the segments to the first start-of-frame marker.
        if (head[0] == 0xFF && head[1] == 0xD8)
        {
            stream.Position = 2;
            return JpegSize(stream);
        }
        return null;
    }

    private static (int, int)? JpegSize(Stream stream)
    {
        Span<byte> segment = stackalloc byte[7];
        Span<byte> len = stackalloc byte[2];
        while (true)
        {
            int b;
            do
            {
                b = stream.ReadByte();
            } while (b is not (0xFF or -1));
            int marker;
            do
            {
                marker = stream.ReadByte();
            } while (marker == 0xFF);
            if (b < 0 || marker < 0)
                return null;
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
                continue; // markers without a length

            if (stream.ReadAtLeast(len, 2, throwOnEndOfStream: false) < 2)
                return null;
            var length = BinaryPrimitives.ReadUInt16BigEndian(len);
            // SOF0..SOF15, except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (stream.ReadAtLeast(segment[..5], 5, throwOnEndOfStream: false) < 5)
                    return null;
                return (BinaryPrimitives.ReadUInt16BigEndian(segment[3..]), BinaryPrimitives.ReadUInt16BigEndian(segment[1..]));
            }
            if (length < 2)
                return null;
            stream.Seek(length - 2, SeekOrigin.Current);
        }
    }
}
