using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GooseDeluxe
{
    /// <summary>
    /// Plays the goose's phrases through MCI — like its honks, and unlike PlaySound/SoundPlayer, this doesn't cut
    /// off the goose's footsteps. Called on the goose's thread only.
    /// </summary>
    internal sealed class SpeechAudio : Speaker.IPlayer, IDisposable
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
        private static extern int mciSendString(string command, StringBuilder result, int resultLength, IntPtr callback);

        private const string Alias = "gdvoice";
        private bool open;

        public string LastError { get; private set; }

        public void Play(string wavPath, int volume)
        {
            Stop();
            int err = mciSendString("open \"" + wavPath + "\" type mpegvideo alias " + Alias, null, 0, IntPtr.Zero);
            bool canVolume = err == 0;
            if (err != 0) err = mciSendString("open \"" + wavPath + "\" alias " + Alias, null, 0, IntPtr.Zero);
            if (err != 0)
            {
                LastError = "не открылся звук " + Path.GetFileName(wavPath) + " (MCI " + err + ")";
                throw new InvalidOperationException(LastError);
            }
            open = true;
            if (canVolume) mciSendString("setaudio " + Alias + " volume to " + Math.Max(0, Math.Min(1000, volume)), null, 0, IntPtr.Zero);
            err = mciSendString("play " + Alias + " from 0", null, 0, IntPtr.Zero);
            if (err != 0) LastError = "звук не заиграл (MCI " + err + ")";
            else LastError = null;
        }

        public void Stop()
        {
            if (!open) return;
            mciSendString("close " + Alias, null, 0, IntPtr.Zero);
            open = false;
        }

        public void Dispose() { Stop(); }
    }
}
