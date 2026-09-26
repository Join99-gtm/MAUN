using System;

namespace GooseDeluxe
{
    /// <summary>
    /// Turns a plain synthesized voice into the goose's: lower (a big, lazy, cocky bird rather than a
    /// polite announcer — the talking goose of Atomic Heart is the idea), a honky nasal bump, a little grit
    /// and a small room, like a voice from a cheap loudspeaker. Works on any mono 16-bit-range samples.
    /// </summary>
    internal static class VoiceFx
    {
        /// <param name="shift">How much lower: 0.72 plays the voice at 72% speed, so its pitch and its formants
        /// drop by about a quarter (the synthesizer is asked to speak faster to make up for the tempo).</param>
        public static float[] Goose(float[] voice, int rate, double shift)
        {
            float[] x = Trim(voice, rate);
            if (x.Length == 0) return x;

            // 1. lower: read the voice slower (cubic interpolation between samples)
            shift = Math.Max(0.4, Math.Min(1.5, shift));
            int n = (int)((x.Length - 1) / shift);
            float[] y = new float[n];
            for (int i = 0; i < n; i++) y[i] = Sample(x, i * shift);

            // 2. tone: no rumble, a bit of chest, the honky nose around 1.1 kHz, presence, a small speaker's top end
            DriftSynth.Biquad hp = DriftSynth.Biquad.HighPass(rate, 85, 0.7),
                              chest = DriftSynth.Biquad.Peaking(rate, 190, 0.9, 2.5),
                              nose = DriftSynth.Biquad.Peaking(rate, 1150, 1.2, 5.5),
                              edge = DriftSynth.Biquad.Peaking(rate, 2800, 1.4, 2.5),
                              lp = DriftSynth.Biquad.LowPass(rate, Math.Min(5500, rate * 0.45), 0.7);
            for (int i = 0; i < n; i++) y[i] = (float)lp.Process(edge.Process(nose.Process(chest.Process(hp.Process(y[i])))));
            DriftSynth.Normalize(y, 0.85f);

            // 3. grit: a soft overdrive blended in
            double k = Math.Tanh(2.6);
            for (int i = 0; i < n; i++) y[i] = (float)(0.6 * y[i] + 0.4 * Math.Tanh(2.6 * y[i]) / k);

            // 4. a small room
            float[] wet = new float[n];
            for (int i = 0; i < n; i++) wet[i] = y[i] * 0.5f;
            DriftSynth.Reverb(wet, rate);
            for (int i = 0; i < n; i++) y[i] += wet[i] * 0.1f;

            DriftSynth.Normalize(y, 0.9f);
            Fade(y, rate);
            return y;
        }

        /// <summary>Drops the silence before and after the speech (keeps 30 ms of it).</summary>
        public static float[] Trim(float[] x, int rate)
        {
            float peak = 0f;
            foreach (float v in x) peak = Math.Max(peak, Math.Abs(v));
            if (peak <= 0f) return new float[0];
            float gate = peak * 0.02f;
            int a = 0, b = x.Length - 1;
            while (a < x.Length && Math.Abs(x[a]) < gate) a++;
            while (b > a && Math.Abs(x[b]) < gate) b--;
            int margin = (int)(0.03 * rate);
            a = Math.Max(0, a - margin);
            b = Math.Min(x.Length - 1, b + margin);
            float[] r = new float[b - a + 1];
            Array.Copy(x, a, r, 0, r.Length);
            return r;
        }

        private static float Sample(float[] x, double p)
        {
            int i = (int)p;
            double f = p - i;
            double xm1 = At(x, i - 1), x0 = At(x, i), x1 = At(x, i + 1), x2 = At(x, i + 2);
            double c1 = 0.5 * (x1 - xm1), c2 = xm1 - 2.5 * x0 + 2 * x1 - 0.5 * x2, c3 = 0.5 * (x2 - xm1) + 1.5 * (x0 - x1);
            return (float)(((c3 * f + c2) * f + c1) * f + x0);
        }

        private static double At(float[] x, int i) { return i < 0 || i >= x.Length ? 0 : x[i]; }

        internal static void Fade(float[] y, int rate)
        {
            int f = Math.Min(y.Length / 2, (int)(0.008 * rate));
            for (int i = 0; i < f; i++)
            {
                float g = i / (float)f;
                y[i] *= g;
                y[y.Length - 1 - i] *= g;
            }
        }
    }
}
