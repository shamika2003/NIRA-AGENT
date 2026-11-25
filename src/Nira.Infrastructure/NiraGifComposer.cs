using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nira.Infrastructure;

/// <summary>One generated sheet, four full frames. WPF quantizes each frame; the host writes a bounded GIF89a loop.</summary>
public static class NiraGifComposer
{
    public static string SpritePrompt(string prompt) => "Create a single precisely aligned 2 by 2 animation sprite sheet. " +
        "Four equally sized rectangular cells, no gutters, borders, text or labels. " +
        "Chronological order top-left, top-right, bottom-left, bottom-right. " +
        "Keep subject, scale, camera, framing, background and lighting consistent; each cell contains one complete frame. " +
        "Make the final frame transition naturally to the first. Requested scene and motion: " + prompt;

    public static byte[] Compose(BitmapSource sheet, int delayMs, CancellationToken ct)
    {
        if (sheet.PixelWidth is < 512 or > 1920 || sheet.PixelHeight is < 512 or > 1920 ||
            sheet.PixelWidth % 2 != 0 || sheet.PixelHeight % 2 != 0 || delayMs is < 100 or > 2000 || delayMs % 10 != 0)
            throw new ArgumentException("Invalid animation sheet or frame duration.");
        int width = sheet.PixelWidth / 2, height = sheet.PixelHeight / 2;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write("GIF89a"u8);
        writer.Write((ushort)width); writer.Write((ushort)height);
        writer.Write(new byte[] { 0x70, 0, 0 }); // No global table; each frame owns its palette.
        writer.Write(new byte[] { 0x21, 0xff, 11 });
        writer.Write("NETSCAPE2.0"u8);
        writer.Write(new byte[] { 3, 1, 0, 0, 0 }); // Infinite loop.
        for (int i = 0; i < 4; i++)
        {
            ct.ThrowIfCancellationRequested();
            var crop = new CroppedBitmap(sheet, new Int32Rect(i % 2 * width, i / 2 * height, width, height));
            var opaque = new FormatConvertedBitmap(crop, PixelFormats.Bgr24, null, 0);
            var encoder = new GifBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(opaque));
            using var frameStream = new MemoryStream();
            encoder.Save(frameStream);
            byte[] frame = frameStream.ToArray();
            WriteFrame(writer, frame, width, height, delayMs);
            if (output.Length > ImageGenerationTool.MaxBytes - 1) throw new IOException("GIF exceeds size limit.");
        }
        writer.Write((byte)0x3b); writer.Flush();
        return output.ToArray();
    }

    // Reuse the WPF encoder's palette and compressed image data, not its extension blocks.
    // Each crop is opaque and covers the complete logical screen, so no delta compositing is needed.
    private static void WriteFrame(BinaryWriter writer, byte[] source, int width, int height, int delayMs)
    {
        if (source.Length < 14 || !source.AsSpan(0, 3).SequenceEqual("GIF"u8)) throw new IOException("Invalid encoded frame.");
        int index = 13;
        byte globalFlags = source[10];
        byte[]? palette = (globalFlags & 0x80) != 0 ? TakePalette(source, ref index, globalFlags) : null;
        while (index < source.Length && source[index] == 0x21)
        {
            index += 2;
            SkipBlocks(source, ref index);
        }
        if (index + 10 > source.Length || source[index++] != 0x2c) throw new IOException("Missing GIF image descriptor.");
        int x = ReadU16(source, index), y = ReadU16(source, index + 2);
        int w = ReadU16(source, index + 4), h = ReadU16(source, index + 6);
        byte flags = source[index + 8]; index += 9;
        if (x != 0 || y != 0 || w != width || h != height) throw new IOException("Unexpected GIF frame dimensions.");
        byte paletteFlags = globalFlags;
        if ((flags & 0x80) != 0)
        { palette = TakePalette(source, ref index, flags); paletteFlags = flags; }
        if (palette is null || index >= source.Length) throw new IOException("Missing GIF frame palette or data.");
        int dataStart = index++;
        SkipBlocks(source, ref index);
        writer.Write(new byte[] { 0x21, 0xf9, 4, 4 }); // Full opaque frame, disposal=keep.
        writer.Write((ushort)(delayMs / 10)); writer.Write(new byte[] { 0, 0, 0x2c });
        writer.Write((ushort)0); writer.Write((ushort)0);
        writer.Write((ushort)width); writer.Write((ushort)height);
        writer.Write((byte)(0x80 | (flags & 0x40) | (paletteFlags & 7)));
        writer.Write(palette); writer.Write(source, dataStart, index - dataStart);
    }
    private static int ReadU16(byte[] bytes, int offset) => bytes[offset] | bytes[offset + 1] << 8;
    private static byte[] TakePalette(byte[] source, ref int index, byte flags)
    {
        int count = 3 * (1 << ((flags & 7) + 1));
        if (index + count > source.Length) throw new IOException("Invalid GIF palette.");
        byte[] table = source.AsSpan(index, count).ToArray(); index += count; return table;
    }
    private static void SkipBlocks(byte[] source, ref int index)
    {
        while (index < source.Length)
        {
            int size = source[index++];
            if (size == 0) return;
            if (index + size > source.Length) break;
            index += size;
        }
        throw new IOException("Truncated GIF frame data.");
    }
}
