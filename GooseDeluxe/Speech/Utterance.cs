using System;
using System.Collections.Generic;
using System.IO;

namespace GooseDeluxe
{
    /// <summary>
    /// A phrase ready to be said: its sound (a WAV file, or none — then only the bubble), how long it takes,
    /// when each word appears in the speech bubble and how wide the beak is open along the way.
    /// </summary>
    internal sealed class Utterance
    {
        public const double EnvStep = 0.02;

        public string Text = "";
        public string WavPath;              // null: nothing to play
        public double Duration;             // seconds from the start of the sound to its end
        public float[] Envelope = new float[0]; // beak opening every EnvStep seconds, 0..1
        public double[] WordTimes = new double[0];
        public int[] WordEnds = new int[0]; // from WordTimes[k] on the text up to WordEnds[k] is shown
        public string Voice = "";

        /// <summary>How many characters of the text are shown <paramref name="t"/> seconds in.</summary>
        public int RevealAt(double t)
        {
            if (t >= Duration) return Text.Length;
            int shown = 0;
            for (int k = 0; k < WordTimes.Length; k++)
            {
                if (t < WordTimes[k]) break;
                shown = WordEnds[k];
            }
            return shown;
        }

        /// <summary>0..1: how wide the beak is open <paramref name="t"/> seconds in.</summary>
        public float MouthAt(double t)
        {
            if (t < 0 || Envelope.Length == 0) return 0f;
            double p = t / EnvStep;
            int i = (int)p;
            if (i >= Envelope.Length - 1) return i == Envelope.Length - 1 ? Envelope[i] : 0f;
            float f = (float)(p - i);
            return Envelope[i] * (1 - f) + Envelope[i + 1] * f;
        }

        /// <summary>A sound that has been made (<paramref name="samples"/>): written to <paramref name="wavPath"/>
        /// (unless that is null), with the beak following its loudness. The words appear at
        /// <paramref name="wordTimes"/>, or, when unknown, spread over the part of the sound that isn't silence.</summary>
        public static Utterance FromSamples(string text, float[] samples, int rate, string wavPath, string voice, double[] wordTimes = null)
        {
            Utterance u = new Utterance { Text = text ?? "", Voice = voice, Duration = samples.Length / (double)rate };
            if (wavPath != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(wavPath));
                try { File.WriteAllBytes(wavPath, DriftSynth.Wav(samples, rate)); }
                catch (IOException) { if (!File.Exists(wavPath)) throw; } // playing right now: it's this very sound
                u.WavPath = wavPath;
            }

            int step = (int)(EnvStep * rate), frames = samples.Length / step + 1;
            float[] rms = new float[frames];
            float max = 1e-6f;
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                int from = f * step, to = Math.Min(samples.Length, from + step);
                for (int i = from; i < to; i++) sum += samples[i] * samples[i];
                rms[f] = to > from ? (float)Math.Sqrt(sum / (to - from)) : 0f;
                max = Math.Max(max, rms[f]);
            }
            u.Envelope = new float[frames];
            int first = -1, last = -1;
            for (int f = 0; f < frames; f++)
            {
                float v = rms[f] / max;
                u.Envelope[f] = M.Clamp01((v - 0.08f) / 0.55f);
                if (v > 0.12f) { if (first < 0) first = f; last = f; }
            }

