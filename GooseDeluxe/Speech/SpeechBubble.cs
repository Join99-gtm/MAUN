using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// A comic speech bubble over the goose's head. The lines are laid out once for the whole phrase, so the
    /// text can type itself in without anything jumping; the bubble stays on screen and its tail points at
    /// the goose's head (from below the goose when there's no room above).
    /// </summary>
    internal sealed class SpeechBubble
    {
        public const float MaxTextWidth = 300f, Pad = 11f, Radius = 12f, TailLength = 16f, Margin = 6f;

        private static Font font;
        private static Font Font { get { return font ?? (font = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel)); } }

        private readonly string text;
        private readonly List<int> starts = new List<int>(), lengths = new List<int>();
        private float textWidth, lineHeight;
        private bool laidOut;

        public SpeechBubble(string text) { this.text = text ?? ""; }

        public int Lines { get { return starts.Count; } }

        /// <summary>Greedy word wrap, measured with the font the bubble is drawn with.</summary>
        public void Layout(Graphics g)
        {
            if (laidOut) return;
            laidOut = true;
            StringFormat fmt = StringFormat.GenericTypographic;
            lineHeight = (float)Math.Ceiling(Font.GetHeight(g)) + 1f;
            int lineStart = 0, lineEnd = 0, i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && text[i] == ' ') i++;
                int wordStart = i;
                while (i < text.Length && text[i] != ' ') i++;
                if (i == wordStart) break;
                float w = g.MeasureString(text.Substring(lineStart, i - lineStart), Font, PointF.Empty, fmt).Width;
                if (w > MaxTextWidth && lineEnd > lineStart)
                {
                    Add(g, lineStart, lineEnd, fmt);
                    lineStart = wordStart;
                    w = g.MeasureString(text.Substring(lineStart, i - lineStart), Font, PointF.Empty, fmt).Width;
                }
                // a word wider than the bubble on its own («АААААА…», a link): broken by characters
                while (w > MaxTextWidth && i - lineStart > 1)
                {
                    int n = 1;
                    while (lineStart + n < i && g.MeasureString(text.Substring(lineStart, n + 1), Font, PointF.Empty, fmt).Width <= MaxTextWidth) n++;
                    Add(g, lineStart, lineStart + n, fmt);
                    lineStart += n;
                    w = g.MeasureString(text.Substring(lineStart, i - lineStart), Font, PointF.Empty, fmt).Width;
                }
                lineEnd = i;
            }
            if (lineEnd > lineStart) Add(g, lineStart, lineEnd, fmt);
            if (starts.Count == 0) { starts.Add(0); lengths.Add(0); }
        }

        private void Add(Graphics g, int from, int to, StringFormat fmt)
        {
            starts.Add(from);
            lengths.Add(to - from);
            textWidth = Math.Max(textWidth, g.MeasureString(text.Substring(from, to - from), Font, PointF.Empty, fmt).Width);
        }

        /// <summary>Where the bubble goes for a goose whose head top is at <paramref name="head"/> and whose
        /// feet are at <paramref name="feet"/>.</summary>
        public RectangleF Place(Vector2 head, Vector2 feet, Vector2 screen, out bool below)
        {
            float w = Math.Min(textWidth + 2 * Pad, Math.Max(40f, screen.x - 2 * Margin));
            float h = starts.Count * lineHeight + 2 * Pad - 2f;
            float x = head.x - w * 0.5f, y = head.y - TailLength - h;
            below = y < Margin;
            if (below) y = feet.y + TailLength;
            x = Math.Max(Margin, Math.Min(x, screen.x - w - Margin));
            y = Math.Max(Margin, Math.Min(y, screen.y - h - Margin));
            return new RectangleF(x, y, w, h);
        }

        public void Draw(Graphics g, Vector2 head, Vector2 feet, Vector2 screen, int shown, float alpha)
        {
            if (alpha <= 0.01f) return;
            Layout(g);
            bool below;
            RectangleF r = Place(head, feet, screen, out below);
            int a = (int)(255 * M.Clamp01(alpha));

            SmoothingMode oldSmoothing = g.SmoothingMode;
            TextRenderingHint oldHint = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Outline(r, head, feet, below))
            {
                using (GraphicsPath shadow = (GraphicsPath)path.Clone())
                using (Matrix m = new Matrix())
                using (Brush sb = new SolidBrush(Color.FromArgb(a * 45 / 255, 0, 0, 0)))
                {
                    m.Translate(2f, 3f);
                    shadow.Transform(m);
                    g.FillPath(sb, shadow);
                }
                using (Brush fill = new SolidBrush(Color.FromArgb(a * 248 / 255, 255, 255, 252))) g.FillPath(fill, path);
                using (Pen pen = new Pen(Color.FromArgb(a, 45, 45, 45), 2f) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
            }
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // on a transparent window: no ClearType fringes
            using (Brush ink = new SolidBrush(Color.FromArgb(a, 25, 25, 25)))
            {
                for (int i = 0; i < starts.Count; i++)
                {
                    int n = Math.Max(0, Math.Min(lengths[i], shown - starts[i]));
                    if (n > 0) g.DrawString(text.Substring(starts[i], n), Font, ink, r.X + Pad, r.Y + Pad + i * lineHeight, StringFormat.GenericTypographic);
                }
            }
            g.TextRenderingHint = oldHint;
            g.SmoothingMode = oldSmoothing;
        }

        /// <summary>A rounded box with the tail cut into its bottom (or top) edge, pointing at the goose.</summary>
        private static GraphicsPath Outline(RectangleF r, Vector2 head, Vector2 feet, bool below)
        {
            float d = Radius * 2, half = 8f;
            Vector2 target = below ? feet : head;
            float baseX = Math.Max(r.Left + Radius + half + 2, Math.Min(target.x, r.Right - Radius - half - 2));
            float tipX = baseX + M.Clamp(target.x - baseX, -TailLength, TailLength) * 0.8f;
            GraphicsPath p = new GraphicsPath();
            p.StartFigure();
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            if (below)
            {
                p.AddLine(baseX - half, r.Top, tipX, r.Top - TailLength);
                p.AddLine(tipX, r.Top - TailLength, baseX + half, r.Top);
            }
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            if (!below)
            {
                p.AddLine(baseX + half, r.Bottom, tipX, r.Bottom + TailLength);
                p.AddLine(tipX, r.Bottom + TailLength, baseX - half, r.Bottom);
            }
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
