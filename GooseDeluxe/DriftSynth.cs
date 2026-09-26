using System;
using System.IO;
using System.Text;

namespace GooseDeluxe
{
    /// <summary>
    /// The drift's sounds, made from scratch (nothing is copied from anywhere): a tyre squeal that loops,
    /// and four bars of drift phonk — a cowbell hook, a sliding 808, kick, clap, hats, pumping and a little room. Both come out as WAV
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

        public const int MusicRate = 44100;
        private const double Bpm = 130;

        // the riff: a two-bar cowbell hook in D minor, semitones from D5 (-99 = rest), played twice over the four bars
        private static readonly int[] Riff =
        {
            0,-99,0,0, -99,3,-99,0, -99,7,-99,5, 3,-99,2,-99,
            0,-99,0,0, -99,3,-99,0, -99,10,-99,7, 5,-99,3,2,
        };
        // the 808 underneath, one chord per bar: Dm, B♭, C, A — from D2
        private static readonly int[] BassRoots = { 0, -4, -2, -5 };

        /// <summary>Four bars of drift phonk at 130 BPM (about 7.4 s), seamless when looped.</summary>
        public static float[] Phonk()
        {
            int rate = MusicRate;
            double step = 60.0 / Bpm / 4;          // a 16th note
            int bar = (int)Math.Round(step * 16 * rate), loop = bar * 4;
            // render the four bars twice and keep the second pass: tails and echoes of the end run into the start
            int total = loop * 2 + rate;
            float[] drums = new float[total], bell = new float[total], bass = new float[total], wet = new float[total];
            float[] duck = new float[total];
            for (int i = 0; i < total; i++) duck[i] = 1f;
            Random rnd = new Random(20);

            for (int pass = 0; pass < 2; pass++)
                for (int b = 0; b < 4; b++)
                {
                    int barStart = pass * loop + b * bar;
                    for (int s = 0; s < 16; s++)
                    {
                        int at = barStart + (int)Math.Round(s * step * rate);
                        if (s == 0 || s == 10 || (b == 3 && s == 7)) { Kick(drums, at, rate); Duck(duck, at, rate); }
                        if (s == 4 || s == 12) Clap(drums, wet, at, rate, rnd);
                        if (s % 2 == 0) Hat(drums, at, rate, rnd, s % 4 == 2 ? 0.2f : 0.13f, 0.022);
                        if ((b == 1 || b == 3) && s >= 14) Hat(drums, at + (int)(step * rate / 2), rate, rnd, 0.12f, 0.018); // 32nd roll
                        if ((b == 0 || b == 2) && s == 6) Hat(drums, at, rate, rnd, 0.12f, 0.16);                         // open hat
                        int note = Riff[(b % 2) * 16 + s];
                        if (note != -99) Cowbell(bell, wet, at, rate, 587.33 * Semi(note));
                    }
                    // the 808: the bar's root, sliding into the next bar's root on the last 16th
                    double from = 73.42 * Semi(BassRoots[b]), to = 73.42 * Semi(BassRoots[(b + 1) % 4]);
                    Bass808(bass, barStart, bar, rate, from, to, step);
                }

            Reverb(wet, rate);
            float[] o = new float[loop];
            for (int i = 0; i < loop; i++)
            {
                int k = loop + i;
                double x = drums[k] + (bell[k] * 0.85 + bass[k]) * duck[k] + wet[k] * 0.32;
                o[i] = (float)Math.Tanh(x * 1.25); // glue: a soft limiter
            }
            return Normalize(o, 0.9f);
        }

        private static double Semi(int k) { return Math.Pow(2, k / 12.0); }

