using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;

namespace GooseDeluxe
{
    /// <summary>
    /// A Russian voice of Windows (System.Speech / SAPI: on Russian Windows usually «Microsoft Irina Desktop»),
    /// made into the goose's voice by <see cref="VoiceFx.Goose"/>. Called from worker threads, one phrase at
    /// a time. Any failure (no Russian voice, no speech at all — as under Wine) leaves <see cref="Name"/>
    /// null and the goose talks its own honking way instead.
    /// </summary>
    internal static class WindowsVoice
    {
        public const int Rate = 22050;

        private static readonly object gate = new object();
        private static object synthesizer; // a SpeechSynthesizer; typed object so loading this class never needs System.Speech
        private static bool looked;
        private static bool male;

        /// <summary>The Russian voice in use, null if there is none.</summary>
        public static string Name { get; private set; }
        /// <summary>Why there is no voice (for the self-check).</summary>
        public static string Problem { get; private set; }
        /// <summary>Every voice Windows has, with its language.</summary>
        public static string All { get; private set; }

        /// <summary>Looks for a Russian voice (once). True if there is one.</summary>
        public static bool Find()
        {
            lock (gate)
            {
                if (looked) return Name != null;
                looked = true;
                try
                {
                    SpeechSynthesizer synth = new SpeechSynthesizer();
                    synthesizer = synth;
                    List<string> all = new List<string>();
                    InstalledVoice best = null;
                    foreach (InstalledVoice v in synth.GetInstalledVoices())
                    {
                        VoiceInfo i = v.VoiceInfo;
                        all.Add(i.Name + " (" + (i.Culture != null ? i.Culture.Name : "?") + ")");
                        if (!v.Enabled || i.Culture == null || !i.Culture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase)) continue;
                        // a man's voice needs less lowering, so it's the better start
                        if (best == null || (i.Gender == VoiceGender.Male && best.VoiceInfo.Gender != VoiceGender.Male)) best = v;
                    }
                    All = all.Count > 0 ? string.Join(", ", all.ToArray()) : "ни одного";
                    if (best == null)
                    {
                        Problem = "в Windows нет русского голоса (есть: " + All + ")";
                        return false;
                    }
                    synth.SelectVoice(best.VoiceInfo.Name);
                    male = best.VoiceInfo.Gender == VoiceGender.Male;
                    Name = best.VoiceInfo.Name;
                    return true;
                }
                catch (Exception ex)
                {
                    Problem = "синтез речи Windows не работает: " + ex.Message;
                    Deluxe.Log("Windows voice: " + ex);
                    return false;
                }
            }
        }

        /// <summary>The phrase in the goose's voice, written to <paramref name="wavPath"/>; null if there's
        /// no Russian voice.</summary>
        public static Utterance Say(string text, string wavPath)
        {
            if (!Find()) return null;
            float[] pcm;
            lock (gate)
            {
                SpeechSynthesizer synth = (SpeechSynthesizer)synthesizer;
                using (MemoryStream ms = new MemoryStream())
                {
                    // it speaks faster than normal: lowered by VoiceFx it comes out at an unhurried pace
                    synth.Rate = male ? 1 : 3;
                    synth.Volume = 100;
                    synth.SetOutputToAudioStream(ms, new SpeechAudioFormatInfo(Rate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                    try { synth.Speak(SpeechText.ForSynthesizer(text)); }
                    finally { synth.SetOutputToNull(); }
                    byte[] b = ms.ToArray();
                    pcm = new float[b.Length / 2];
                    for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)(b[2 * i] | (b[2 * i + 1] << 8)) / 32768f;
                }
            }
            float[] goose = VoiceFx.Goose(pcm, Rate, male ? 0.88 : 0.72);
            if (goose.Length < Rate / 10) return null;
            return Utterance.FromSamples(text, goose, Rate, wavPath, "голос Windows «" + ShortName(Name) + "», по-гусиному");
        }

        private static string ShortName(string name)
        {
            if (name == null) return "";
            return name.Replace("Microsoft ", "").Replace(" Desktop", "").Replace(" - Russian", "").Trim();
        }
    }
}
