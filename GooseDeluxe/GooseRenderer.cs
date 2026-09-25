using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// Draws the goose onto the ARGB overlay. Same silhouette as the original (thick round-capped
    /// lines), but anti-aliased and with legs, a soft shadow, wings, an opening beak, eyes that blink
    /// and look around, and an optional hat.
    /// </summary>
    internal sealed class GooseRenderer
    {
        private readonly DeluxeConfig cfg;
        private static readonly Color footmarkColor = Color.SaddleBrown;

        public GooseRenderer(DeluxeConfig cfg) { this.cfg = cfg; }

        public void Draw(Graphics g, GoosePose p, GooseEntity ge, ParticleSystem particles, float now)
        {
            g.SmoothingMode = cfg.AntiAlias ? SmoothingMode.AntiAlias : SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            Color white = ge.renderData.brushGooseWhite.Color;
            Color orange = ge.renderData.brushGooseOrange.Color;
            Color outline = ge.renderData.brushGooseOutline.Color;
            Color legColor = M.Mix(orange, Color.Black, 0.18f);
            float s = p.scale;

            DrawFootmarks(g, ge, now, s);
            DrawShadow(g, p);

            // legs + feet
            if (cfg.Legs)
            {
                Vector2 hip = p.underbodyCenter + M.Up * -2f * s;
                Line(g, legColor, 2.2f * s, hip + p.perp * 2f * s, p.lFoot);
                Line(g, legColor, 2.2f * s, hip - p.perp * 2f * s, p.rFoot);
            }
            Circle(g, orange, p.lFoot, 4f * s);
            Circle(g, orange, p.rFoot, 4f * s);

            // Wings: both outlines go under the body so the near wing's outline only shows where it
            // sticks out of the silhouette; the far wing is filled before the body, the near one after.
            int nearSide = p.perp.y >= 0f ? 1 : -1; // lower on screen == closer to the viewer
            bool wings = p.wingOpen > 0.02f;
            Color wingFill = M.Mix(white, outline, 0.22f);
            if (wings)
            {
                DrawWing(g, p, -nearSide, outline, true);
                DrawWing(g, p, nearSide, outline, true);
                DrawWing(g, p, -nearSide, wingFill, false);
            }

            DrawBody(g, p, white, outline);

            if (wings) DrawWing(g, p, nearSide, wingFill, false);

            DrawBeak(g, p, orange);
            DrawEyes(g, p);
            DrawHat(g, p);
            if (p.mouseHeld) DrawStruggle(g, p, now);

            particles.Draw(g, now);
        }

        private void DrawFootmarks(Graphics g, GooseEntity ge, float now, float s)
        {
            FootMark[] marks = ge.footMarks;
            if (marks == null) return;
            for (int i = 0; i < marks.Length; i++)
            {
                if (marks[i].time == 0f) continue;
                float fadeStart = marks[i].time + FootMark.Lifetime;
                float f = M.Clamp01((now - fadeStart) / FootMark.ShrinkTime);
                if (f >= 1f) continue;
                float r = M.Lerp(3f, 0f, f) * s;
                Circle(g, footmarkColor, marks[i].position, r);
            }
        }

        private void DrawShadow(Graphics g, GoosePose p)
        {
            float s = p.scale;
            float rx = 21f * s * (1f - p.squash * 0.5f);
            float ry = 10f * s;
            Vector2 c = p.pos + new Vector2(0f, 2f * s);
            if (!cfg.SoftShadow)
            {
                Ellipse(g, Color.FromArgb(70, 0, 0, 0), c, rx, ry);
                return;
            }
            // stacked translucent ellipses: darkest in the middle, feathered at the edge
            const int layers = 8;
            for (int i = 0; i < layers; i++)
            {
                float k = 1f - i / (float)layers * 0.8f;
                Ellipse(g, Color.FromArgb(14, 0, 0, 0), c, rx * k, ry * k);
            }
        }

        private void DrawBody(Graphics g, GoosePose p, Color white, Color outline)
        {
            float s = p.scale;
            float bodyW = (22f + 22f * p.puff) * s;
            float neckW = 13f * s, head1W = 15f * s, head2W = 10f * s, underW = 15f * s;
            float o = 2f * s; // outline thickness
            Vector2 bodyA = p.bodyCenter + p.fwd * 11f * s * (1f + p.squash), bodyB = p.bodyCenter - p.fwd * 11f * s * (1f + p.squash);
            Vector2 underA = p.underbodyCenter + p.fwd * 7f * s, underB = p.underbodyCenter - p.fwd * 7f * s;

            // outline pass
            Line(g, outline, bodyW + o, bodyA, bodyB);
            Line(g, outline, neckW + o, p.neckBase, p.neckHeadPoint);
            Line(g, outline, head1W + o, p.neckHeadPoint, p.head1EndPoint);
            Line(g, outline, head2W + o, p.head1EndPoint, p.head2EndPoint);
            // grey belly, like the original
            Line(g, outline, underW, underA, underB);
            // fill pass
            Line(g, white, bodyW, bodyA, bodyB);
            Line(g, white, neckW, p.neckBase, p.neckHeadPoint);
            Line(g, white, head1W, p.neckHeadPoint, p.head1EndPoint);
            Line(g, white, head2W, p.head1EndPoint, p.head2EndPoint);
        }

        private void DrawWing(Graphics g, GoosePose p, int side, Color color, bool outlinePass)
        {
            float s = p.scale;
            float open = p.wingOpen;
            float flap = (float)Math.Sin(p.flap);
            // perspective: sideways offsets are squashed vertically, as with the eyes in the original
            Vector2 sideVec = new Vector2(p.perp.x * side * 1.3f, p.perp.y * side * 0.4f);
            Vector2 root = p.bodyCenter + sideVec * 7f * s - p.fwd * 1f * s;
            Vector2 elbow = root - p.fwd * 10f * s * open + sideVec * 8f * s * open + M.Up * (6f + flap * 7f) * s * open;
            Vector2 tip = elbow - p.fwd * 13f * s * open + sideVec * 6f * s * open + M.Up * (flap * 9f - 3f) * s * open;
            float o = outlinePass ? 2f * s : 0f;
            Line(g, color, 8f * s + o, root, elbow);
            Line(g, color, 5f * s + o, elbow, tip);
            if (!outlinePass)
            {
                // a couple of feather tips
                Color tipColor = M.Mix(color, Color.Gray, 0.35f);
                Line(g, tipColor, 1.2f * s, tip, tip - p.fwd * 4f * s * open + M.Up * 1.5f * s);
                Line(g, tipColor, 1.2f * s, tip + M.Up * -2.5f * s, tip - p.fwd * 4f * s * open - M.Up * 1.5f * s);
            }
        }

        private void DrawBeak(Graphics g, GoosePose p, Color orange)
        {
            float s = p.scale;
            float open = p.beakOpen;
            Vector2 tipBase = p.head2EndPoint;
            if (open < 0.05f)
            {
                Line(g, orange, 9f * s, tipBase, tipBase + p.fwdHead * 3f * s);
                return;
            }
            float ang = open * 22f;
            Vector2 upperDir = M.Rotate(p.fwdHead, p.fwdHead.x >= 0f ? -ang : ang);
            Vector2 lowerDir = M.Rotate(p.fwdHead, p.fwdHead.x >= 0f ? ang : -ang);
            // dark mouth interior between the halves
            Line(g, Color.FromArgb(120, 40, 20), 5f * s, tipBase, tipBase + p.fwdHead * 3f * s);
            Line(g, orange, 4.6f * s, tipBase + M.Up * 2.2f * s, tipBase + M.Up * 2.2f * s + upperDir * 4f * s);
            Line(g, orange, 4.2f * s, tipBase - M.Up * 2.2f * s, tipBase - M.Up * 2.2f * s + lowerDir * 3.5f * s);
        }

        private void DrawEyes(Graphics g, GoosePose p)
        {
            float s = p.scale;
            Vector2 sideVec = new Vector2(p.perpHead.x * 1.3f, p.perpHead.y * 0.4f) * 5f * s;
            Vector2 baseP = p.neckHeadPoint + M.Up * 3f * s + p.fwdHead * 5f * s;
            DrawEye(g, baseP - sideVec, p, s);
            DrawEye(g, baseP + sideVec, p, s);
        }

        private static void DrawEye(Graphics g, Vector2 at, GoosePose p, float s)
        {
            if (p.blink > 0.5f)
            {
                Line(g, Color.Black, 1.4f * s, at - p.fwdHead * 2f * s, at + p.fwdHead * 2f * s);
                return;
            }
            float r = 2f * s * (1f - p.blink * 0.6f);
            Vector2 pupil = at + p.eyeLook * s;
            Circle(g, Color.Black, pupil, r);
            Circle(g, Color.FromArgb(200, 255, 255, 255), pupil + new Vector2(-0.7f, -0.7f) * s, 0.65f * s);
        }

        private void DrawHat(Graphics g, GoosePose p)
        {
            if (cfg.Hat == HatStyle.None) return;
            float s = p.scale;
            Vector2 top = p.neckHeadPoint + M.Up * 8f * s + p.fwdHead * 2.5f * s;
            switch (cfg.Hat)
            {
                case HatStyle.TopHat:
                    {
                        Color black = Color.FromArgb(30, 30, 34);
                        float brimW = 11f * s, brimH = 3f * s;
                        Ellipse(g, black, top, brimW, brimH);
                        RectangleF crown = new RectangleF(top.x - 7f * s, top.y - 13f * s, 14f * s, 13f * s);
                        using (SolidBrush b = new SolidBrush(black)) g.FillRectangle(b, crown);
                        Ellipse(g, Color.FromArgb(45, 45, 50), top + M.Up * 13f * s, 7f * s, 2.2f * s);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(170, 30, 40)))
                            g.FillRectangle(b, top.x - 7f * s, top.y - 4.5f * s, 14f * s, 2.5f * s);
                        break;
                    }
                case HatStyle.Party:
                    {
                        PointF a = new PointF(top.x - 7f * s, top.y + 1f * s);
                        PointF b2 = new PointF(top.x + 7f * s, top.y + 1f * s);
                        PointF c = new PointF(top.x + 1.5f * s, top.y - 18f * s);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 140, 230))) g.FillPolygon(b, new[] { a, b2, c });
                        using (Pen pen = new Pen(Color.FromArgb(250, 210, 60), 2f * s))
                        {
                            g.DrawLine(pen, top.x - 4.5f * s, top.y - 5f * s, top.x + 5.5f * s, top.y - 5f * s);
                            g.DrawLine(pen, top.x - 2.2f * s, top.y - 11f * s, top.x + 4f * s, top.y - 11f * s);
                        }
                        Circle(g, Color.FromArgb(240, 80, 120), new Vector2(c.X, c.Y), 2.5f * s);
                        break;
                    }
                case HatStyle.Santa:
                    {
                        Color red = Color.FromArgb(200, 40, 45);
                        PointF a = new PointF(top.x - 8f * s, top.y + 1f * s);
                        PointF b2 = new PointF(top.x + 8f * s, top.y + 1f * s);
                        PointF c = new PointF(top.x - 6f * s, top.y - 15f * s); // tip flops backwards
                        PointF mid = new PointF(top.x + 3f * s, top.y - 9f * s);
                        using (SolidBrush b = new SolidBrush(red)) g.FillPolygon(b, new[] { a, b2, mid, c });
                        Ellipse(g, Color.White, top + M.Up * 0.5f * s, 9.5f * s, 2.8f * s);
                        Circle(g, Color.White, new Vector2(c.X, c.Y), 2.6f * s);
                        break;
                    }
            }
        }

        private static void DrawStruggle(Graphics g, GoosePose p, float now)
        {
            // little motion lines around the beak while the goose is holding the cursor
            float s = p.scale;
            Vector2 beak = p.head2EndPoint + p.fwdHead * 4f * s;
            float jitter = (float)Math.Sin(now * 40f) * 1.5f;
            Color c = Color.FromArgb(200, 255, 255, 255);
            for (int i = 0; i < 3; i++)
            {
                float ang = -50f + i * 50f + jitter * (i % 2 == 0 ? 1f : -1f);
                Vector2 d = M.Rotate(p.fwdHead, ang);
                Line(g, c, 1.3f * s, beak + d * 7f * s, beak + d * 12f * s);
            }
        }

        // ---- primitives ----

        private static void Line(Graphics g, Color c, float width, Vector2 a, Vector2 b)
        {
            if (width <= 0f) return;
            using (Pen pen = new Pen(c, width))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (Math.Abs(a.x - b.x) < 0.01f && Math.Abs(a.y - b.y) < 0.01f) b.x += 0.02f;
                g.DrawLine(pen, a.x, a.y, b.x, b.y);
            }
        }

        private static void Circle(Graphics g, Color c, Vector2 at, float r)
        {
            if (r <= 0f) return;
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, at.x - r, at.y - r, r * 2f, r * 2f);
        }

        private static void Ellipse(Graphics g, Color c, Vector2 at, float rx, float ry)
        {
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, at.x - rx, at.y - ry, rx * 2f, ry * 2f);
        }
    }
}