        /// <summary>The 808 cowbell: two band-limited square waves about a fifth apart (540/800 Hz on the
        /// original, pitched here), a sharp click and a ringing tail, some of it into the echo.</summary>
        private static void Cowbell(float[] dry, float[] wet, int at, int rate, double f)
        {
            int len = (int)(0.42 * rate);
            double f2 = f * 1.4815;
            Biquad bp = Biquad.BandPass(rate, f * 1.7, 1.3);
            for (int i = 0; i < len && at + i < dry.Length; i++)
            {
                double t = (double)i / rate;
                double sq = Square(f, t, rate) + Square(f2, t, rate);
                double body = bp.Process(sq) * 1.6 + sq * 0.18;
                double env = Math.Exp(-t / 0.011) * 0.6 + Math.Exp(-t / 0.13) * 0.4;
                double v = Math.Tanh(body * env * 1.4) * 0.42;
                dry[at + i] += (float)v;
                wet[at + i] += (float)(v * 0.5);
            }
        }

        /// <summary>A square wave built from its odd harmonics up to the Nyquist limit: no aliasing hiss.</summary>
        private static double Square(double f, double t, int rate)
        {
            double sum = 0;
            for (int k = 1; k * f < rate * 0.45; k += 2) sum += Math.Sin(2 * Math.PI * k * f * t) / k;
            return sum * 4 / Math.PI;
        }

        private static void Kick(float[] mix, int at, int rate)
        {
            int len = (int)(0.3 * rate);
            double phase = 0;
            for (int i = 0; i < len && at + i < mix.Length; i++)
            {
                double t = (double)i / rate;
                double f = 50 + 140 * Math.Exp(-t / 0.028);
                phase += 2 * Math.PI * f / rate;
                double click = t < 0.002 ? Math.Sin(Math.PI * t / 0.002) * 0.5 : 0; // a knock that starts from silence
                mix[at + i] += (float)((Math.Sin(phase) * Math.Exp(-t / 0.11) + click) * 0.75);
            }
        }

        /// <summary>Everything but the drums dips when the kick hits and swells back: the "pumping" of phonk.</summary>
        private static void Duck(float[] duck, int at, int rate)
        {
            int len = (int)(0.22 * rate);
            for (int i = 0; i < len && at + i < duck.Length; i++)
            {
                double t = (double)i / rate;
                duck[at + i] = (float)Math.Min(duck[at + i], 1 - 0.55 * (1 - Math.Exp(-t / 0.0015)) * Math.Exp(-t / 0.07)); // dips fast, not instantly: no click
            }
        }

        /// <summary>A long, distorted 808 on the root that slides into the next root at the end of the bar.</summary>
        private static void Bass808(float[] mix, int at, int len, int rate, double from, double to, double step)
        {
            double phase = 0;
            int slideAt = len - (int)(step * rate);
            for (int i = 0; i < len && at + i < mix.Length; i++)
            {
                double t = (double)i / rate;
                double f = from * (1 + 0.35 * Math.Exp(-t / 0.03));          // the punchy start
                if (i > slideAt)
                {
                    double k = (double)(i - slideAt) / (len - slideAt);
                    f = from * Math.Pow(to / from, k * k);                     // glide into the next note
                }
                phase += 2 * Math.PI * f / rate;
                double env = Math.Min(1, t / 0.004) * Math.Min(1, (len - i) / (0.004 * rate)) * (0.55 + 0.45 * Math.Exp(-t / 0.9)); // no click where the next 808 takes over
                // distortion adds the overtones that make an 808 audible on laptop speakers
                mix[at + i] += (float)(Math.Tanh(Math.Sin(phase) * 3.0) * env * 0.42);
            }
        }

        private static void Clap(float[] dry, float[] wet, int at, int rate, Random rnd)
        {
            int len = (int)(0.25 * rate);
            Biquad bp = Biquad.BandPass(rate, 1300, 0.7);
            for (int i = 0; i < len && at + i < dry.Length; i++)
            {
                double t = (double)i / rate;
                double env = (t < 0.03 ? Math.Exp(-(t % 0.01) / 0.003) : 0) + Math.Exp(-t / 0.08) * 0.75;
                double v = bp.Process(rnd.NextDouble() * 2 - 1) * Math.Min(1, t / 0.0004) * env * 0.85;
                dry[at + i] += (float)v;
                wet[at + i] += (float)(v * 0.6);
            }
        }

