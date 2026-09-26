using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using SamEngine;

namespace GooseDeluxe
{
    internal enum ParticleKind { Dust, Feather, Text, SleepZ, Snow, Smoke }

    internal struct Particle
    {
        public ParticleKind kind;
        public Vector2 pos;
        public Vector2 vel;
        public float born;
        public float life;
        public float seed;
        public float size;
        public string text;
        public Color color;
    }

    /// <summary>Dust puffs when charging, feathers when honking/bumped, floating "HONK!" text.</summary>
    internal sealed class ParticleSystem
    {
        private const int Max = 600;
        private readonly List<Particle> list = new List<Particle>();
        private static readonly Font textFont = new Font("Arial", 11f, FontStyle.Bold);
        private static readonly Color dustColor = Color.FromArgb(150, 140, 120);
        private static readonly Color smokeColor = Color.FromArgb(228, 228, 232);

        public int Count { get { return list.Count; } }

        public void SpawnDust(Vector2 at, Vector2 gooseVel, float scale, float now)
        {
            if (list.Count >= Max) return;
            Particle p = new Particle();
            p.kind = ParticleKind.Dust;
            p.pos = at + new Vector2(M.Rand(-3f, 3f), M.Rand(-2f, 2f)) * scale;
            p.vel = Vector2.Normalize(gooseVel) * -M.Rand(15f, 45f) + new Vector2(M.Rand(-10f, 10f), M.Rand(-25f, -8f));
            p.life = M.Rand(0.45f, 0.8f);
            p.size = M.Rand(2.5f, 4.5f) * scale;
            p.color = dustColor;
            p.seed = M.Rand(0f, 100f);
            p.born = now;
            list.Add(p);
        }

        public int CountOf(ParticleKind kind)
        {
            int n = 0;
            foreach (Particle p in list) if (p.kind == kind) n++;
            return n;
        }

        /// <summary>Tyre smoke from under a foot when the goose drifts: a soft puff that swells, rises and fades.</summary>
        public void SpawnSmoke(Vector2 at, Vector2 gooseVel, float scale, float strength, float now)
        {
            if (list.Count >= Max) return;
            Particle p = new Particle();
            p.kind = ParticleKind.Smoke;
            p.pos = at + new Vector2(M.Rand(-3f, 3f), M.Rand(-2f, 1f)) * scale;
            float speed = Vector2.Magnitude(gooseVel);
            Vector2 back = speed > 1f ? gooseVel * (-M.Rand(0.05f, 0.18f)) : Vector2.zero; // left behind the goose
            p.vel = back + new Vector2(M.Rand(-22f, 22f), M.Rand(-26f, -6f));
            p.life = M.Rand(1.2f, 2.1f);
            p.size = M.Rand(7f, 12f) * scale * (0.75f + 0.5f * strength);
            p.color = smokeColor;
            p.seed = M.Rand(0f, 6.28f);
            p.born = now;
            list.Add(p);
        }

        public void SpawnFeathers(Vector2 at, int count, float scale, Color color, float now)
        {
            for (int i = 0; i < count && list.Count < Max; i++)
            {
                Particle p = new Particle();
                p.kind = ParticleKind.Feather;
                p.pos = at + new Vector2(M.Rand(-6f, 6f), M.Rand(-8f, 2f)) * scale;
                p.vel = new Vector2(M.Rand(-40f, 40f), M.Rand(-70f, -20f));
                p.life = M.Rand(1.4f, 2.4f);
                p.size = M.Rand(2.5f, 4f) * scale;
                p.color = color;
                p.seed = M.Rand(0f, 6.28f);
                p.born = now;
                list.Add(p);
            }
        }

        public void SpawnText(Vector2 at, string text, Color color, float now)
        {
            if (list.Count >= Max) return;
            Particle p = new Particle();
            p.kind = ParticleKind.Text;
            p.pos = at;
            p.vel = new Vector2(M.Rand(-8f, 8f), -28f);
            p.life = 1.1f;
            p.text = text;
            p.color = color;
            p.seed = M.Rand(-12f, 12f);
            p.born = now;
            list.Add(p);
        }