            List<SpeechText.Word> words = SpeechText.Words(u.Text);
            if (wordTimes == null || wordTimes.Length != words.Count)
            {
                double start = first < 0 ? 0 : first * EnvStep, end = last < 0 ? u.Duration : (last + 1) * EnvStep;
                wordTimes = SpeechText.Schedule(words, Math.Max(0.1, end - start));
                for (int k = 0; k < wordTimes.Length; k++) wordTimes[k] += start;
            }
            u.SetWords(words, wordTimes);
            return u;
        }

        /// <summary>
        /// A ready recording. A WAV is read — so the beak follows it — and written again as a clean copy, evenly
        /// loud and without the silence around it (some tools write headers Windows won't play). Anything else
        /// (MP3…) plays as it is, for <paramref name="lengthOf"/> seconds, the beak moving with the syllables.
        /// </summary>
        public static Utterance FromRecording(string text, string path, string copyDir, Func<string, double> lengthOf)
        {
            string name = "запись «" + System.IO.Path.GetFileName(path) + "»";
            if (System.IO.Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                int rate;
                float[] s = VoiceFx.Trim(WavFile.Read(path, out rate), rate);
                if (s.Length < rate / 20) throw new InvalidDataException("в записи тишина: " + System.IO.Path.GetFileName(path));
                DriftSynth.Normalize(s, 0.9f);
                VoiceFx.Fade(s, rate);
                FileInfo fi = new FileInfo(path);
                string copy = System.IO.Path.Combine(copyDir, FileNameFor("rec", path + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks));
                return FromSamples(text, s, rate, copy, name);
            }
            double seconds = lengthOf != null ? lengthOf(path) : 0;
            Utterance u = Silent(text, name);
            if (seconds > 0.2)
            {
                double k = seconds / u.Duration;
                for (int i = 0; i < u.WordTimes.Length; i++) u.WordTimes[i] *= k;
                float[] env = new float[(int)(seconds / EnvStep) + 1];
                for (int i = 0; i < env.Length; i++) env[i] = u.Envelope[Math.Min(u.Envelope.Length - 1, (int)(i / k))];
                u.Envelope = env;
                u.Duration = seconds;
            }
            u.WavPath = path;
            return u;
        }

        /// <summary>No sound (muted, or no voice at all): the bubble types at a speaking pace and the beak
        /// moves with the syllables.</summary>
        public static Utterance Silent(string text, string voice)
        {
            Utterance u = new Utterance { Text = text ?? "", Voice = voice };
            List<SpeechText.Word> words = SpeechText.Words(u.Text);
            double seconds = 0;
            foreach (SpeechText.Word w in words) seconds += w.Syllables * 0.14 + 0.04 + w.PauseAfter;
            if (words.Count > 0) seconds -= words[words.Count - 1].PauseAfter;
            seconds = Math.Max(0.6, seconds);
            double[] times = SpeechText.Schedule(words, seconds);
            u.Duration = seconds + 0.1;
            u.Envelope = new float[(int)(u.Duration / EnvStep) + 1];
            for (int k = 0; k < words.Count; k++)
            {
                double end = k + 1 < words.Count ? times[k + 1] - words[k].PauseAfter : seconds; // Schedule's scale is 1 here
                int n = words[k].Syllables;
                for (int s = 0; s < n; s++)
                {
                    double a = times[k] + (end - times[k]) * s / n, b = times[k] + (end - times[k]) * (s + 0.8) / n;
                    for (int f = (int)(a / EnvStep); f <= (int)(b / EnvStep) && f < u.Envelope.Length; f++)
                        u.Envelope[f] = Math.Max(u.Envelope[f], (float)Math.Sin(Math.PI * M.Clamp01((float)((f * EnvStep - a) / Math.Max(0.01, b - a)))) * 0.8f);
                }
            }
            u.SetWords(words, times);
            return u;
        }

        /// <summary>A file name for a phrase's sound that stays the same from run to run (FNV-1a of the text).</summary>
        public static string FileNameFor(string voice, string text)
        {
            uint h = 2166136261;
            foreach (char c in text ?? "") { h ^= c; h *= 16777619; }
            return voice + "-" + h.ToString("x8") + ".wav";
        }

        private void SetWords(List<SpeechText.Word> words, double[] times)
        {
            WordTimes = times;
            WordEnds = new int[words.Count];
            for (int k = 0; k < words.Count; k++) WordEnds[k] = k + 1 < words.Count ? words[k + 1].Start : Text.Length;
        }
    }
}
