using System;
using System.Collections.Generic;

namespace GooseDeluxe
{
    /// <summary>
    /// The goose's own way of talking when there's no Russian voice in Windows (or it's chosen): every syllable
    /// of the phrase is a short honk, coloured by the syllable's vowel, with the consonant before it hissed,
    /// clicked or hummed, and the phrase's intonation — louder and higher on «!», rising on «?».
    /// The honk itself is modelled on the goose's recordings (Honk1-4.mp3): about 700 Hz, fundamental and
    /// 2nd harmonic equally strong, 3rd-5th about 5-6 dB lower, a rough lower buzz as it starts.
    /// </summary>
    internal static class GooseVoice
    {
        public const int Rate = 22050;
        public const string Name = "по-гусиному";

        private static readonly double[] HonkDb = { 0, 0, -5, -6, -6, -10, -12, -14, -17, -20, -22, -24, -26, -28 };

        private sealed class Syl
        {
            public char Vowel, Onset;
            public double Start, VowelStart, VowelEnd, Pitch, Loud;
            public int Contour;   // +1 rising, -1 falling
            public bool Rasp;
        }

        public static Utterance Say(string text, string wavPath)
        {
            double[] times;
            float[] samples = Render(text, out times);
            return Utterance.FromSamples(text, samples, Rate, wavPath, Name, times);
        }

        public static float[] Render(string text, out double[] wordTimes)
        {
            List<SpeechText.Word> words = SpeechText.Words(text ?? "");
            Random rnd = new Random(Seed(text));
            double mood = 0.94 + 0.12 * rnd.NextDouble();
            List<Syl> syl = new List<Syl>();
            wordTimes = new double[words.Count];
            double t = 0.05;
            int sentence = 0;
            for (int wi = 0; wi < words.Count; wi++)
            {
                SpeechText.Word w = words[wi];
                wordTimes[wi] = t;
                for (int k = 0; k < w.Syllables; k++)
                {
                    bool last = k == w.Syllables - 1;
                    Syl s = new Syl { Vowel = w.Vowels[k], Onset = w.Onsets[k], Start = t };
                    s.VowelStart = t + OnsetLength(s.Onset);
                    double len = 0.085 * (0.85 + 0.3 * rnd.NextDouble());
                    if (last && w.Mark != '\0') len *= 1.4;
                    s.VowelEnd = s.VowelStart + len;
                    s.Loud = 0.78 + 0.14 * rnd.NextDouble() + (k == 0 ? 0.08 : 0) + (w.Shout ? 0.15 : 0);
                    s.Pitch = 610 * mood * (0.95 + 0.1 * rnd.NextDouble()) * (k == 0 ? 1.04 : 1) * (w.Shout ? 1.08 : 1);
                    syl.Add(s);
                    t = s.VowelEnd + (last ? 0.05 + w.PauseAfter : 0.018);
                }
                if (w.Syllables == 0) t += w.PauseAfter;
                if ((w.Mark != '\0' || wi == words.Count - 1) && syl.Count > sentence)
                {
                    int a = sentence, b = syl.Count - 1;
                    for (int q = a; q <= b; q++) syl[q].Pitch *= 1.06 - 0.12 * (b > a ? (q - a) / (double)(b - a) : 0);
                    Syl end = syl[b];
                    if (w.Mark == '!') { end.Pitch *= 1.18; end.Loud *= 1.15; end.Rasp = true; if (b > a) syl[b - 1].Pitch *= 1.08; }
                    else if (w.Mark == '?') end.Contour = 1;
                    else if (w.Mark == '…') { end.Contour = -1; end.Loud *= 0.85; }
                    sentence = syl.Count;
                }
            }

            float[] o = new float[(int)((t + 0.12) * Rate)];
            foreach (Syl s in syl)
            {
                Consonant(o, s, rnd);
                Honk(o, s, rnd);
            }
            DriftSynth.Normalize(o, 0.85f);
            VoiceFx.Fade(o, Rate);
            return o;
        }

