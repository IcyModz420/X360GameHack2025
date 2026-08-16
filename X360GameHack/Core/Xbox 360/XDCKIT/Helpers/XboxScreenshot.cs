// =============================================================================
// XDCKIT.XboxScreenshot.cs - Detile + PNG encode of xbdm `screenshot` payloads
// =============================================================================
// xbdm's `screenshot` returns the front-buffer bytes plus a metadata line
// (pitch / width / height / format / sw / sh / framebuffersize / ...).
// Xenon stores 2D render targets in **AddrLib / "XG"** macro tiling, NOT raster
// order, so a literal linear copy produces scrambled / striped output.
//
// This helper provides the inverse mapping (port of Xenia's
// `xe::gpu::texture_address::Tiled2D` + `TiledCombine` for 4-byte texels) and
// a one-call PNG saver that decodes 32 bpp ARGB front buffers as
// `D3DFMT_LE_X8R8G8B8` (BGRA in memory) - the same layout xbdm captures.
//
// Lives in the global namespace alongside the rest of XDCKIT.
// =============================================================================
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
/// Pure functions for decoding xbdm <c>screenshot</c> payloads (detile + PNG
/// encode). All methods are static and side-effect free except those that
/// take an explicit output path.
/// </summary>
public static class XboxScreenshot
{
    /// <summary>
    /// Decode a tiled xbdm framebuffer and save it as PNG.  Returns
    /// <c>false</c> when geometry doesn't look like a 32 bpp ARGB surface
    /// (caller should fall back to writing the raw bytes).
    /// </summary>
    /// <param name="info">Metadata parsed from the screenshot response.</param>
    /// <param name="frame">Raw tiled pixel bytes from xbdm.</param>
    /// <param name="outputPath">Destination <c>.png</c> path.</param>
    public static bool TryEncodePng(ScreenshotInfo info, byte[] frame, string outputPath)
    {
        if (frame == null || frame.Length == 0) return false;
        if (info.Width == 0 || info.Height == 0) return false;
        if (string.IsNullOrEmpty(outputPath))
            throw new ArgumentException("Output path required.", nameof(outputPath));

        int srcW  = (int)info.Width;
        int srcH  = (int)info.Height;
        int pitch = (int)(info.Pitch > 0 ? info.Pitch : info.Width * 4u);
        if ((pitch & 3) != 0) return false;
        if (pitch < srcW * 4) return false;

        byte[] linear = Detile32bpp(frame, srcW, srcH, pitch);
        if (linear == null) return false;

        using (var bmp = new Bitmap(srcW, srcH, PixelFormat.Format32bppArgb))
        {
            var rect = new Rectangle(0, 0, srcW, srcH);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                var row = new byte[stride];
                for (int y = 0; y < srcH; y++)
                {
                    Buffer.BlockCopy(linear, y * srcW * 4, row, 0, srcW * 4);
                    // X8 byte is undefined for front buffers - force opaque.
                    for (int x = 0; x < srcW; x++) row[x * 4 + 3] = 0xFF;
                    Marshal.Copy(row, 0, data.Scan0 + y * stride, stride);
                }
            }
            finally { bmp.UnlockBits(data); }

            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            bmp.Save(outputPath, ImageFormat.Png);
        }
        return true;
    }

    /// <summary>
    /// Detile a 32 bpp Xenon framebuffer into a flat row-major BGRA byte
    /// buffer (<c>width * height * 4</c> bytes, suitable for direct copy
    /// into <see cref="PixelFormat.Format32bppArgb"/>).
    /// </summary>
    /// <param name="tiled">Tiled pixel bytes (the xbdm payload).</param>
    /// <param name="width">Logical output width in texels.</param>
    /// <param name="height">Logical output height in texels.</param>
    /// <param name="pitchBytes">Storage pitch reported by xbdm, in bytes.</param>
    /// <returns>BGRA bytes, or <c>null</c> if a sample would read outside <paramref name="tiled"/>.</returns>
    public static byte[] Detile32bpp(byte[] tiled, int width, int height, int pitchBytes)
    {
        if (tiled == null || width <= 0 || height <= 0) return null;
        int pitchPixels  = pitchBytes / 4;
        int pitchAligned = (pitchPixels + 31) & ~31;
        if (pitchAligned <= 0) return null;

        byte[] linear = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int srcByte = Tiled2DByteOffset32bpp(x, y, pitchAligned);
                if (srcByte < 0 || srcByte + 4 > tiled.Length) return null;

                int dst = (y * width + x) * 4;
                linear[dst + 0] = tiled[srcByte + 0];
                linear[dst + 1] = tiled[srcByte + 1];
                linear[dst + 2] = tiled[srcByte + 2];
                linear[dst + 3] = tiled[srcByte + 3];
            }
        }
        return linear;
    }

    /// <summary>
    /// Port of <c>xe::gpu::texture_address::Tiled2D</c> + <c>TiledCombine</c>
    /// for 4-byte texels.  Maps linear (x, y) to a guest **byte** offset
    /// inside a 2D AddrLib-style tiled 32 bpp surface.
    /// </summary>
    /// <param name="x">Texel column (0-based).</param>
    /// <param name="y">Texel row (0-based).</param>
    /// <param name="pitchAlignedPixels">Storage pitch in texels, rounded up to 32.</param>
    public static int Tiled2DByteOffset32bpp(int x, int y, int pitchAlignedPixels)
    {
        const int kMacroTileWidthLog2  = 5;
        const int kMacroTileHeightLog2 = 5;
        const int bytesPerBlockLog2    = 2;

        int outerBlocks =
            (((y >> kMacroTileHeightLog2) * (pitchAlignedPixels >> kMacroTileWidthLog2))
             + (x >> kMacroTileWidthLog2)) << 6;

        int innerBlocks      = (((y >> 1) & 7) << 3) | (x & 7);
        int outerInnerBytes  = (outerBlocks | innerBlocks) << bytesPerBlockLog2;

        int bank = (y >> 4) & 1;
        int pipe = ((x >> 3) & 3) ^ (((y >> 3) & 1) << 1);
        int yLsb = y & 1;

        return ((yLsb & 1) << 4)
             | ((pipe & 3) << 6)
             | ((bank & 1) << 11)
             | (outerInnerBytes & 0xF)
             | (((outerInnerBytes >> 4) & 1) << 5)
             | (((outerInnerBytes >> 5) & 7) << 8)
             | ((outerInnerBytes >> 8) << 12);
    }
}
