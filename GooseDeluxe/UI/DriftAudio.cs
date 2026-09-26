using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GooseDeluxe
{
    /// <summary>
    /// The drift's sound: a quiet tyre squeal while the goose drifts, and phonk. Played through MCI, the way the
    /// goose plays its honks and music — not through PlaySound/SoundPlayer, which plays one sound at a time and
    /// would cut off the goose's footsteps. The squeal stays well below the footsteps.
    /// The phonk is the user's own track from the "Фонк" folder next to the mod (DriftMusicFrom..DriftMusicTo
    /// seconds of it) or, if there is none, the beat from <see cref="DriftSynth"/>.
    /// Called on the goose's thread from the mod's timer (every 100 ms).
    /// </summary>
    internal sealed class DriftAudio : IDisposable
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
        private static extern int mciSendString(string command, StringBuilder result, int resultLength, IntPtr callback);

        private const string Squeal = "gdsqueal", Music = "gdphonk";
        private const int SquealMax = 220;  // of 1000: under the goose's footsteps
        private const int MusicMax = 450;
        private const double LingerSeconds = 1.5;
        private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aac" };

        private readonly string modDir;
        private bool squealOpen, squealPlaying, musicOpen, musicPlaying, userTrack;
        private int squealVolume = -1, musicVolume = -1, fromMs, toMs;
        private double lastDriftAt = -100;
        private float musicLevel;

        public string MusicSource { get; private set; }
        public string LastError { get; private set; }
        public bool Playing { get { return squealPlaying || musicPlaying; } }

        public DriftAudio(string modDir) { this.modDir = modDir; }

        public static string PhonkFolder(string modDir) { return Path.Combine(modDir, "Фонк"); }

        public void Update(float drift, bool soundOn, DeluxeConfig cfg, double now)
        {
            if (LastError != null) return; // one try: no sound device or no codec means silence until the next start
            try
            {
                UpdateSqueal(soundOn && cfg.DriftSound ? drift : 0f);
                UpdateMusic(drift, soundOn && cfg.DriftMusic, cfg, now);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Deluxe.Log("drift sound: " + ex.Message);
                Dispose();
            }
        }

        private void UpdateSqueal(float level)
        {
            if (level < 0.12f)
            {
                if (squealPlaying) { Mci("stop " + Squeal); squealPlaying = false; squealVolume = -1; }
                return;
            }
            if (!squealOpen)
            {
                string wav = DriftSynth.Ensure(modDir, "drift-squeal.wav", DriftSynth.Screech);
                Open(wav, Squeal);
                squealOpen = true;
            }
            SetVolume(Squeal, (int)(SquealMax * Math.Min(1f, level)), ref squealVolume);
            if (!squealPlaying) { Mci("play " + Squeal + " from 0 repeat"); squealPlaying = true; }
        }

        private void UpdateMusic(float drift, bool on, DeluxeConfig cfg, double now)
        {
            if (on && drift > 0.3f) lastDriftAt = now;
            bool want = on && now - lastDriftAt < LingerSeconds;
            if (want)
            {
                if (!musicOpen) OpenMusic(cfg);
                if (!musicPlaying) { Start(); musicPlaying = true; }
                else if (userTrack && Position() >= toMs - 60) Start(); // the chosen piece is over: again
                musicLevel = Math.Min(1f, musicLevel + 0.34f);           // fade in over ~0.3 s
            }
            else if (musicPlaying)
            {
                musicLevel -= 0.12f;                                     // fade out over ~0.8 s
                if (musicLevel <= 0f)
                {
                    // closed, not just stopped: a new track in the folder or new seconds apply next time
                    Mci("close " + Music);
                    musicOpen = musicPlaying = false;
                    musicLevel = 0f;
                    musicVolume = -1;
                    return;
                }
            }
            if (musicPlaying) SetVolume(Music, (int)(MusicMax * musicLevel), ref musicVolume);
        }

        private void OpenMusic(DeluxeConfig cfg)
        {
            string track = UserTrack();
            if (track != null)
            {
                Open(track, Music);
                Mci("set " + Music + " time format milliseconds");
                fromMs = (int)(cfg.DriftMusicFrom * 1000);
                toMs = (int)(cfg.DriftMusicTo * 1000);
                int length = Length();
                if (length > 0 && fromMs >= length) { fromMs = 0; toMs = Math.Min(length, (int)((cfg.DriftMusicTo - cfg.DriftMusicFrom) * 1000)); }
                if (length > 0) toMs = Math.Min(toMs, length);
                userTrack = true;
                MusicSource = Path.GetFileName(track) + ", " + (fromMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) +
                              "–" + (toMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " с";
            }
            else
            {
                Open(DriftSynth.Ensure(modDir, "drift-phonk.wav", DriftSynth.Phonk), Music);
                userTrack = false;
                MusicSource = "встроенный фонк-бит";
            }
            musicOpen = true;
        }

        private void Start()
        {
            if (userTrack) Mci("play " + Music + " from " + fromMs + " to " + toMs);
            else Mci("play " + Music + " from 0 repeat");
        }

        /// <summary>The first audio file in the Фонк folder, if the user put one there.</summary>
        public string UserTrack()
        {
            string dir = PhonkFolder(modDir);
            if (!Directory.Exists(dir)) return null;
            List<string> files = new List<string>();
            foreach (string f in Directory.GetFiles(dir))
                if (Array.IndexOf(AudioExtensions, Path.GetExtension(f).ToLowerInvariant()) >= 0) files.Add(f);
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files.Count > 0 ? files[0] : null;
        }

        private void Open(string path, string alias)
        {
            // mpegvideo (DirectShow) is what the goose uses and it can change the volume; a plain open by file
            // type is the fallback (a WAV then plays at its own, already quiet, level)
            int err = mciSendString("open \"" + path + "\" type mpegvideo alias " + alias, null, 0, IntPtr.Zero);
            if (err != 0) err = mciSendString("open \"" + path + "\" alias " + alias, null, 0, IntPtr.Zero);
            if (err != 0) throw new InvalidOperationException("не открылся звук " + Path.GetFileName(path) + " (MCI " + err + ")");
        }

        private int Position()
        {
            StringBuilder sb = new StringBuilder(32);
            int v;
            return mciSendString("status " + Music + " position", sb, sb.Capacity, IntPtr.Zero) == 0 && int.TryParse(sb.ToString(), out v) ? v : 0;
        }

        private int Length()
        {
            StringBuilder sb = new StringBuilder(32);
            int v;
            return mciSendString("status " + Music + " length", sb, sb.Capacity, IntPtr.Zero) == 0 && int.TryParse(sb.ToString(), out v) ? v : 0;
        }

        private static void SetVolume(string alias, int volume, ref int current)
        {
            if (Math.Abs(volume - current) < 8) return;
            mciSendString("setaudio " + alias + " volume to " + Math.Max(0, Math.Min(1000, volume)), null, 0, IntPtr.Zero);
            current = volume;
        }

        private static void Mci(string command) { mciSendString(command, null, 0, IntPtr.Zero); }

        public void Dispose()
        {
            if (squealOpen) { Mci("close " + Squeal); squealOpen = squealPlaying = false; }
            if (musicOpen) { Mci("close " + Music); musicOpen = musicPlaying = false; }
            squealVolume = musicVolume = -1;
        }
    }
}