        private static int Seed(string text)
        {
            int h = 17;
            if (text != null) foreach (char c in text) h = h * 31 + c;
            return h;
        }

        private static int Kind(char c)
        {
            if ("птк".IndexOf(c) >= 0) return 1;
            if ("бдг".IndexOf(c) >= 0) return 2;
            if ("сзц".IndexOf(c) >= 0) return 3;
            if ("шжщч".IndexOf(c) >= 0) return 4;
            if ("фвх".IndexOf(c) >= 0) return 5;
            if ("мн".IndexOf(c) >= 0) return 6;
            if ("лрй".IndexOf(c) >= 0) return 7;
            switch (c) // Latin letters sound like their Russian look-alikes
            {
                case 'p': case 't': case 'k': case 'c': case 'q': return 1;
                case 'b': case 'd': case 'g': return 2;
                case 's': case 'z': case 'x': return 3;
                case 'j': return 4;
                case 'f': case 'v': case 'w': case 'h': return 5;
                case 'm': case 'n': return 6;
                case 'l': case 'r': return 7;
            }
            return 0;
        }

        private static double OnsetLength(char c)
        {
            switch (Kind(c))
            {
                case 1: return 0.03;
                case 2: return 0.028;
                case 3: return 0.05;
                case 4: return 0.055;
                case 5: return 0.04;
                case 6: return 0.035;
                case 7: return 0.012;
                default: return 0;
            }
        }

        private static void Consonant(float[] o, Syl s, Random rnd)
        {
            int kind = Kind(s.Onset);
            int at = (int)(s.Start * Rate), len = (int)((s.VowelStart - s.Start) * Rate);
            if (kind == 0 || len <= 0) return;
            char c = s.Onset;
            double freq, q, amp, closure = 0, voice = 0;
            switch (kind)
            {
                case 1: case 2:
                    freq = "пбp".IndexOf(c) >= 0 ? 900 : "тдtd".IndexOf(c) >= 0 ? 4000 : 1900;
                    q = 1.0; amp = kind == 1 ? 0.35 : 0.25; closure = 0.6; voice = kind == 2 ? 0.06 : 0;
                    break;
                case 3: freq = 6000; q = 1.2; amp = 0.22; closure = c == 'ц' ? 0.3 : 0; voice = c == 'з' ? 0.08 : 0; break;
                case 4: freq = c == 'щ' ? 3600 : 3000; q = 1.0; amp = 0.25; closure = c == 'ч' ? 0.3 : 0; voice = c == 'ж' ? 0.08 : 0; break;
                case 5: freq = c == 'х' ? 1300 : 1500; q = c == 'х' ? 1.5 : 0.7; amp = c == 'х' ? 0.14 : 0.1; voice = c == 'в' ? 0.1 : 0; break;
                case 6:
                    for (int i = 0; i < len && at + i < o.Length; i++)
                    {
                        double tt = i / (double)Rate, p = i / (double)len, ph = 2 * Math.PI * s.Pitch * 0.85 * tt;
                        double env = Math.Min(1, p / 0.2) * Math.Min(1, (1 - p) / 0.2 + 0.5);
                        o[at + i] += (float)((Math.Sin(ph) + 0.3 * Math.Sin(2 * ph)) * env * 0.25 * s.Loud);
                    }
                    return;
                default:
                    if (c == 'л' || c == 'l')
                        for (int i = 0; i < len && at + i < o.Length; i++)
                            o[at + i] += (float)(Math.Sin(2 * Math.PI * s.Pitch * 0.9 * i / Rate) * 0.12 * s.Loud * Math.Sin(Math.PI * i / len));
                    return;
            }
            DriftSynth.Biquad bp = DriftSynth.Biquad.BandPass(Rate, Math.Min(freq, Rate * 0.4), q);
            int quiet = (int)(len * closure);
            for (int i = 0; i < len && at + i < o.Length; i++)
            {
                double tt = i / (double)Rate, noise = bp.Process(rnd.NextDouble() * 2 - 1);
                double v = voice * Math.Sin(2 * Math.PI * s.Pitch * 0.5 * tt);
                if (i >= quiet)
                {
                    double p = (i - quiet) / (double)Math.Max(1, len - quiet);
                    double env = kind <= 2 ? Math.Exp(-p * 4) : Math.Min(1, p / 0.25) * Math.Min(1, (1 - p) / 0.25);
                    v += noise * env * amp * 2.2;
                }
                o[at + i] += (float)(v * s.Loud);
            }
        }

