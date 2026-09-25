using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>A mound of snow the goose can charge through.</summary>
    internal sealed class SnowDrift
    {
        public Vector2 pos;
        public float rx, ry;
        public float born;
        public float kickedAt = -1f;
        public float meltAt = -1f;
        /// <summary>A test drift made outside winter doesn't melt away before this time.</summary>
        public float keepUntil = -1f;
        public bool Kicked { get { return kickedAt >= 0f; } }
        public bool Fading { get { return kickedAt >= 0f || meltAt >= 0f; } }
    }

    /// <summary>
    /// Winter on the desktop: snowfall that comes and goes, drifts the goose kicks up, footprints in the
    /// snow, and snow piling up along the bottom edge (on top of the taskbar). Everything melts away
    /// when winter ends (or the system date moves out of it).
    /// </summary>
    internal sealed class WinterScene
    {
        private struct Flake { public float x, y, speed, phase, sway; public int sprite; }
        private struct Print { public Vector2 pos; public float dir; public float born; }
        private sealed class Feet { public float l = -1f, r = -1f; }

        public const int MaxDrifts = 3;
        public const float PrintLife = 10f;
        public const int MaxPrints = 140;
        public const float MaxBank = 14f;
        private const float BankGrowSeconds = 480f; // 8 minutes of snowfall for a full bank
        private const float BankMeltSeconds = 60f;

        private readonly List<SnowDrift> drifts = new List<SnowDrift>();
        private readonly List<Flake> flakes = new List<Flake>();
        private readonly List<Print> prints = new List<Print>();
        private readonly Dictionary<GooseEntity, Feet> feet = new Dictionary<GooseEntity, Feet>();
        private readonly Random rng;
        private float nextDriftTime = -1f;
        private float bank;
        private float spawnBudget;
        private Bitmap[] sprites;

        /// <summary>It's winter: snow falls, drifts appear. When false everything gradually melts.</summary>
        public bool Active;

        public float Intensity { get; private set; }
        public int FlakeCount { get { return flakes.Count; } }
        public int PrintCount { get { return prints.Count; } }
        public float Bank { get { return bank; } }
        public IList<SnowDrift> Drifts { get { return drifts; } }
        public bool AnythingToDraw { get { return flakes.Count > 0 || drifts.Count > 0 || prints.Count > 0 || bank > 0.3f; } }

        public WinterScene(Random rng = null) { this.rng = rng ?? new Random(); }

        private float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

        private static int MaxFlakes(Vector2 screen)
        {
            return (int)M.Clamp(screen.x * screen.y / 11000f, 30f, 140f);
        }

        /// <summary>A snowdrift that is still whole, for the goose to aim at.</summary>
        public SnowDrift PickDrift()
        {
            List<SnowDrift> whole = drifts.FindAll(d => !d.Fading);
            return whole.Count == 0 ? null : whole[rng.Next(whole.Count)];
        }

        public SnowDrift AddDrift(Vector2 pos, float rx, float now)
        {
            SnowDrift d = new SnowDrift { pos = pos, rx = rx, ry = rx * 0.42f, born = now };
            drifts.Add(d);
            return d;
        }

        public void Update(float dt, float now, Vector2 screen, IList<GooseEntity> geese, ParticleSystem particles, float scale)
        {
            // snowfall comes in slow waves, with quiet spells in between
            float wave = 0.5f + 0.5f * (float)(Math.Sin(now / 53.0) * Math.Cos(now / 31.0 + 1.3));
            Intensity = Active ? M.Clamp01(wave * 1.35f - 0.12f) : 0f;
            UpdateFlakes(dt, now, screen);

            if (Active && Intensity > 0.2f) bank = Math.Min(MaxBank, bank + dt * MaxBank / BankGrowSeconds);
            else if (!Active) bank = Math.Max(0f, bank - dt * MaxBank / BankMeltSeconds);

            // drifts: a new one every 20-60 s, at most three; they melt when winter is over
            if (Active)
            {
                if (nextDriftTime < 0f) nextDriftTime = now + 5f;
                if (now > nextDriftTime)
                {
                    nextDriftTime = now + Rand(20f, 60f);
                    if (drifts.FindAll(d => !d.Fading).Count < MaxDrifts)
                    {
                        Vector2 p = new Vector2(Rand(0.12f, 0.88f) * screen.x, Rand(0.25f, 0.85f) * screen.y);
                        AddDrift(p, Rand(34f, 52f) * scale, now);
                    }
                }
            }
            else
            {
                nextDriftTime = -1f;
                foreach (SnowDrift d in drifts) if (!d.Fading && now > d.keepUntil) d.meltAt = now;
            }

            if (geese != null)
            {
                foreach (SnowDrift d in drifts)
                {
                    if (d.Fading || now - d.born < 0.6f) continue;
                    foreach (GooseEntity g in geese)
                    {
                        float dx = (g.position.x - d.pos.x) / (d.rx + 4f), dy = (g.position.y - d.pos.y) / (d.ry + 4f);
                        if (dx * dx + dy * dy > 1f) continue;
                        d.kickedAt = now;
                        if (particles != null) particles.SpawnSnowBurst(d.pos, g.velocity, (int)(26 + d.rx * 0.5f), scale, now);
                        break;
                    }
                }
                TrackFeet(geese, now, scale);
            }
            drifts.RemoveAll(d => (d.kickedAt >= 0f && now - d.kickedAt > 0.6f) || (d.meltAt >= 0f && now - d.meltAt > 4f));
            prints.RemoveAll(p => now - p.born > PrintLife);
        }

        private void UpdateFlakes(float dt, float now, Vector2 screen)
        {
            int target = (int)(MaxFlakes(screen) * Intensity);
            for (int i = flakes.Count - 1; i >= 0; i--)
            {
                Flake f = flakes[i];
                f.y += f.speed * dt;
                f.x += (float)Math.Sin(f.phase + now * 0.9f) * f.sway * dt;
                if (f.y > screen.y + 6f || f.x < -10f || f.x > screen.x + 10f)
                {
                    if (flakes.Count > target) { flakes.RemoveAt(i); continue; }
                    f.y = -Rand(4f, 30f);
                    f.x = Rand(0f, screen.x);
                }
                flakes[i] = f;
            }
            // new flakes enter from the top gradually, so snow starts falling rather than appearing
            spawnBudget += dt * Math.Max(4f, target / 4f);
            while (flakes.Count < target && spawnBudget >= 1f)
            {
                spawnBudget -= 1f;
                int sprite = rng.Next(3);
                flakes.Add(new Flake
                {
                    x = Rand(0f, screen.x),
                    y = -Rand(2f, 40f),
                    speed = Rand(22f, 40f) + sprite * 9f, // bigger flakes are closer, so they fall faster
                    phase = Rand(0f, 6.28f),
                    sway = Rand(6f, 16f),
                    sprite = sprite,
                });
            }
            if (flakes.Count >= target) spawnBudget = 0f;
        }

        private void TrackFeet(IList<GooseEntity> geese, float now, float scale)
        {
            foreach (GooseEntity g in geese)
            {
                if (g.rig == null || g.rig.feets == null) continue;
                Feet st;
                if (!feet.TryGetValue(g, out st)) { st = new Feet(); feet[g] = st; }
                ProceduralFeets f = g.rig.feets;
                // a foot has landed when its move finishes (start time goes from >0 back to -1)
                if (Active && st.l > 0f && f.lFootMoveTimeStart < 0f) AddPrint(f.lFootPos, g, now, scale);
                if (Active && st.r > 0f && f.rFootMoveTimeStart < 0f) AddPrint(f.rFootPos, g, now, scale);
                st.l = f.lFootMoveTimeStart;
                st.r = f.rFootMoveTimeStart;
            }
            if (feet.Count > geese.Count + 4)
            {
                List<GooseEntity> stale = new List<GooseEntity>();
                foreach (GooseEntity k in feet.Keys) if (!geese.Contains(k)) stale.Add(k);
                foreach (GooseEntity k in stale) feet.Remove(k);
            }
        }

        private void AddPrint(Vector2 foot, GooseEntity g, float now, float scale)
        {
            if (prints.Count >= MaxPrints) prints.RemoveAt(0);
            prints.Add(new Print { pos = g.position + (foot - g.position) * scale, dir = g.direction, born = now });
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Snow on the ground: the bank along the bottom, footprints and drifts (under the geese).</summary>
        public void DrawGround(Graphics g, float now, Vector2 screen, float scale)
        {
            if (bank > 0.3f)
            {
                List<PointF> pts = new List<PointF>();
                for (float x = 0f; x <= screen.x + 24f; x += 24f)
                {
                    float h = bank * (0.72f + 0.28f * (float)Math.Sin(x / 37f + 0.7f) * (float)Math.Cos(x / 91f));
                    pts.Add(new PointF(x, screen.y - h));
                }
                PointF[] edge = pts.ToArray();
                pts.Add(new PointF(screen.x + 24f, screen.y + 2f));
                pts.Add(new PointF(0f, screen.y + 2f));
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(245, 250, 252, 255)))
                    g.FillPolygon(fill, pts.ToArray());
                using (Pen rim = new Pen(Color.FromArgb(160, 190, 208, 232), 1.4f))
                    g.DrawCurve(rim, edge, 0.5f);
            }

            foreach (Print p in prints)
            {
                float a = 1f - M.Clamp01((now - p.born - PrintLife * 0.6f) / (PrintLife * 0.4f));
                if (a <= 0f) continue;
                Vector2 fwd = Vector2.GetFromAngleDegrees(p.dir);
                using (Pen pen = new Pen(M.WithAlpha(Color.FromArgb(150, 170, 200), a * 0.8f), 1.3f * scale))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    // a goose foot: three toes pointing where it walked
                    for (int t = -1; t <= 1; t++)
                    {
                        Vector2 toe = M.Rotate(fwd, t * 32f) * 3.6f * scale;
                        g.DrawLine(pen, p.pos.x, p.pos.y, p.pos.x + toe.x, p.pos.y + toe.y * 0.6f);
                    }
                }
            }

            foreach (SnowDrift d in drifts) DrawDrift(g, d, now);
        }

        private static void DrawDrift(Graphics g, SnowDrift d, float now)
        {
            float grow = EaseOutBack(M.Clamp01((now - d.born) / 1.1f));
            float fade = 1f;
            if (d.kickedAt >= 0f) fade = 1f - M.Clamp01((now - d.kickedAt) / 0.5f);
            else if (d.meltAt >= 0f) fade = 1f - M.Clamp01((now - d.meltAt) / 4f);
            float k = grow * fade;
            if (k <= 0.01f) return;
            float rx = d.rx * k, ry = d.ry * k;
            Vector2 c = d.pos;
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb((int)(55 * fade), 60, 80, 110)))
                g.FillEllipse(shadow, c.x - rx * 1.05f, c.y - ry * 0.55f, rx * 2.1f, ry * 1.25f);
            using (SolidBrush baseShade = new SolidBrush(Color.FromArgb(255, 214, 227, 244)))
            {
                g.FillEllipse(baseShade, c.x - rx, c.y - ry * 1.3f, rx * 2f, ry * 1.8f);
                g.FillEllipse(baseShade, c.x - rx * 0.9f, c.y - ry * 2.0f, rx * 1.1f, ry * 1.6f);
                g.FillEllipse(baseShade, c.x - rx * 0.1f, c.y - ry * 1.8f, rx * 1.0f, ry * 1.4f);
            }
            using (SolidBrush snow = new SolidBrush(Color.FromArgb(255, 250, 252, 255)))
            {
                g.FillEllipse(snow, c.x - rx * 0.92f, c.y - ry * 1.42f, rx * 1.75f, ry * 1.45f);
                g.FillEllipse(snow, c.x - rx * 0.82f, c.y - ry * 2.05f, rx * 0.95f, ry * 1.35f);
                g.FillEllipse(snow, c.x - rx * 0.05f, c.y - ry * 1.85f, rx * 0.85f, ry * 1.15f);
            }
            using (SolidBrush shine = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                g.FillEllipse(shine, c.x - rx * 0.55f, c.y - ry * 1.75f, rx * 0.45f, ry * 0.5f);
        }

        /// <summary>Falling snow, drawn over everything.</summary>
        public void DrawAir(Graphics g)
        {
            if (flakes.Count == 0) return;
            if (sprites == null) sprites = new[] { MakeFlake(2.2f, 150), MakeFlake(3.2f, 200), MakeFlake(4.4f, 235) };
            InterpolationMode oldMode = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            foreach (Flake f in flakes)
            {
                Bitmap s = sprites[f.sprite];
                g.DrawImage(s, (int)f.x - s.Width / 2, (int)f.y - s.Height / 2, s.Width, s.Height);
            }
            g.InterpolationMode = oldMode;
        }

        /// <summary>A soft white dot, pre-rendered once: blitting it is much cheaper than anti-aliased ellipses.</summary>
        private static Bitmap MakeFlake(float diameter, int alpha)
        {
            int size = (int)Math.Ceiling(diameter) + 2;
            Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float o = (size - diameter) / 2f;
                using (SolidBrush halo = new SolidBrush(Color.FromArgb(alpha / 3, 255, 255, 255)))
                    g.FillEllipse(halo, o - 0.6f, o - 0.6f, diameter + 1.2f, diameter + 1.2f);
                using (SolidBrush core = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                    g.FillEllipse(core, o, o, diameter, diameter);
            }
            return b;
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * (float)Math.Pow(x - 1f, 3) + c1 * (float)Math.Pow(x - 1f, 2);
        }
    }
}
