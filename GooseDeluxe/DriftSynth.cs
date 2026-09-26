using System;
using System.IO;
using System.Text;

namespace GooseDeluxe
{
    /// <summary>
    /// The drift's sounds, made from scratch (nothing is copied from anywhere): a tyre squeal that loops,
    /// and a short phonk-style beat — cowbell melody, 808 bass, kick, clap and hats. Both come out as WAV
    /// files the goose's own way of playing sounds (MCI) can loop.
    /// </summary>
    internal static class DriftSynth
    {
        public const int Rate = 22050;

        // ------------------------------------------------------------------ tyre squeal

        /// <summary>A 2-second loop: a wobbling whine around 1.9 kHz plus hiss. High-pitched on purpose, so it
        /// sits above the goose's footsteps instead of covering them.</summary>
        public static float[] Screech()
        {
            int n = Rate * 2, fade = Rate / 20;
            float[] o = new float[n + fade];
            Random rnd = new Random(7);
            Biquad hiss = Biquad.BandPass(Rate, 2600, 1.1);
            double phase = 0;
            for (int i = 0; i < o.Length; i++)
            {
                double t = (double)i / Rate;
                // every modulation makes whole cycles in 2 s, so the loop doesn't jump
                double f = 1850 + 220 * Math.Sin(2 * Math.PI * 5.5 * t) + 90 * Math.Sin(2 * Math.PI * 13 * t + 1.3);
                phase += 2 * Math.PI * f / Rate;
                double tone = Math.Sin(phase) + 0.35 * Math.Sin(2 * phase + 0.5) + 0.15 * Math.Sin(3 * phase);
                double noise = hiss.Process(rnd.NextDouble() * 2 - 1);
                double amp = 0.75 + 0.25 * Math.Sin(2 * Math.PI * 9 * t);
                o[i] = (float)((0.55 * tone + 1.6 * noise) * amp);
            }
            return Normalize(Loop(o, fade), 0.5f);
        }

        // ------------------------------------------------------------------ phonk

        private const double Bpm = 130;

        /// <summary>Two bars of a drift-phonk groove (about 3.7 s); played in a loop while the goose drifts.</summary>
        public static float[] Phonk()
        {
            double step = 60.0 / Bpm / 4; // a 16th note
            const int steps = 32;
            int n = (int)(Rate * step * steps);
            float[] mix = new float[n + Rate];

            int[] kick  = { 1,0,0,0, 0,0,0,1, 0,0,1,0, 0,0,0,0 };
            int[] clap  = { 0,0,0,0, 1,0,0,0, 0,0,0,0, 1,0,0,0 };
            int[] hat   = { 1,0,1,0, 1,0,1,1, 1,0,1,0, 1,1,1,0 };
            // cowbell: semitones from C#5, -99 = rest
            int[] bell  = { 0,-99,3,-99, 0,7,-99,5, 3,-99,0,-99, -2,-99,0,-99,
                            0,-99,3,-99, 0,7,-99,10, 8,-99,7,-99, 5,-99,3,-99 };
            int[] bassRoot = { 0, -4 }; // C# then A, one per bar

            Random rnd = new Random(11);
            for (int s = 0; s < steps; s++)
            {
                int at = (int)(s * step * Rate);
                int inBar = s % 16;
                if (kick[inBar] == 1)
                {
                    Kick(mix, at);
                    int next = s + 1;
                    while (next < steps && kick[next % 16] == 0) next++;
                    Bass(mix, at, (int)((next - s) * step * Rate), 69.30 * Semi(bassRoot[s / 16]));
                }
                if (clap[inBar] == 1) Clap(mix, at, rnd);
                if (hat[inBar] == 1) Hat(mix, at, rnd, inBar % 4 == 2 ? 0.55f : 0.35f);
                if (bell[s] != -99) Cowbell(mix, at, 554.37 * Semi(bell[s]));
            }
            // the tails of the last hits wrap round to the start, so the loop is seamless
            float[] o = new float[n];
            for (int i = 0; i < mix.Length; i++) o[i % n] += mix[i];
            // a bit of grit and a soft top, like an old tape
            double lp = 0;
            for (int i = 0; i < n; i++)
            {
                double x = Math.Tanh(o[i] * 1.6);
                lp += (x - lp) * 0.55;
                o[i] = (float)lp;
            }
            return Normalize(o, 0.85f);
        }

        private static double Semi(int k) { return Math.Pow(2, k / 12.0); }