        /// <summary>The vowel: an honk-shaped tone, its harmonics shaped by the vowel's two formants.</summary>
        private static void Honk(float[] o, Syl s, Random rnd)
        {
            int at = (int)(s.VowelStart * Rate), len = (int)((s.VowelEnd - s.VowelStart) * Rate);
            if (len <= 0) return;
            double f1, f2;
            Formants(s.Vowel, out f1, out f2);
            double[] amp = new double[HonkDb.Length + 1];
            double sum = 0;
            for (int h = 1; h <= HonkDb.Length; h++)
            {
                double f = h * s.Pitch;
                if (f > Rate * 0.42) break;
                double db = HonkDb[h - 1] + 7 * Math.Exp(-Sq((f - f1) / 300)) + 7 * Math.Exp(-Sq((f - f2) / 420)) - 3;
                amp[h] = Math.Pow(10, db / 20);
                sum += amp[h];
            }
            for (int h = 1; h < amp.Length; h++) amp[h] /= Math.Max(1e-6, sum) / 1.4;

            double dur = len / (double)Rate, phase = 0, wobble = rnd.NextDouble() * 6.28;
            bool trill = s.Onset == 'р' || s.Onset == 'r';
            double attack = s.Onset == ' ' || Kind(s.Onset) == 7 ? 0.022 : 0.01;
            for (int i = 0; i < len && at + i < o.Length; i++)
            {
                double t = i / (double)Rate, p = t / dur;
                double f = s.Pitch * (0.92 + 0.08 * Math.Min(1, t / 0.03));   // the honk's quick rise
                f *= 1 - 0.06 * Math.Max(0, (p - 0.65) / 0.35);                 // and its drop at the end
                if (s.Contour > 0) f *= 1 + 0.3 * p * p;
                else if (s.Contour < 0) f *= 1 - 0.18 * p;
                f *= 1 + 0.012 * Math.Sin(2 * Math.PI * 7.5 * t + wobble);
                phase += 2 * Math.PI * f / Rate;
                double v = 0;
                for (int h = 1; h < amp.Length; h++) if (amp[h] > 0) v += amp[h] * Math.Sin(h * phase);
                // the goose's rasp: its honk starts on a rough, "period-doubled" buzz half an octave down
                double rasp = 0.35 * Math.Exp(-t / 0.03) + (s.Rasp ? 0.22 : 0);
                v += rasp * (0.55 * Math.Sin(0.5 * phase) + 0.4 * Math.Sin(1.5 * phase) + 0.25 * Math.Sin(2.5 * phase));
                double env = Math.Min(1, t / attack) * Math.Min(1, (dur - t) / 0.028);
                if (trill && t < 0.05) env *= 0.55 + 0.45 * Math.Cos(2 * Math.PI * 28 * t);
                o[at + i] += (float)(Math.Tanh(v * 1.4) * env * s.Loud * 0.5);
            }
        }

        private static void Formants(char v, out double f1, out double f2)
        {
            switch (v)
            {
                case 'а': f1 = 750; f2 = 1250; break;
                case 'о': f1 = 520; f2 = 900; break;
                case 'у': f1 = 330; f2 = 750; break;
                case 'э': f1 = 520; f2 = 1850; break;
                case 'и': f1 = 300; f2 = 2300; break;
                default: f1 = 360; f2 = 1550; break;
            }
        }

        private static double Sq(double x) { return x * x; }
    }
}
