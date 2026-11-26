using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace X360GameHack.UI
{
    /// <summary>
    /// X360GameHack's own mark: a dark tile, a green ring that stops just short of a full turn (the "360") and two code
    /// brackets "&gt; &lt;" that meet as an X.
    /// Drawn in code so it is sharp at every size; the same drawing makes the program icon.
    /// </summary>
    public static class AppLogo
    {
        public static readonly Color TileTop = Color.FromArgb(38, 46, 42), TileBottom = Color.FromArgb(10, 13, 12),
                                     RingLight = Color.FromArgb(120, 240, 110), RingDark = Color.FromArgb(22, 170, 70);

        /// <summary>The mark in its own colours: dark tile, green ring, white brackets.</summary>
        public static void Draw(Graphics g, RectangleF r) => Draw(g, r, TileTop, TileBottom, RingLight, RingDark, Color.White);

        public static void Draw(Graphics g, RectangleF r, Color tileTop, Color tileBottom, Color ringLight, Color ringDark, Color mark)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = Math.Min(r.Width, r.Height);
            bool small = s < 28;
            using (var tile = Rounded(r, s * 0.23f))
            {
                using (var fill = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), tileTop, tileBottom, 90f)) g.FillPath(fill, tile);
                using (var edge = new Pen(Color.FromArgb(small ? 150 : 110, ringDark), Math.Max(1f, s * 0.018f))) g.DrawPath(edge, tile);
            }
            Func<float, float, PointF> p = (x, y) => new PointF(r.X + x * r.Width, r.Y + y * r.Height);

            // the ring: almost a full turn (the "360"), open at the top right, ending in a dot
            float ringWidth = Math.Max(1.5f, s * (small ? 0.105f : 0.085f)), radius = s * (small ? 0.345f : 0.335f);
            var circle = new RectangleF(r.X + r.Width / 2 - radius, r.Y + r.Height / 2 - radius, radius * 2, radius * 2);
            using (var brush = new LinearGradientBrush(new RectangleF(circle.X - ringWidth, circle.Y - ringWidth, circle.Width + 2 * ringWidth, circle.Height + 2 * ringWidth), ringLight, ringDark, 60f))
            using (var pen = new Pen(brush, ringWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(pen, circle, -20f, 300f);
            if (!small)
            {
                double angle = -50 * Math.PI / 180;
                float dot = ringWidth * 0.78f;
                var c = new PointF(r.X + r.Width / 2 + radius * (float)Math.Cos(angle), r.Y + r.Height / 2 + radius * (float)Math.Sin(angle));
                using (var b = new SolidBrush(ringLight)) g.FillEllipse(b, c.X - dot, c.Y - dot, dot * 2, dot * 2);
            }

            // the two brackets "> <" that meet as an X
            float width = Math.Max(1.5f, s * (small ? 0.105f : 0.082f));
            float x0 = small ? 0.345f : 0.355f, x1 = small ? 0.455f : 0.452f, y0 = small ? 0.355f : 0.365f;
            using (var pen = new Pen(mark, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, new[] { p(x0, y0), p(x1, 0.50f), p(x0, 1 - y0) });
                g.DrawLines(pen, new[] { p(1 - x0, y0), p(1 - x1, 0.50f), p(1 - x0, 1 - y0) });
            }
            g.Restore(state);
        }
        private static PointF Offset(PointF point, float d) => new PointF(point.X, point.Y + d);

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Bitmap Render(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                float inset = size >= 48 ? size * 0.04f : 0f;
                Draw(g, new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset));
            }
            return bmp;
        }

        /// <summary>Writes the mark as a Windows icon holding 16, 20, 24, 32, 40, 48, 64 and 256 pixel images.</summary>
        public static void SaveIcon(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var images = new List<byte[]>();
            foreach (int size in sizes)
                using (var bmp = Render(size))
                {
                    if (size == 256) { using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); images.Add(ms.ToArray()); } continue; }
                    // classic entry: header, 32-bit pixels bottom-up, then the 1-bit mask
                    int maskRow = ((size + 31) / 32) * 4;
                    using (var ms = new MemoryStream())
                    using (var w = new BinaryWriter(ms))
                    {
                        w.Write(40); w.Write(size); w.Write(size * 2); w.Write((short)1); w.Write((short)32); w.Write(0);
                        w.Write(size * size * 4 + maskRow * size); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                        for (int y = size - 1; y >= 0; y--)
                            for (int x = 0; x < size; x++) { var c = bmp.GetPixel(x, y); w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A); }
                        for (int y = size - 1; y >= 0; y--)
                        {
                            var row = new byte[maskRow];
                            for (int x = 0; x < size; x++) if (bmp.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                            w.Write(row);
                        }
                        w.Flush();
                        images.Add(ms.ToArray());
                    }
                }
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32); w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var image in images) w.Write(image);
            }
        }
    }
}
