using System;
using System.Drawing;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>Small math helpers on top of SamEngine.Vector2.</summary>
    internal static class M
    {
        public const float Deg2Rad = (float)Math.PI / 180f;
        public const float Rad2Deg = 180f / (float)Math.PI;
        public static readonly Vector2 Up = new Vector2(0f, -1f);

        public static float Clamp(float v, float min, float max) { return Math.Min(Math.Max(v, min), max); }
        public static float Clamp01(float v) { return Clamp(v, 0f, 1f); }
        public static float Lerp(float a, float b, float p) { return a + (b - a) * p; }
        public static float Rand(float min, float max) { return SamMath.RandomRange(min, max); }
        public static int RandInt(int maxExclusive) { return SamMath.Rand.Next(maxExclusive); }

        /// <summary>Wraps an angle difference into [-180, 180).</summary>
        public static float WrapDeg(float a)
        {
            a = a % 360f;
            if (a >= 180f) a -= 360f;
            if (a < -180f) a += 360f;
            return a;
        }

        public static float AngleDeg(Vector2 v) { return (float)Math.Atan2(v.y, v.x) * Rad2Deg; }

        public static Vector2 Rotate(Vector2 v, float deg)
        {
            float c = (float)Math.Cos(deg * Deg2Rad), s = (float)Math.Sin(deg * Deg2Rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        public static PointF P(Vector2 v) { return new PointF(v.x, v.y); }

        public static float EaseInOut(float p) { p = Clamp01(p); return p * p * (3f - 2f * p); }

        public static Color Mix(Color a, Color b, float p)
        {
            p = Clamp01(p);
            return Color.FromArgb(
                (int)Lerp(a.A, b.A, p), (int)Lerp(a.R, b.R, p), (int)Lerp(a.G, b.G, p), (int)Lerp(a.B, b.B, p));
        }

        public static Color WithAlpha(Color c, float alpha01)
        {
            return Color.FromArgb((int)(255f * Clamp01(alpha01)), c.R, c.G, c.B);
        }
    }
}