        private static void Hat(float[] mix, int at, int rate, Random rnd, float level, double decay)
        {
            int len = (int)(Math.Min(0.3, decay * 6) * rate);
            Biquad hp = Biquad.HighPass(rate, 7500, 0.7);
            for (int i = 0; i < len && at + i < mix.Length; i++)
            {
                double t = (double)i / rate;
                mix[at + i] += (float)(hp.Process(rnd.NextDouble() * 2 - 1) * Math.Min(1, t / 0.0004) * Math.Exp(-t / decay) * level);
            }
        }

        /// <summary>A small room (Schroeder: four combs, two all-passes), in place.</summary>
        internal static void Reverb(float[] x, int rate)
        {
            double[] combMs = { 29.7, 37.1, 41.1, 43.7 };
            float[] outp = new float[x.Length];
            foreach (double ms in combMs)
            {
                int d = (int)(ms * rate / 1000);
                float[] buf = new float[d];
                int p = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    float y = buf[p];
                    buf[p] = x[i] + y * 0.8f;
                    p = (p + 1) % d;
                    outp[i] += y * 0.25f;
                }
            }
            foreach (double ms in new[] { 5.0, 1.7 })
            {
                int d = (int)(ms * rate / 1000);
                float[] buf = new float[d];
                int p = 0;
                for (int i = 0; i < outp.Length; i++)
                {
                    float b = buf[p];
                    float y = -0.7f * outp[i] + b;
                    buf[p] = outp[i] + 0.7f * y;
                    p = (p + 1) % d;
                    outp[i] = y;
                }
            }
            Array.Copy(outp, x, x.Length);
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

        internal static float[] Normalize(float[] o, float peak)
        {
            float max = 1e-6f;
            foreach (float s in o) max = Math.Max(max, Math.Abs(s));
            for (int i = 0; i < o.Length; i++) o[i] *= peak / max;
            return o;
        }

        public static byte[] Wav(float[] samples) { return Wav(samples, Rate); }

        public static byte[] Wav(float[] samples, int rate)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                int data = samples.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data);
                foreach (float s in samples) w.Write((short)Math.Round(Math.Max(-1f, Math.Min(1f, s)) * 32767f));
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Writes the WAV unless an identical one is already there; returns its path.</summary>
        public static string Ensure(string dir, string name, Func<float[]> make, int rate = Rate)
        {
            string path = Path.Combine(dir, name);
            if (!File.Exists(path)) File.WriteAllBytes(path, Wav(make(), rate));
            return path;
        }

        internal sealed class Biquad
        {
            private double b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public static Biquad HighPass(int rate, double freq, double q)
            {
                double w = 2 * Math.PI * freq / rate, alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha, c = Math.Cos(w);
                return new Biquad { b0 = (1 + c) / 2 / a0, b1 = -(1 + c) / a0, b2 = (1 + c) / 2 / a0, a1 = -2 * c / a0, a2 = (1 - alpha) / a0 };
            }

            public static Biquad LowPass(int rate, double freq, double q)
            {
                double w = 2 * Math.PI * freq / rate, alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha, c = Math.Cos(w);
                return new Biquad { b0 = (1 - c) / 2 / a0, b1 = (1 - c) / a0, b2 = (1 - c) / 2 / a0, a1 = -2 * c / a0, a2 = (1 - alpha) / a0 };
            }

            /// <summary>A bell boost (or cut, for negative dB) around <paramref name="freq"/>.</summary>
            public static Biquad Peaking(int rate, double freq, double q, double db)
            {
                double A = Math.Pow(10, db / 40), w = 2 * Math.PI * freq / rate, alpha = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + alpha / A;
                return new Biquad { b0 = (1 + alpha * A) / a0, b1 = -2 * c / a0, b2 = (1 - alpha * A) / a0, a1 = -2 * c / a0, a2 = (1 - alpha / A) / a0 };
            }

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
