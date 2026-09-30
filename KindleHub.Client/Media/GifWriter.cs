using System;
using System.Collections.Generic;
using System.IO;

namespace KindleHub.Client.Media;

/// <summary>Writes a 1-bit animated GIF (GIF89a) from a set of monochrome frames.
///
/// A KHFLIP1 drawing is a binary cell grid, so a two-entry global colour table
/// (black/white) is all that is needed. Frames are emitted with a per-frame
/// delay taken from the flipbook's fps, and the Netscape looping extension so
/// the result animates forever in whatever GIF viewer the OS opens it with.
/// </summary>
internal static class GifWriter
{
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="frames">One packed byte array per frame; 1 = black pixel, 0 = white.</param>
    /// <param name="delayMs">Delay per frame in milliseconds.</param>
    public static byte[] Build(int width, int height, IReadOnlyList<byte[]> frames, int delayMs)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (frames is null || frames.Count == 0) throw new ArgumentException("need at least one frame", nameof(frames));
        if (delayMs < 20) delayMs = 20;   // browsers clamp very short delays
        if (delayMs > 65535) delayMs = 65535;

        using var ms = new MemoryStream();
        void Str(string s) { var b = System.Text.Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
        void U16(int v) { ms.WriteByte((byte)(v & 0xFF)); ms.WriteByte((byte)((v >> 8) & 0xFF)); }

        Str("GIF89a");
        U16(width);
        U16(height);
        // 0xF0 = global colour table present (2^(0+1) = 2 entries), no sort, 1-bit per pixel.
        ms.WriteByte(0xF0);
        ms.WriteByte(0x00);                       // background colour index (white)
        ms.WriteByte(0x00);                       // pixel aspect ratio
        // Palette order must match the cell semantics: index 0 is an unset cell
        // (white paper), index 1 is an inked cell (black).
        ms.WriteByte(0xFF); ms.WriteByte(0xFF); ms.WriteByte(0xFF); // colour 0 = white
        ms.WriteByte(0x00); ms.WriteByte(0x00); ms.WriteByte(0x00); // colour 1 = black

        WriteNetscapeLoop(ms);

        foreach (var frame in frames)
        {
            // Graphic Control Extension: disposal 1 (do not dispose), delay, no transparency.
            ms.WriteByte(0x21);
            ms.WriteByte(0xF9);
            ms.WriteByte(0x04);
            ms.WriteByte(0x04);                    // packed: disposal method 1
            U16(delayMs);
            ms.WriteByte(0x00);                    // transparent colour index (unused)
            ms.WriteByte(0x00);

            // Image Descriptor
            ms.WriteByte(0x2C);
            U16(0); U16(0);                        // left, top
            U16(width); U16(height);
            ms.WriteByte(0x00);                    // no local colour table, not interlaced

            WriteLzw(ms, frame, width * height);
        }

        ms.WriteByte(0x3B);                        // trailer
        return ms.ToArray();
    }

    private static void WriteNetscapeLoop(Stream s)
    {
        s.WriteByte(0x21);        // extension
        s.WriteByte(0xFF);        // application extension
        s.WriteByte(0x0B);        // block size
        var app = System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0");
        s.Write(app, 0, app.Length);
        s.WriteByte(0x03);        // sub-block size
        s.WriteByte(0x01);        // loop sub-block id
        s.WriteByte(0x00);        // loop count low  (0 = forever)
        s.WriteByte(0x00);        // loop count high
        s.WriteByte(0x00);        // block terminator
    }

    /// <summary>Emits the frame's pixels as a GIF LZW sub-block stream.
    /// A 2-bit minimum code size matches the two-colour table.</summary>
    private static void WriteLzw(Stream outStream, byte[] pixels, int pixelCount)
    {
        const int minCodeSize = 2;
        outStream.WriteByte(minCodeSize);

        var bitWriter = new GifBitWriter(outStream);
        var clearCode = 1 << minCodeSize;         // 4
        var endCode = clearCode + 1;              // 5

        var dict = new Dictionary<int, int>(4096);
        int next = endCode + 1;
        int codeSize = minCodeSize + 1;           // 3
        bitWriter.Write(clearCode, codeSize);

        int prefix = -1;                          // -1 == no current run yet
        for (var i = 0; i < pixelCount; i++)
        {
            int k = pixels[i] != 0 ? 1 : 0;
            if (prefix < 0)
            {
                prefix = k;
                continue;
            }

            var key = (prefix << 8) | k;
            if (dict.TryGetValue(key, out var found))
            {
                prefix = found;
                continue;
            }

            bitWriter.Write(prefix, codeSize);
            if (next < 4096)
            {
                dict[key] = next++;
                if (next - 1 == (1 << codeSize) && codeSize < 12) codeSize++;
            }
            else
            {
                // Dictionary is full — reset so long frames still compress.
                bitWriter.Write(clearCode, codeSize);
                dict.Clear();
                next = endCode + 1;
                codeSize = minCodeSize + 1;
            }
            prefix = k;
        }

        if (prefix >= 0) bitWriter.Write(prefix, codeSize);
        bitWriter.Write(endCode, codeSize);
        bitWriter.Flush();
    }

    /// <summary>Packs codes LSB-first into GIF sub-blocks of at most 255 bytes.</summary>
    private sealed class GifBitWriter
    {
        private readonly Stream _out;
        private readonly List<byte> _pending = new(256);
        private int _bitBuffer;
        private int _bitCount;

        public GifBitWriter(Stream output) => _out = output;

        public void Write(int code, int bitCount)
        {
            _bitBuffer |= (code << _bitCount);
            _bitCount += bitCount;
            while (_bitCount >= 8)
            {
                _pending.Add((byte)(_bitBuffer & 0xFF));
                _bitBuffer >>= 8;
                _bitCount -= 8;
                if (_pending.Count == 255) Drain();
            }
        }

        public void Flush()
        {
            if (_bitCount > 0)
            {
                _pending.Add((byte)(_bitBuffer & 0xFF));
                _bitBuffer = 0;
                _bitCount = 0;
            }
            Drain();
            _out.WriteByte(0x00); // block terminator
        }

        private void Drain()
        {
            if (_pending.Count == 0) return;
            _out.WriteByte((byte)_pending.Count);
            _out.Write(_pending.ToArray(), 0, _pending.Count);
            _pending.Clear();
        }
    }
}