        /// <summary>A drift bursting as the goose charges through it: snow thrown up and forward.</summary>
        public void SpawnSnowBurst(Vector2 at, Vector2 gooseVel, int count, float scale, float now)
        {
            Vector2 push = gooseVel * 0.35f;
            for (int i = 0; i < count && list.Count < Max; i++)
            {
                Particle p = new Particle();
                p.kind = ParticleKind.Snow;
                p.pos = at + new Vector2(M.Rand(-14f, 14f), M.Rand(-10f, 2f)) * scale;
                p.vel = push + new Vector2(M.Rand(-90f, 90f), M.Rand(-260f, -90f));
                p.life = M.Rand(0.8f, 1.4f);
                p.size = M.Rand(1.4f, 3.4f) * scale;
                p.color = M.RandInt(4) == 0 ? Color.FromArgb(255, 222, 234, 250) : Color.FromArgb(255, 252, 253, 255);
                p.born = now;
                list.Add(p);
            }
        }

        public void SpawnSleepZ(Vector2 at, float scale, float now)
        {
            if (list.Count >= Max) return;
            Particle p = new Particle();
            p.kind = ParticleKind.SleepZ;
            p.pos = at;
            p.vel = new Vector2(M.Rand(10f, 18f), -22f);
            p.life = 2.2f;
            p.size = scale;
            p.text = "z";
            p.color = Color.FromArgb(255, 235, 240, 255);
            p.seed = M.Rand(0f, 6.28f);
            p.born = now;
            list.Add(p);
        }

