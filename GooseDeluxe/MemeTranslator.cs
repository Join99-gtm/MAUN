using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace GooseDeluxe
{
    /// <summary>
    /// Russian versions of the memes that come with the goose. Six of its eight memes have English words
    /// drawn into the picture. For each of those we know where the words are: their letters are wiped out
    /// (every letter pixel is repainted from the picture around it, so the fence, the paper or the
    /// background carries on under them) and Russian words are written in the same place, colour and a
    /// similar font. The Russian copies replace the originals in Assets/Images/Memes, where the goose picks
    /// its memes; the originals wait in Memes/en (the goose doesn't look into subfolders) and come back when
    /// RussianMemes is switched off. Other pictures in the folder, including the user's own, stay as they are.
    /// </summary>
    internal static class MemeTranslator
    {
        internal enum Ink { White, WhiteAndOutline, Dark, Brown, Blue }
        internal enum Fill { Around, Vertical }

        internal sealed class Patch
        {
            // where the English words are: a box centred at (X, Y), turned by Angle degrees clockwise
            public float X, Y, W, H, Angle;
            public Ink Ink;
            public Fill Fill = Fill.Around;
            // what to write there instead ("" = only wipe); by default in the same box
            public string Text = "";
            public float TX, TY, TW, TH;
            public float Size;          // biggest font size, px; smaller if the words don't fit the box
            public string[] Fonts;
            public FontStyle Style = FontStyle.Regular;
            public Color Color = Color.Black;
            public Color Outline = Color.Empty;
            public float OutlineWidth;
        }

        internal sealed class Meme
        {
            public string File;
            public int Width, Height;
            public Patch[] Patches;
        }

        private static readonly string[] Sans = { "Arial", "Liberation Sans", "DejaVu Sans" };
        private static readonly string[] Serif = { "Georgia", "Times New Roman", "Liberation Serif", "DejaVu Serif" };
        private static readonly string[] Hand = { "Segoe Print", "Comic Sans MS", "Comic Neue", "DejaVu Sans" };
        private static readonly string[] Comic = { "Comic Sans MS", "Segoe Print", "Comic Neue", "DejaVu Sans" };
        private static readonly string[] Narrow = { "Bahnschrift Light Condensed", "Bahnschrift Condensed", "Arial Narrow", "Nimbus Sans Narrow", "Liberation Sans Narrow", "Arial" };

        public static readonly Meme[] Memes =
        {
            new Meme
            {
                File = "Meme1.png", Width = 640, Height = 620,   // *inhales* … HONK
                Patches = new[]
                {
                    new Patch { X = 503, Y = 147, W = 150, H = 32, Ink = Ink.White, Fill = Fill.Vertical,
                                Text = "*вдыхает*", Size = 30, Fonts = Sans, Color = Color.White },
                    new Patch { X = 510, Y = 537, W = 150, H = 44, Ink = Ink.White, Fill = Fill.Vertical,
                                Text = "ГА-ГА!", TW = 170, Size = 46, Fonts = Sans, Style = FontStyle.Bold, Color = Color.White },
                },
            },
            new Meme
            {
                File = "Meme2.png", Width = 640, Height = 479,   // stonks → honks
                Patches = new[]
                {
                    new Patch { X = 482, Y = 307, W = 236, H = 76, Ink = Ink.WhiteAndOutline,
                                Text = "гогонкс", TW = 262, TH = 76, Size = 84, Fonts = Sans, Color = Color.White,
                                Outline = Color.FromArgb(20, 20, 20), OutlineWidth = 7 },
                },
            },
            new Meme
            {
                File = "Meme3.png", Width = 828, Height = 817,   // to do: honk / busy day
                Patches = new[]
                {
                    // "to do:" and its underline go; "план:" comes back underlined
                    new Patch { X = 177, Y = 457, W = 152, H = 84, Angle = 19, Ink = Ink.Brown,
                                Text = "план:", TX = 178, TY = 452, TW = 140, TH = 66, Size = 50, Fonts = Comic,
                                Style = FontStyle.Underline, Color = Color.FromArgb(118, 106, 92) },
                    new Patch { X = 147, Y = 532, W = 112, H = 58, Angle = 16, Ink = Ink.Brown,
                                Text = "гоготать", TX = 160, TY = 532, TW = 190, TH = 56, Size = 44, Fonts = Comic,
                                Color = Color.FromArgb(118, 106, 92) },
                    new Patch { X = 619, Y = 284, W = 196, H = 56, Ink = Ink.Dark,
                                Text = "насыщенный день", TW = 330, TH = 56, Size = 44, Fonts = Comic, Style = FontStyle.Bold,
                                Color = Color.FromArgb(10, 10, 10) },
                },
            },
            new Meme
            {
                File = "Meme5.png", Width = 538, Height = 447,   // Looking for trouble and if I cannot find it, I will create it.
                Patches = new[]
                {
                    Strip(349, 62, 280, 40, "Ищу неприятности"),
                    Strip(387, 136, 214, 34, "а если не найду"),
                    Strip(414, 225, 102, 36, "их,"),
                    Strip(342, 325, 80, 30, "то сам"),
                    Strip(395, 385, 118, 34, "устрою."),
                },
            },
            new Meme
            {
                File = "Meme6.png", Width = 960, Height = 960,   // MESS WITH THE HONK / YOU GET THE BONK
                Patches = new[]
                {
                    new Patch { X = 480, Y = 65, W = 612, H = 96, Ink = Ink.Dark,
                                Text = "КТО ГУСЯ ОБИДИТ", TW = 640, Size = 104, Fonts = Narrow, Color = Color.FromArgb(34, 30, 32) },
                    new Patch { X = 480, Y = 889, W = 612, H = 104, Ink = Ink.Dark,
                                Text = "ТОТ ДУБИНУ УВИДИТ", TW = 640, Size = 104, Fonts = Narrow, Color = Color.FromArgb(34, 30, 32) },
                },
            },
            new Meme
            {
                File = "Meme7.png", Width = 512, Height = 512,   // i m an agent of chaos
                Patches = new[]
                {
                    new Patch { X = 118, Y = 73, W = 156, H = 76, Ink = Ink.Dark, Text = "я", Size = 80, Fonts = Hand, Color = Color.Black },
                    new Patch { X = 56, Y = 166, W = 84, H = 42, Ink = Ink.Dark },
                    new Patch { X = 171, Y = 266, W = 208, H = 86, Ink = Ink.Blue,
                                Text = "агент", Size = 84, Fonts = Hand, Color = Color.FromArgb(10, 168, 232) },
                    new Patch { X = 185, Y = 390, W = 100, H = 170, Ink = Ink.Blue },
                    new Patch { X = 314, Y = 406, W = 176, H = 100, Ink = Ink.Blue,
                                Text = "хаоса", TX = 300, TY = 410, TW = 250, TH = 96, Size = 88, Fonts = Hand, Color = Color.FromArgb(10, 168, 232) },
                },
            },
        };

        private static Patch Strip(float x, float y, float w, float h, string text)
        {
            // a paper strip: wipe its black words, write the Russian ones (a bit inside the strip's edge)
            return new Patch { X = x, Y = y, W = w, H = h, Ink = Ink.Dark, Text = text, TW = w - 6, TH = h - 4, Size = 34,
                               Fonts = Serif, Color = Color.FromArgb(28, 24, 22) };
        }

        public static Meme Find(string fileName)
        {
            foreach (Meme m in Memes)
                if (string.Equals(m.File, fileName, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        // ------------------------------------------------------------------ files

        /// <summary>Russian copies in, originals to Memes/en — or back, when <paramref name="russian"/> is off. Returns how many memes are Russian now.</summary>
        public static int Apply(string gooseDir, bool russian)
        {
            if (string.IsNullOrEmpty(gooseDir)) return 0;
            string dir = Path.Combine(Path.Combine(Path.Combine(gooseDir, "Assets"), "Images"), "Memes");
            if (!Directory.Exists(dir)) return 0;
            string enDir = Path.Combine(dir, "en");
            int done = 0;
            foreach (Meme m in Memes)
            {
                string path = Path.Combine(dir, m.File), parked = Path.Combine(enDir, m.File);
                try
                {
                    if (russian)
                    {
                        if (File.Exists(parked))
                        {
                            // already done; a Russian copy that went missing is made again from the original
                            if (!File.Exists(path)) Save(TranslateFile(parked, m), path);
                            done++;
                            continue;
                        }
                        if (!File.Exists(path)) continue;
                        Bitmap ru = TranslateFile(path, m);
                        if (ru == null) continue; // a different picture under the same name: leave it be
                        Directory.CreateDirectory(enDir);
                        File.Move(path, parked);
                        Save(ru, path);
                        done++;
                    }
                    else if (File.Exists(parked))
                    {
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(parked, path);
                    }
                }
                catch (Exception ex) { Deluxe.Log("meme " + m.File + ": " + ex.Message); }
            }
            if (!russian)
            {
                try { if (Directory.Exists(enDir) && Directory.GetFileSystemEntries(enDir).Length == 0) Directory.Delete(enDir); }
                catch { }
            }
            return done;
        }

        private static Bitmap TranslateFile(string path, Meme m)
        {
            using (FileStream fs = File.OpenRead(path))
            using (Image src = Image.FromStream(fs))
            {
                if (src.Width != m.Width || src.Height != m.Height) return null;
                return Translate(src, m);
            }
        }

        private static void Save(Bitmap bmp, string path)
        {
            if (bmp == null) return;
            using (bmp) bmp.Save(path, ImageFormat.Png);
        }

        // ------------------------------------------------------------------ pictures

        public static Bitmap Translate(Image src, Meme m)
        {
            Bitmap bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            }
            int w = bmp.Width, h = bmp.Height;
            int[] px = new int[w * h];
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w, w);
                foreach (Patch p in m.Patches) Wipe(px, w, h, p);
                for (int y = 0; y < h; y++) Marshal.Copy(px, y * w, data.Scan0 + y * data.Stride, w);
            }
            finally { bmp.UnlockBits(data); }

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                foreach (Patch p in m.Patches) Write(g, p);
            }
            return bmp;
        }

        private static bool IsInk(int argb, Ink ink)
        {
            int r = (argb >> 16) & 255, g = (argb >> 8) & 255, b = argb & 255;
            int lum = (r * 299 + g * 587 + b * 114) / 1000;
            switch (ink)
            {
                case Ink.White: return r > 200 && g > 200 && b > 200;
                case Ink.WhiteAndOutline: return (r > 215 && g > 215 && b > 215) || (r < 70 && g < 70 && b < 70);
                case Ink.Dark: return lum < 150;
                case Ink.Brown: return lum < 185;
                case Ink.Blue: return b > r + 60 && b > g + 20;
            }
            return false;
        }

        private static bool Inside(Patch p, float grow, int x, int y, double cos, double sin)
        {
            double dx = x - p.X, dy = y - p.Y;
            double u = dx * cos + dy * sin, v = -dx * sin + dy * cos;
            return Math.Abs(u) <= p.W / 2 + grow && Math.Abs(v) <= p.H / 2 + grow;
        }

        /// <summary>Repaints the letter pixels (and a thin rim around them) from the picture around them.</summary>
        internal static void Wipe(int[] px, int w, int h, Patch p)
        {
            const int grow = 2;
            double a = p.Angle * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            double hw = p.W / 2 + grow + 1, hh = p.H / 2 + grow + 1;
            double ex = Math.Abs(hw * cos) + Math.Abs(hh * sin), ey = Math.Abs(hw * sin) + Math.Abs(hh * cos);
            int x0 = Math.Max(0, (int)Math.Floor(p.X - ex)), x1 = Math.Min(w - 1, (int)Math.Ceiling(p.X + ex));
            int y0 = Math.Max(0, (int)Math.Floor(p.Y - ey)), y1 = Math.Min(h - 1, (int)Math.Ceiling(p.Y + ey));
            int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
            if (bw <= 0 || bh <= 0) return;

            bool[] ink = new bool[bw * bh];
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (Inside(p, 0, x, y, cos, sin) && IsInk(px[y * w + x], p.Ink)) ink[(y - y0) * bw + (x - x0)] = true;
            // the letters' soft edges blend into the background: take a couple of pixels around them too
            bool[] mask = new bool[bw * bh];
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    if (!ink[y * bw + x]) continue;
                    for (int dy = -grow; dy <= grow; dy++)
                        for (int dx = -grow; dx <= grow; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < bw && ny < bh && Inside(p, grow, nx + x0, ny + y0, cos, sin)) mask[ny * bw + nx] = true;
                        }
                }

            if (p.Fill == Fill.Vertical) FillVertical(px, w, h, x0, y0, bw, bh, mask);
            FillAround(px, w, x0, y0, bw, bh, mask); // whatever is left
        }

        /// <summary>Each masked run in a column blends from the pixel above it to the pixel below (keeps vertical boards and stripes).</summary>
        private static void FillVertical(int[] px, int w, int h, int x0, int y0, int bw, int bh, bool[] mask)
        {
            for (int x = 0; x < bw; x++)
            {
                int y = 0;
                while (y < bh)
                {
                    if (!mask[y * bw + x]) { y++; continue; }
                    int top = y - 1, bottom = y;
                    while (bottom < bh && mask[bottom * bw + x]) bottom++;
                    int gx = x + x0, gyTop = top + y0, gyBottom = bottom + y0;
                    bool hasTop = gyTop >= 0, hasBottom = gyBottom < h;
                    if (hasTop || hasBottom)
                    {
                        int cTop = px[(hasTop ? gyTop : gyBottom) * w + gx], cBottom = px[(hasBottom ? gyBottom : gyTop) * w + gx];
                        int n = bottom - top;
                        for (int yy = y; yy < bottom; yy++)
                        {
                            float t = (yy - top) / (float)n;
                            px[(yy + y0) * w + gx] = Lerp(cTop, cBottom, t);
                            mask[yy * bw + x] = false;
                        }
                    }
                    y = bottom;
                }
            }
        }

        /// <summary>Fills the masked pixels ring by ring from the outside in, each one the average of its already known neighbours.</summary>
        private static void FillAround(int[] px, int w, int x0, int y0, int bw, int bh, bool[] mask)
        {
            List<int> todo = new List<int>();
            for (int i = 0; i < mask.Length; i++) if (mask[i]) todo.Add(i);
            List<KeyValuePair<int, int>> ring = new List<KeyValuePair<int, int>>();
            while (todo.Count > 0)
            {
                ring.Clear();
                List<int> rest = new List<int>();
                foreach (int i in todo)
                {
                    int x = i % bw, y = i / bw;
                    int sa = 0, sr = 0, sg = 0, sb = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= bw || ny >= bh || mask[ny * bw + nx]) continue;
                            int c = px[(ny + y0) * w + nx + x0];
                            sa += (c >> 24) & 255; sr += (c >> 16) & 255; sg += (c >> 8) & 255; sb += c & 255; n++;
                        }
                    if (n == 0) { rest.Add(i); continue; }
                    ring.Add(new KeyValuePair<int, int>(i, ((sa / n) << 24) | ((sr / n) << 16) | ((sg / n) << 8) | (sb / n)));
                }
                if (ring.Count == 0) break; // nothing known around (can't happen unless the whole box is ink)
                foreach (KeyValuePair<int, int> kv in ring)
                {
                    int x = kv.Key % bw, y = kv.Key / bw;
                    px[(y + y0) * w + x + x0] = kv.Value;
                    mask[kv.Key] = false;
                }
                todo = rest;
            }
        }

        private static int Lerp(int a, int b, float t)
        {
            int ca = (int)(((a >> 24) & 255) + (((b >> 24) & 255) - ((a >> 24) & 255)) * t);
            int cr = (int)(((a >> 16) & 255) + (((b >> 16) & 255) - ((a >> 16) & 255)) * t);
            int cg = (int)(((a >> 8) & 255) + (((b >> 8) & 255) - ((a >> 8) & 255)) * t);
            int cb = (int)((a & 255) + ((b & 255) - (a & 255)) * t);
            return (ca << 24) | (cr << 16) | (cg << 8) | cb;
        }

        /// <summary>The Russian words, centred in their box, as big as fits (up to the patch's size).</summary>
        private static void Write(Graphics g, Patch p)
        {
            if (string.IsNullOrEmpty(p.Text)) return;
            FontFamily family = Family(p.Fonts);
            FontStyle style = family.IsStyleAvailable(p.Style) ? p.Style : FontStyle.Regular;
            float cx = p.TX > 0 ? p.TX : p.X, cy = p.TY > 0 ? p.TY : p.Y;
            float bw = p.TW > 0 ? p.TW : p.W, bh = p.TH > 0 ? p.TH : p.H;
            using (GraphicsPath path = new GraphicsPath())
            using (StringFormat sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                path.AddString(p.Text, family, (int)style, p.Size, new PointF(0, 0), sf);
                RectangleF b = path.GetBounds();
                if (b.Width <= 0 || b.Height <= 0) return;
                float room = p.OutlineWidth;
                float k = Math.Min(1f, Math.Min((bw - room) / b.Width, (bh - room) / b.Height));
                using (Matrix mx = new Matrix())
                {
                    mx.Translate(cx, cy, MatrixOrder.Append);
                    mx.Rotate(p.Angle, MatrixOrder.Prepend);
                    mx.Scale(k, k, MatrixOrder.Prepend);
                    mx.Translate(-(b.X + b.Width / 2), -(b.Y + b.Height / 2), MatrixOrder.Prepend);
                    path.Transform(mx);
                }
                if (p.Outline != Color.Empty && p.OutlineWidth > 0)
                    using (Pen pen = new Pen(p.Outline, p.OutlineWidth * Math.Max(0.5f, k)) { LineJoin = LineJoin.Round })
                        g.DrawPath(pen, path);
                using (Brush brush = new SolidBrush(p.Color)) g.FillPath(brush, path);
            }
        }

        private static readonly Dictionary<string, FontFamily> families = new Dictionary<string, FontFamily>();

        private static FontFamily Family(string[] names)
        {
            foreach (string name in names ?? new string[0])
            {
                FontFamily f;
                if (families.TryGetValue(name, out f)) { if (f != null) return f; continue; }
                try
                {
                    f = new FontFamily(name);
                    // Mono/libgdiplus quietly substitutes unknown names; keep looking unless it's the real one
                    if (!string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) f = null;
                }
                catch (ArgumentException) { f = null; }
                families[name] = f;
                if (f != null) return f;
            }
            return FontFamily.GenericSansSerif;
        }
    }
}
