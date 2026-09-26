using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// The goose saying a phrase: the sound is made in the background (a Windows voice takes a moment), then it
    /// plays while the words type themselves into a bubble over the goose's head and the beak moves with the
    /// voice. Everything but the background work runs on the goose's thread (<see cref="Update"/> from the
    /// mod's timer, <see cref="Draw"/> from the frame). Times are seconds on one steady clock.
    /// </summary>
    internal sealed class Speaker
    {
        public interface IPlayer
        {
            void Play(string wavPath, int volume);
            void Stop();
        }

        /// <summary>Makes the sound of a phrase in the given voice ("Atomic", "Goose" or "Off"). Runs on a
        /// worker thread; may throw (the phrase is then shown without sound).</summary>
        public delegate Utterance Builder(string text, string voice);

        public const double FadeIn = 0.15, FadeOut = 0.35, GiveUpAfter = 20;

        private readonly IPlayer player;
        private readonly Builder build;
        private readonly object gate = new object();
        private readonly Dictionary<string, Utterance> ready = new Dictionary<string, Utterance>();
        private readonly HashSet<string> making = new HashSet<string>();

        private string wantText, wantVoice;
        private double wantSince;
        private bool wantSound;
        private int wantVolume;
        private Utterance current;
        private double startedAt;
        private SpeechBubble bubble;

        public string LastError { get; private set; }
        public string LastVoice { get; private set; }
        public int Said { get; private set; }
        /// <summary>The goose is walking up to the cursor: hold the phrase until <see cref="Release"/>.</summary>
        public bool Held { get; private set; }

        public Speaker(IPlayer player, Builder build)
        {
            this.player = player;
            this.build = build;
        }

        /// <summary>Waiting for a phrase to be made, saying one, or still showing its bubble.</summary>
        public bool Busy { get { return wantText != null || current != null; } }

        /// <summary>Still to say or still saying (the bubble may linger on after this).</summary>
        public bool Talking(double now) { return wantText != null || (current != null && now < startedAt + current.Duration); }

        public string CurrentText { get { return current != null ? current.Text : wantText; } }

        /// <summary>Say <paramref name="text"/> as soon as its sound is ready (and, if <paramref name="hold"/>,
        /// the goose has arrived: <see cref="Release"/>).</summary>
        public void Say(string text, string voice, bool sound, int volume, bool hold, double now)
        {
            if (string.IsNullOrEmpty(text)) return;
            Stop();
            wantText = text;
            wantVoice = voice;
            wantSound = sound;
            wantVolume = volume;
            wantSince = now;
            Held = hold;
            Prepare(text, voice);
        }

        public void Release() { Held = false; }

        /// <summary>Makes a phrase's sound in the background, so it's ready when it's needed.</summary>
        public void Prepare(string text, string voice)
        {
            if (string.IsNullOrEmpty(text)) return;
            string key = voice + "|" + text;
            lock (gate)
            {
                if (ready.ContainsKey(key) || making.Contains(key)) return;
                making.Add(key);
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                // nothing may escape a worker thread: that would take the whole goose down
                Utterance u = null;
                try { u = build(text, voice); }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    Deluxe.Log("phrase voice failed: " + ex);
                }
                if (u == null)
                {
                    try { u = Utterance.Silent(text, "без голоса"); }
                    catch (Exception ex)
                    {
                        Deluxe.Log("phrase failed: " + ex);
                        u = new Utterance { Text = text, Voice = "без голоса", Duration = 3 };
                    }
                }
                lock (gate)
                {
                    making.Remove(key);
                    if (ready.Count >= 200)
                    {
                        // lots of «скажи …» from a phone: forget the others, but never the phrase about to be said
                        string waiting = wantVoice + "|" + wantText;
                        Utterance keep;
                        bool had = ready.TryGetValue(waiting, out keep);
                        ready.Clear();
                        if (had) ready[waiting] = keep;
                    }
                    ready[key] = u;
                }
            });
        }

        /// <summary>Starts a phrase that's ready, ends one that's over.</summary>
        public void Update(double now)
        {
            if (wantText != null && !Held)
            {
                Utterance u;
                lock (gate) ready.TryGetValue(wantVoice + "|" + wantText, out u);
                if (u != null) Start(u, now);
                else if (now - wantSince > GiveUpAfter) { Deluxe.Log("phrase not ready in time: " + wantText); wantText = null; }
            }
            if (current != null && now > startedAt + current.Duration + Linger(current) + FadeOut)
            {
                current = null;
                bubble = null;
            }
        }

        private void Start(Utterance u, double now)
        {
            wantText = null;
            current = u;
            startedAt = now;
            bubble = new SpeechBubble(u.Text);
            LastVoice = u.Voice;
            Said++;
            try
            {
                player.Stop();
                if (wantSound && u.WavPath != null) player.Play(u.WavPath, wantVolume);
            }
            catch (Exception ex) { LastError = ex.Message; Deluxe.Log("phrase sound failed: " + ex.Message); }
            Deluxe.Log("phrase (" + u.Voice + ", " + u.Duration.ToString("0.0") + " s): " + u.Text);
        }

        /// <summary>How long the whole phrase stays up after the voice stops: time to read it.</summary>
        public static double Linger(Utterance u) { return Math.Max(2.0, Math.Min(5.0, u.Text.Length * 0.035)); }

        public void Stop()
        {
            wantText = null;
            Held = false;
            if (current != null)
            {
                current = null;
                bubble = null;
                try { player.Stop(); } catch { }
            }
        }

        public float Mouth(double now) { return current == null ? 0f : current.MouthAt(now - startedAt); }

        public void Draw(Graphics g, Vector2 head, Vector2 feet, Vector2 screen, double now)
        {
            if (current == null || bubble == null) return;
            double t = now - startedAt, end = current.Duration + Linger(current);
            float alpha = (float)Math.Min(Math.Min(1.0, t / FadeIn), Math.Max(0.0, 1.0 - (t - end) / FadeOut));
            bubble.Draw(g, head, feet, screen, current.RevealAt(t), alpha);
        }
    }
}