        public void Update(float dt, float now)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Particle p = list[i];
                float age = now - p.born;
                if (age > p.life) { list.RemoveAt(i); continue; }
                switch (p.kind)
                {
                    case ParticleKind.Dust:
                        p.vel = p.vel * (1f - 2.5f * dt);
                        break;
                    case ParticleKind.Feather:
                        // Gentle fall with a side-to-side sway, like a real feather.
                        p.vel.y += 55f * dt;
                        p.vel.y = Math.Min(p.vel.y, 35f);
                        p.vel.x = (float)Math.Sin(p.seed + age * 5f) * 30f;
                        break;
                    case ParticleKind.Text:
                        p.vel = p.vel * (1f - 1.5f * dt);
                        break;
                    case ParticleKind.SleepZ:
                        p.vel.x = 14f + (float)Math.Sin(p.seed + age * 3f) * 10f;
                        break;
                    case ParticleKind.Snow:
                        p.vel.y += 520f * dt;
                        p.vel.x *= 1f - 1.2f * dt;
                        break;
                    case ParticleKind.Smoke:
                        p.vel = p.vel * (1f - 1.8f * dt);
                        p.vel.y -= 10f * dt; // warm smoke drifts up a little
                        break;
                }
                p.pos += p.vel * dt;
                list[i] = p;
            }
        }

        /// <summary>Tyre smoke goes under the geese (they burst out of their own cloud); call before drawing them.</summary>
        public void DrawSmoke(Graphics g, float now) { Draw(g, now, true); }

        public void Draw(Graphics g, float now) { Draw(g, now, false); }

        private void Draw(Graphics g, float now, bool smoke)
        {
            if (list.Count == 0) return;
            // hundreds of big soft puffs: without anti-aliasing they cost a fraction and look the same
            SmoothingMode oldSmoothing = g.SmoothingMode;
            if (smoke) g.SmoothingMode = SmoothingMode.HighSpeed;
            TextRenderingHint oldHint = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            for (int i = 0; i < list.Count; i++)
            {
                Particle p = list[i];
                if ((p.kind == ParticleKind.Smoke) != smoke) continue;
                float t = M.Clamp01((now - p.born) / p.life);
                switch (p.kind)
                {
                    case ParticleKind.Dust:
                        {
                            float r = p.size * (0.6f + t * 1.2f);
                            using (SolidBrush b = new SolidBrush(M.WithAlpha(p.color, 0.45f * (1f - t))))
                                g.FillEllipse(b, p.pos.x - r, p.pos.y - r * 0.8f, r * 2f, r * 1.6f);
                            break;
                        }
                    case ParticleKind.Feather:
                        {
                            float alpha = t > 0.7f ? (1f - t) / 0.3f : 1f;
                            float rot = (float)Math.Sin(p.seed + (now - p.born) * 5f) * 35f;
                            using (SolidBrush b = new SolidBrush(M.WithAlpha(p.color, alpha)))
                            using (Pen edge = new Pen(M.WithAlpha(Color.FromArgb(170, 170, 175), alpha), 1f))
                            using (Pen pen = new Pen(M.WithAlpha(Color.FromArgb(190, 190, 195), alpha * 0.9f), 0.8f))
                            {
                                g.TranslateTransform(p.pos.x, p.pos.y);
                                g.RotateTransform(rot);
                                g.FillEllipse(b, -p.size * 0.5f, -p.size * 1.3f, p.size, p.size * 2.6f);
                                g.DrawEllipse(edge, -p.size * 0.5f, -p.size * 1.3f, p.size, p.size * 2.6f);
                                g.DrawLine(pen, 0f, -p.size * 1.1f, 0f, p.size * 1.1f);
                                g.ResetTransform();
                            }
                            break;
                        }
                    case ParticleKind.Smoke:
                        {
                            float r = p.size * (0.7f + 2.4f * t);
                            float alpha = (t < 0.12f ? t / 0.12f : 1f) * (float)Math.Pow(1f - t, 1.4f);
                            using (SolidBrush outer = new SolidBrush(M.WithAlpha(p.color, 0.34f * alpha)))
                                g.FillEllipse(outer, p.pos.x - r * 1.35f, p.pos.y - r * 1.1f, r * 2.7f, r * 2.2f);
                            using (SolidBrush core = new SolidBrush(M.WithAlpha(Color.FromArgb(210, 210, 216), 0.6f * alpha)))
                                g.FillEllipse(core, p.pos.x - r, p.pos.y - r * 0.8f, r * 2f, r * 1.6f);
                            break;
                        }
                    case ParticleKind.Snow:
                        {
                            float alpha = t > 0.6f ? (1f - t) / 0.4f : 1f;
                            using (SolidBrush b = new SolidBrush(M.WithAlpha(p.color, alpha)))
                                g.FillEllipse(b, p.pos.x - p.size / 2f, p.pos.y - p.size / 2f, p.size, p.size);
                            break;
                        }
                    case ParticleKind.SleepZ:
                        {
                            float alpha = t < 0.15f ? t / 0.15f : (t > 0.6f ? (1f - t) / 0.4f : 1f);
                            float px = (9f + 9f * t) * p.size;
                            using (Font f = new Font("Arial", Math.Max(4f, px), FontStyle.Bold, GraphicsUnit.Pixel))
                            using (SolidBrush shadow = new SolidBrush(M.WithAlpha(Color.Black, alpha * 0.4f)))
                            using (SolidBrush b = new SolidBrush(M.WithAlpha(p.color, alpha)))
                            {
                                g.DrawString(p.text, f, shadow, p.pos.x + 1f, p.pos.y + 1f);
                                g.DrawString(p.text, f, b, p.pos.x, p.pos.y);
                            }
                            break;
                        }
                    case ParticleKind.Text:
                        {
                            float alpha = t > 0.55f ? (1f - t) / 0.45f : 1f;
                            float pop = t < 0.15f ? 0.6f + 0.4f * (t / 0.15f) : 1f;
                            SizeF sz = g.MeasureString(p.text, textFont);
                            g.TranslateTransform(p.pos.x, p.pos.y);
                            g.RotateTransform(p.seed);
                            g.ScaleTransform(pop, pop);
                            using (SolidBrush shadow = new SolidBrush(M.WithAlpha(Color.Black, alpha * 0.55f)))
                            using (SolidBrush b = new SolidBrush(M.WithAlpha(p.color, alpha)))
                            {
                                g.DrawString(p.text, textFont, shadow, -sz.Width / 2f + 1f, -sz.Height / 2f + 1f);
                                g.DrawString(p.text, textFont, b, -sz.Width / 2f, -sz.Height / 2f);
                            }
                            g.ResetTransform();
                            break;
                        }
                }
            }
            g.TextRenderingHint = oldHint;
            g.SmoothingMode = oldSmoothing;
        }
    }
}