        private static void Kick(float[] mix, int at)
        {
            int len = (int)(0.35 * Rate);
            double phase = 0;
            for (int i = 0; i < len && at + i < mix.Length; i++)
            {
                double t = (double)i / Rate;
                double f = 48 + 110 * Math.Exp(-t / 0.035);
                phase += 2 * Math.PI * f / Rate;
                mix[at + i] += (float)(Math.Sin(phase) * Math.Exp(-t / 0.16) * 0.9);
            }
        }

        private static void Bass(float[] mix, int at, int len, double freq)
        {
            len = Math.Min(len + (int)(0.05 * Rate), (int)(1.2 * Rate));
            double phase = 0;
            for (int i = 0; i < len && at + i < mix.Length; i++)
            {
                double t = (double)i / Rate;
                double f = freq * (1 + 0.5 * Math.Exp(-t / 0.02)); // a little 808 "boing" at the start
                phase += 2 * Math.PI * f / Rate;
                double env = Math.Min(1, t / 0.005) * Math.Exp(-t / 0.7) * Math.Min(1, (len - i) / (0.02 * Rate));
                mix[at + i] += (float)(Math.Tanh(Math.Sin(phase) * 2.5) * env * 0.45);
            }
        }

        private static void Clap(float[] mix, int at, Random rnd)
        {
            int len = (int)(0.22 * Rate);
            Biquad bp = Biquad.BandPass(Rate, 1500, 0.9);
            for (int i = 0; i < len && at + i < mix.Length; i++)
            {
                double t = (double)i / Rate;
                // three quick claps and a tail, like hands a few centimetres apart
                double env = Math.Exp(-(t % 0.011) / 0.004) * (t < 0.033 ? 1 : 0) + Math.Exp(-t / 0.09) * 0.8;
                mix[at + i] += (float)(bp.Process(rnd.NextDouble() * 2 - 1) * env * 0.75);
            }
        }

        private static void Hat(float[] mix, int at, Random rnd, float level)
        {
            int len = (int)(0.06 * Rate);
            double prev = 0;
            for (int i = 0; i < len && at + i < mix.Length; i++)
            {
                double t = (double)i / Rate;
                double w = rnd.NextDouble() * 2 - 1;
                double hp = w - prev; // crude high-pass: only the fizz is left
                prev = w;
                mix[at + i] += (float)(hp * Math.Exp(-t / 0.018) * level * 0.5);
            }
        }

        private static void Cowbell(float[] mix, int at, double freq)
        {
            // the 808 cowbell: two square waves a fifth-ish apart through a band-pass, dying fast
            int len = (int)(0.32 * Rate);
            Biquad bp = Biquad.BandPass(Rate, freq * 1.25, 2.5);
            double p1 = 0, p2 = 0, f2 = freq * 1.4829;
            for (int i = 0; i < len && at + i < mix.Length; i++)
            {
                double t = (double)i / Rate;
                p1 += freq / Rate; p2 += f2 / Rate;
                double sq = (p1 % 1 < 0.5 ? 1 : -1) + (p2 % 1 < 0.5 ? 1 : -1);
                double env = Math.Exp(-t / 0.035) * 0.6 + Math.Exp(-t / 0.16) * 0.4;
                mix[at + i] += (float)(bp.Process(sq) * env * 0.5);
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Blends the last <paramref name="fade"/> samples into the first ones and drops them: a seamless loop.</summary>
        private static float[] Loop(float[] o, int fade)
        {
            int n = o.Length - fade;
            float[] r = new float[n];
            Array.Copy(o, r, n);
            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;
                r[i] = r[i] * k + o[n + i] * (1 - k);
            }
            return r;
        }

        private static float[] Normalize(float[] o, float peak)
        {
            float max = 1e-6f;
            foreach (float s in o) max = Math.Max(max, Math.Abs(s));
            for (int i = 0; i < o.Length; i++) o[i] *= peak / max;
            return o;
        }

        public static byte[] Wav(float[] samples)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                int data = samples.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data);
                foreach (float s in samples) w.Write((short)Math.Round(Math.Max(-1f, Math.Min(1f, s)) * 32767f));
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Writes the WAV unless an identical one is already there; returns its path.</summary>
        public static string Ensure(string dir, string name, Func<float[]> make)
        {
            string path = Path.Combine(dir, name);
            if (!File.Exists(path)) File.WriteAllBytes(path, Wav(make()));
            return path;
        }

        private sealed class Biquad
        {
            private double b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public static Biquad BandPass(int rate, double freq, double q)
            {
                double w = 2 * Math.PI * freq / rate, alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
                return new Biquad { b0 = alpha / a0, b1 = 0, b2 = -alpha / a0, a1 = -2 * Math.Cos(w) / a0, a2 = (1 - alpha) / a0 };
            }

            public double Process(double x)
            {
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }
    }
}
