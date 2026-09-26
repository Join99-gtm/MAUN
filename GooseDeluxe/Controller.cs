using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// Wires everything together inside the goose process: our renderer, the speed and randomness fixes,
    /// pausing (manual and for fullscreen apps), the tray icon and hotkeys, goose mail and visiting geese.
    /// Every entry point is guarded: if something here throws, the goose keeps running and the
    /// failure is logged to GooseDeluxe.log.
    /// </summary>
    internal sealed class Controller : IGooseControls
    {
        public const string Version = ModInfo.Version;

        private const int WM_HOTKEY_COME = 1, WM_HOTKEY_HONK = 2, WM_HOTKEY_PAUSE = 3, WM_HOTKEY_MENU = 4;
        private static readonly char[] HotkeyLetters = { 'G', 'H', 'P', 'M' };
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        private DeluxeConfig cfg;
        private OverlayWindow overlay;
        private ParticleSystem particles;
        private GooseAnimator animator;
        private GooseRenderer renderer;
        private FixedTimestep timestep;
        private DeckFixer deckFixer;
        private Guests guests;
        private Tray tray;
        private WinterScene winter;
        private AutumnControl autumn;
        private bool leafClicksBroken;
        private int leafClicks;
        private readonly GooseForms forms = new GooseForms();
        private int russianMemes;
        private DriftAudio driftAudio;
        private readonly RandomChase randomChase = new RandomChase();
        private readonly RandomChase randomPhrase = new RandomChase();
        private readonly Random rng = new Random();
        private PhraseBook phrases;
        private Recordings recordings;
        private Speaker speaker;
        private SpeechAudio speechAudio;
        private string windowsVoiceError;
        private string recordingError;
        private float escProgress;
        private bool leftDown;
        private Vector2 leftDownAt;
        private double lastOverlayClickAt = -1;
        private static FieldInfo gooseEscCounter;
        private static bool gooseEscCounterLooked;
        private static readonly Font EscFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        private static readonly Brush LeafHitBrush = new SolidBrush(Color.FromArgb(1, 0, 0, 0));
        private Season season = Season.None;
        private bool newYear;
        private double nextSnowdriftRun = -1;
        private System.Windows.Forms.Timer timer;
        private GooseEntity.RenderFunction originalRender;
        private GooseEntity.TickFunction originalTick;
        private readonly List<int> hotkeys = new List<int>();
        private readonly Dictionary<char, string> hotkeyState = new Dictionary<char, string>();
        private HotkeyPoller poller;
        private string lastHotkey = "";
        private double lastHotkeyAt = -1, lastRightClickAt = -1;
        private string trayError, hotkeyError;
        private int framesCounted, ticksCounted;
        private double rateWindowStart;
        private float fps, ticksPerSecond;
        private double welcomeAt = -1, selfTestSentAt = -1, selfTestBackAt = -1;
        private string IniPath { get { return Path.Combine(Deluxe.ModDir, "GooseDeluxe.ini"); } }

        private bool hooked, failed;
        private float lastRenderTime = -1f;
        private int timerTicks;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double fullscreenSince = -1, lastHonkIn = -100, lastStealIn = -100, lastDispatch = -100, escSince = -1;
        private bool toldAboutMailWhileAsleep;

        // ---------------------------------------------------------------- start-up

        public void Init(IMod mod)
        {
            try
            {
                Deluxe.ModDir = API.Helper != null && API.Helper.getModDirectory != null
                    ? API.Helper.getModDirectory(mod)
                    : Path.GetDirectoryName(typeof(Controller).Assembly.Location);
            }
            catch { Deluxe.ModDir = Path.GetDirectoryName(typeof(Controller).Assembly.Location); }
            try
            {
                Assembly exe = Assembly.GetEntryAssembly();
                Deluxe.GooseDir = Path.GetDirectoryName(exe != null ? exe.Location : Application.ExecutablePath);
            }
            catch { }
            try { cfg = DeluxeConfig.Load(Path.Combine(Deluxe.ModDir, "GooseDeluxe.ini")); }
            catch (Exception ex) { cfg = new DeluxeConfig(); Deluxe.Log("Config load failed, using defaults: " + ex.Message); }
            Deluxe.Cfg = cfg;
            Deluxe.Log("GooseDeluxe " + Version + " starting");
            Deluxe.Status("started", "pid " + Process.GetCurrentProcess().Id + ", " + Deluxe.GooseDir);

            particles = new ParticleSystem();
            animator = new GooseAnimator(cfg, particles);
            renderer = new GooseRenderer(cfg);
            Deluxe.Particles = particles;
            Deluxe.Animator = animator;

            try { RussianPack.Apply(Deluxe.GooseDir, cfg.RussianNotes); }
            catch (Exception ex) { Deluxe.Log("Russian pack: " + ex.Message); }
            // redrawing the memes takes a few seconds the first time: not on the goose's thread while it starts
            bool memesInRussian = cfg.RussianMemes;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { russianMemes = MemeTranslator.Apply(Deluxe.GooseDir, memesInRussian); }
                catch (Exception ex) { Deluxe.Log("Russian memes: " + ex.Message); }
            });

            winter = new WinterScene();
            Deluxe.Winter = winter;

            InjectionPoints.PreTickEvent += OnPreTick;
            InjectionPoints.PostTickEvent += OnPostTick;
            InjectionPoints.PreRenderEvent += OnPreRender;
            InjectionPoints.PostRenderEvent += OnPostRender;
        }

        /// <summary>First frame: the goose exists and we're on its UI thread with a running message loop.</summary>
        private void Hook(GooseEntity goose)
        {
            Deluxe.Goose = goose;
            Engine.Init();
            GooseSettings.Init();
            Deluxe.ScreenSize = () =>
            {
                Form f = Engine.MainForm;
                return f != null ? new Vector2(f.ClientSize.Width, f.ClientSize.Height) : new Vector2(1280f, 720f);
            };

            overlay = new OverlayWindow(MainWindowBounds());
            overlay.Show();
            overlay.KeepOnTop();
            originalRender = goose.render;
            goose.render = NoRender;

            originalTick = goose.tick;
            timestep = new FixedTimestep(originalTick) { Enabled = cfg.FixSpeed };
            goose.tick = timestep.Tick;

            if (cfg.HonestRandom)
            {
                Func<Deck> deck = DeckFixer.FromGooseDatabase();
                if (deck != null) deckFixer = new DeckFixer(deck);
                else Deluxe.Log("Task deck not found; random order left as is");
            }

            guests = new Guests(() => originalTick, () => goose.updateRig, () => new GooseAnimator(cfg, particles) { SilentStart = true });
            Deluxe.IsGuest = e => guests.IsGuest(e);
            Deluxe.AnimatorFor = e => ReferenceEquals(e, Deluxe.Goose) ? animator : guests.AnimatorOf(e);
            Deluxe.NoteWindowFactory = p => new NoteForm(p, string.IsNullOrEmpty(p.FromCode) ? null : (Action<DeliveryPayload>)ReplyTo);

            timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (s, e) => Guard("timer", OnTimer);
            timer.Start();

            if (cfg.Tray)
            {
                try
                {
                    tray = new Tray(this, AppIcon());
                    Deluxe.Notify = (t, m) => tray.Balloon(t, m);
                }
                catch (Exception ex) { trayError = ex.Message; Deluxe.Log("tray failed: " + ex); }
            }
            Guard("seasons", () =>
            {
                autumn = AutumnControl.Find();
                UpdateSeason();
            });
            if (cfg.Hotkeys)
            {
                try { RegisterHotkeys(); }
                catch (Exception ex) { hotkeyError = ex.Message; Deluxe.Log("hotkeys failed: " + ex); }
                // backup that works even when another program owns the combination
                poller = new HotkeyPoller(vk => (GetAsyncKeyState(vk) & 0x8001) != 0, () => clock.Elapsed.TotalSeconds);
            }
            overlay.GooseRightClicked += () => Guard("right click", ShowGooseMenu);
            overlay.LeftClicked += p => Guard("left click", () => OnLeftClick(p));
            Guard("drift", () =>
            {
                driftAudio = new DriftAudio(Deluxe.ModDir);
                string phonk = DriftAudio.PhonkFolder(Deluxe.ModDir);
                if (!Directory.Exists(phonk))
                {
                    Directory.CreateDirectory(phonk);
                    File.WriteAllText(Path.Combine(phonk, "Как добавить свой трек.txt"),
                        "Положи сюда свой трек (mp3, wav, wma, m4a) — когда гусь дрифтует, он будет играть кусок\r\n" +
                        "с DriftMusicFrom по DriftMusicTo секунду (настройки в GooseDeluxe.ini, по умолчанию 20–25).\r\n" +
                        "Если тут нет трека, играет встроенный фонк-бит.\r\n", new System.Text.UTF8Encoding(true));
                }
            });
            Guard("phrases", () =>
            {
                phrases = new PhraseBook(Deluxe.ModDir);
                recordings = new Recordings(VoiceFolder);
                EnsureVoiceFolder();
                ThreadPool.QueueUserWorkItem(_ => CleanVoiceFiles());
                speechAudio = new SpeechAudio();
                speaker = new Speaker(speechAudio, BuildUtterance);
                SayTask.Arrived = () => { if (speaker != null) speaker.Release(); };
                SayTask.StillTalking = () => speaker != null && speaker.Talking(clock.Elapsed.TotalSeconds);
                PrepareNext(); // the first phrase is ready at once
                Deluxe.Log("phrases: " + phrases.Count + " in " + phrases.Path + "; recordings: " + recordings.Files);
            });
            Guard("what's new", ShowWhatsNewOnce);
            if (cfg.Friends)
            {
                Guard("friends", () =>
                {
                    FriendService f = new FriendService(cfg.NtfyServer, Path.Combine(Deluxe.ModDir, "GooseFriend.ini"));
                    f.MyColors = GooseSettings.Colors;
                    f.MyHat = () => cfg.Hat;
                    Deluxe.Friends = f;
                    f.Start();
                });
            }
            Application.ApplicationExit += (s, e) => Cleanup();
            hooked = true;
            string summary = "engine=" + Engine.Available + " settings=" + GooseSettings.Available + " deck=" + (deckFixer != null) +
                             " season=" + season + " autumnMod=" + (autumn != null) + " tray=" + (tray != null) +
                             " friends=" + (Deluxe.Friends != null ? GooseCode.Display(Deluxe.Friends.MyCode) : "off");
            Deluxe.Log("Hooked. " + summary);
            Deluxe.Status("hooked", summary);
        }

        // ---------------------------------------------------------------- goose frame

        private void OnPreTick(GooseEntity goose)
        {
            if (failed) return;
            if (!hooked)
            {
                try { Hook(goose); }
                catch (Exception ex) { Fail(goose, ex); return; }
            }
            if (deckFixer != null)
            {
                try { deckFixer.Update(); }
                catch (Exception ex) { Deluxe.Log("Deck fix disabled: " + ex.Message); deckFixer = null; }
            }
            if (Deluxe.Carrying != CarryKind.None && !Deluxe.IsCurrentTask(CarryTask.Id)) Deluxe.Carrying = CarryKind.None;
        }

        /// <summary>Outside autumn the Autumn mod's leaf piles are removed as soon as it makes them.</summary>
        private void SuppressLeaves()
        {
            if (autumn == null || (cfg.LeafPiles && (season == Season.Autumn || season == Season.None))) return;
            try { autumn.Suppress(); }
            catch (Exception ex) { Deluxe.Log("Leaf control disabled: " + ex.Message); autumn = null; }
        }

        // runs before the Autumn mod draws its piles, whichever mod was loaded first
        private void OnPreRender(GooseEntity goose, Graphics unused)
        {
            if (hooked && !failed)
            {
                SuppressLeaves();
                HoldGooseEscCounter();
            }
        }

        /// <summary>
        /// The goose quits after ESC is held for about 8 s, with an English banner ("Continue holding ESC to
        /// evict goose"). We quit after cfg.EscHoldSeconds with a Russian one, so its counter is kept at zero.
        /// </summary>
        private static void HoldGooseEscCounter()
        {
            if (!gooseEscCounterLooked)
            {
                gooseEscCounterLooked = true;
                try
                {
                    Assembly exe = Assembly.GetEntryAssembly();
                    Type t = exe == null ? null : exe.GetType("GooseDesktop.Refactor.EscToQuitOverlay", false);
                    gooseEscCounter = t == null ? null : t.GetField("curQuitAlpha", BindingFlags.NonPublic | BindingFlags.Static);
                    if (gooseEscCounter == null) Deluxe.Log("the goose's ESC counter not found; its own ESC countdown stays");
                }
                catch (Exception ex) { Deluxe.Log("ESC counter: " + ex.Message); }
            }
            if (gooseEscCounter == null) return;
            try { gooseEscCounter.SetValue(null, 0f); }
            catch { gooseEscCounter = null; }
        }

        /// <summary>ESC held long enough to send the goose away? Never while something runs fullscreen: games use ESC.</summary>
        private bool EscHeldLongEnough()
        {
            if (Deluxe.HiddenForFullscreen) { escSince = -1; escProgress = 0f; return false; }
            double now = clock.Elapsed.TotalSeconds;
            if ((GetAsyncKeyState(0x1B) & 0x8000) != 0)
            {
                if (escSince < 0) escSince = now;
                escProgress = (float)((now - escSince) / Math.Max(0.3f, cfg.EscHoldSeconds));
                return escProgress >= 1f;
            }
            escSince = -1;
            escProgress = 0f;
            return false;
        }

        private static void DrawEscBar(Graphics g, float progress)
        {
            const string text = "Держи ESC — гусь уходит…";
            SizeF size = g.MeasureString(text, EscFont);
            Rectangle box = new Rectangle(10, 10, (int)size.Width + 24, (int)size.Height + 14);
            using (Brush back = new SolidBrush(Color.FromArgb(235, 173, 216, 230))) g.FillRectangle(back, box);
            using (Brush fill = new SolidBrush(Color.FromArgb(235, 255, 182, 193)))
                g.FillRectangle(fill, box.X, box.Y, (int)(box.Width * Math.Min(1f, progress)), box.Height);
            g.DrawString(text, EscFont, Brushes.Black, box.X + 12, box.Y + 7);
        }

        private void OnPostTick(GooseEntity goose)
        {
            if (hooked && !failed)
            {
                SuppressLeaves();
                // a meme / Not-epad the goose has just made (it shows it later): next one from the no-repeat deck
                try { forms.Update(goose.currentTaskData, cfg, Deluxe.GooseDir); }
                catch (Exception ex) { Deluxe.Log("meme/note swap failed: " + ex.Message); }
            }
            if (!hooked || failed || guests == null) return;
            try { guests.Update(timestep != null ? timestep.LastSteps : 1); }
            catch (Exception ex) { Deluxe.Log("Visitors removed after an error: " + ex); guests.Clear(); }
        }

        private void OnPostRender(GooseEntity goose, Graphics unused)
        {
            if (!hooked || failed) return;
            if (EscHeldLongEnough()) { Exit(); return; }
            PollPileClick();
            try
            {
                float now = Time.time;
                float dt = lastRenderTime < 0f ? 1f / 60f : M.Clamp(now - lastRenderTime, 0f, 0.1f);
                lastRenderTime = now;
                DrawFrame(dt, now);
                framesCounted++;
                if (timestep != null) ticksCounted += timestep.LastSteps;
                double t = clock.Elapsed.TotalSeconds;
                if (t - rateWindowStart >= 1.0)
                {
                    fps = (float)(framesCounted / (t - rateWindowStart));
                    ticksPerSecond = (float)(ticksCounted / (t - rateWindowStart));
                    framesCounted = ticksCounted = 0;
                    rateWindowStart = t;
                }
            }
            catch (Exception ex) { Fail(goose, ex); }
        }

        private void DrawFrame(float dt, float now)
        {
            overlay.ResizeTo(MainWindowBounds());
            using (Graphics g = Graphics.FromImage(overlay.Surface))
            {
                g.Clear(Color.Transparent);
                if (!Deluxe.HiddenForFullscreen)
                {
                    renderer.Prepare(g);
                    if (LeafClicksOn) DrawLeafHitAreas(g);
                    List<KeyValuePair<float, Action>> draws = new List<KeyValuePair<float, Action>>();
                    Vector2 screen = Deluxe.ScreenSize();
                    bool scarf = cfg.WinterScarf && season == Season.Winter;

                    GooseEntity me = Deluxe.Goose;
                    List<GooseEntity> onScreen = new List<GooseEntity> { me };
                    foreach (Guest guest in guests.All) if (guest.GoneAt < 0f) onScreen.Add(guest.Entity);
                    winter.Update(dt, now, screen, onScreen, particles, cfg.Scale);
                    if (winter.AnythingToDraw) winter.DrawGround(g, now, screen, cfg.Scale);

                    animator.Asleep = Deluxe.Sleeping;
                    animator.TalkOpen = speaker != null ? speaker.Mouth(clock.Elapsed.TotalSeconds) : 0f;
                    animator.Talking = speaker != null && speaker.Talking(clock.Elapsed.TotalSeconds);
                    GoosePose mine = animator.Update(me, dt, now);
                    mine.carry = Deluxe.Carrying;
                    HatStyle myHat = cfg.Hat != HatStyle.None ? cfg.Hat : (newYear ? HatStyle.Santa : HatStyle.None);
                    draws.Add(new KeyValuePair<float, Action>(me.position.y, () => renderer.Draw(g, mine, me, now, myHat, null, scarf)));

                    foreach (Guest guest in guests.All)
                    {
                        Guest v = guest;
                        if (v.GoneAt >= 0f)
                        {
                            renderer.DrawFootprintsOnly(g, v.Entity, cfg.Scale, now);
                            continue;
                        }
                        GoosePose pose = v.Animator.Update(v.Entity, dt, now);
                        string label = string.IsNullOrEmpty(v.Look.Name) ? "гость" : v.Look.Name;
                        draws.Add(new KeyValuePair<float, Action>(v.Entity.position.y, () => renderer.Draw(g, pose, v.Entity, now, v.Look.Hat, label, scarf)));
                    }
                    draws.Sort((a, b) => a.Key.CompareTo(b.Key)); // lower on screen = closer = drawn last
                    particles.DrawSmoke(g, now);
                    foreach (KeyValuePair<float, Action> d in draws) d.Value();

                    particles.Update(dt, now);
                    particles.Draw(g, now);
                    winter.DrawAir(g);
                    if (speaker != null && speaker.Busy) DrawBubble(g, mine, me, screen);
                    if (escProgress > 0.03f) DrawEscBar(g, escProgress);
                }
            }
            overlay.Present();
            if (!Deluxe.HiddenForFullscreen) overlay.KeepOnTop();
            UpdateClickable();
        }

        private static void NoRender(GooseEntity g, Graphics gfx) { }

        private static Rectangle MainWindowBounds()
        {
            Form main = Engine.MainForm;
            if (main != null && main.Width > 0 && main.Height > 0) return main.Bounds;
            return Screen.PrimaryScreen.WorkingArea;
        }

        private void Fail(GooseEntity goose, Exception ex)
        {
            failed = true;
            Deluxe.Log("Disabled after an error, the goose is back to its own drawing: " + ex);
            Deluxe.Status("failed", ex.GetType().Name + ": " + ex.Message);
            try
            {
                if (originalRender != null) goose.render = originalRender;
                if (originalTick != null) goose.tick = originalTick;
                Deluxe.Sleeping = Deluxe.HiddenForFullscreen = false;
                Engine.Thaw();
                if (overlay != null) { overlay.Hide(); overlay.Dispose(); overlay = null; }
                if (timer != null) timer.Stop();
                if (driftAudio != null) driftAudio.Dispose();
                if (speechAudio != null) speechAudio.Dispose();
            }
            catch { }
        }

        private void Guard(string what, Action action)
        {
            try { action(); }
            catch (Exception ex) { Deluxe.Log(what + " failed: " + ex); }
        }

        // ---------------------------------------------------------------- timer: queue, fullscreen, mail, sleep

        private void OnTimer()
        {
            if (failed) return;
            Deluxe.RunUiQueue();
            timerTicks++;
            if (cfg.PauseInFullscreen && timerTicks % 3 == 0) CheckFullscreen();
            if (timerTicks % 50 == 0) UpdateSeason(); // every 5 s: changing the system date takes effect quickly
            if (poller != null)
                foreach (char key in poller.Poll(HotkeyLetters)) OnHotkey(key, "опрос клавиатуры");
            MaybeWelcome();
            bool awake = !Deluxe.Sleeping && !Deluxe.HiddenForFullscreen;
            if (cfg.RandomChase && awake && Deluxe.IsCurrentTask("Wander") &&
                randomChase.Due(clock.Elapsed.TotalSeconds, cfg.RandomChaseMinutes))
                StartChase();
            UpdatePhrases(awake);
            if (driftAudio != null)
                driftAudio.Update(animator.DriftAmount, !GooseSettings.SilenceSounds && !Deluxe.Sleeping && !Deluxe.HiddenForFullscreen,
                                  cfg, clock.Elapsed.TotalSeconds);
            DispatchMail();
            MaybeRunThroughSnow();
            // while frozen the goose doesn't paint, so a sleeping goose is animated from here
            if (Engine.Frozen && Deluxe.Sleeping && !Deluxe.HiddenForFullscreen)
            {
                // no goose frames while it sleeps: holding ESC to send it away is checked from here
                if (EscHeldLongEnough()) { Exit(); return; }
                Time.TickTime();
                DrawFrame(0.1f, Time.time);
            }
        }

        private void CheckFullscreen()
        {
            double now = clock.Elapsed.TotalSeconds;
            bool fs;
            try { fs = FullscreenDetector.IsFullscreenOnPrimary(); }
            catch (Exception ex) { Deluxe.Log("Fullscreen check disabled: " + ex.Message); cfg.PauseInFullscreen = false; return; }
            if (fs)
            {
                if (fullscreenSince < 0) fullscreenSince = now;
                if (!Deluxe.HiddenForFullscreen && now - fullscreenSince >= 0.8)
                {
                    Deluxe.HiddenForFullscreen = true;
                    ApplyEngineState();
                }
            }
            else
            {
                fullscreenSince = -1;
                if (Deluxe.HiddenForFullscreen)
                {
                    Deluxe.HiddenForFullscreen = false;
                    ApplyEngineState();
                }
            }
        }

        /// <summary>Freezes or resumes the goose to match Sleeping / HiddenForFullscreen.</summary>
        private void ApplyEngineState()
        {
            if (failed) return;
            bool freeze = Deluxe.Sleeping || Deluxe.HiddenForFullscreen;
            animator.Asleep = Deluxe.Sleeping;
            if (freeze) Engine.Freeze(Deluxe.HiddenForFullscreen);
            else Engine.Thaw();
            if (timestep != null) timestep.Reset();
            lastRenderTime = -1f;
            if (Deluxe.HiddenForFullscreen || (Deluxe.Sleeping && Engine.Frozen))
            {
                Time.TickTime();
                DrawFrame(0.05f, Time.time); // clears the overlay when hidden, draws the sleeping goose otherwise
            }
        }

        // ---------------------------------------------------------------- seasons

        private void UpdateSeason()
        {
            Season now = SeasonClock.Current(cfg.Seasons);
            newYear = cfg.NewYearHat && now != Season.None && SeasonClock.IsNewYear(SeasonClock.Now());
            if (now == season) return;
            Deluxe.Log("Season: " + season + " -> " + now);
            season = now;
            winter.Active = season == Season.Winter;
            if (season == Season.Winter) nextSnowdriftRun = clock.Elapsed.TotalSeconds + 20;
        }

        /// <summary>In winter, now and then, the goose charges through a snowdrift.</summary>
        private void MaybeRunThroughSnow()
        {
            if (season != Season.Winter || Deluxe.Sleeping || Deluxe.HiddenForFullscreen) return;
            double now = clock.Elapsed.TotalSeconds;
            if (now < nextSnowdriftRun || !Deluxe.IsCurrentTask("Wander") || winter.PickDrift() == null) return;
            nextSnowdriftRun = now + M.Rand(25f, 70f);
            Deluxe.SetTask(ChaseSnowdriftTask.Id, false);
        }

        /// <summary>First start of a new version: a balloon from the tray icon and a note brought by the goose.</summary>
        private void ShowWhatsNewOnce()
        {
            string marker = Path.Combine(Deluxe.ModDir, "GooseDeluxe.version");
            string seen = File.Exists(marker) ? File.ReadAllText(marker).Trim() : "";
            if (seen == Version) return;
            File.WriteAllText(marker, Version);
            welcomeAt = clock.Elapsed.TotalSeconds + 6;
            if (tray != null)
                Deluxe.UiQueue.Enqueue(() => tray.Balloon("Гусь обновился до " + Version,
                    "Теперь я говорю фразы: нажми на меня — или «Сказать фразу» в меню (правая кнопка по гусю). Значок гуся — у часов."));
        }

        private void MaybeWelcome()
        {
            if (welcomeAt < 0 || clock.Elapsed.TotalSeconds < welcomeAt) return;
            if (Deluxe.Sleeping || Deluxe.HiddenForFullscreen || !Deluxe.IsCurrentTask("Wander")) return;
            welcomeAt = -1;
            DeliverTask.Pending = new DeliveryPayload
            {
                FromName = "гуся",
                Text = "Привет! Я обновился: GooseDeluxe " + Version + ".\n\n" +
                       "Нажми на меня ПРАВОЙ кнопкой мыши — там меню и пульт с настройками. Или Ctrl+Alt+M.\n" +
                       "Левой кнопкой — я скажу фразу. Ещё — «Сказать фразу» в меню, а иногда я сам подойду и скажу.\n" +
                       "Свои фразы можно дописать: в меню «Фразы гуся (дописать свои)».\n" +
                       "Голос и «сам подходит» — в пульте, вкладка «Настройки». Кучи листьев разлетаются от клика.\n\n" +
                       "Во вкладке «Проверка» видно, что у меня работает. Га!",
            };
            if (!Deluxe.SetTask(DeliverTask.Id, false)) DeliverTask.Pending = null;
        }

        /// <summary>
        /// The overlay catches mouse clicks only while the cursor is on the goose (right click: menu, left: honk)
        /// or on a leaf pile (left click kicks it). Everywhere else clicks go through to the desktop.
        /// </summary>
        private void UpdateClickable()
        {
            if (overlay == null) return;
            bool near = false;
            Point cur = Cursor.Position;
            Rectangle b = MainWindowBounds();
            Vector2 at = new Vector2(cur.X - b.X, cur.Y - b.Y);
            if (!Deluxe.HiddenForFullscreen && animator.HasPose) near = GooseHit.IsOnGoose(animator.Pose, at);
            if (!near && LeafClicksOn)
            {
                try { near = autumn.PileAt(at) >= 0; }
                catch (Exception ex) { leafClicksBroken = true; Deluxe.Log("leaf clicks disabled: " + ex.Message); }
            }
            overlay.SetClickable(near);
        }

        private bool LeafClicksOn
        {
            get { return cfg.ClickLeafPiles && autumn != null && !leafClicksBroken && autumn.CanKick && !Deluxe.HiddenForFullscreen && !Deluxe.Sleeping; }
        }

        /// <summary>
        /// The piles are drawn by the goose's own window, under ours, where our overlay is fully transparent and
        /// so lets every click through. A barely-there fill (alpha 1 of 255) over each pile makes ours catch it.
        /// </summary>
        private void DrawLeafHitAreas(Graphics g)
        {
            List<AutumnControl.Pile> piles;
            try { piles = autumn.Piles(); }
            catch (Exception ex) { leafClicksBroken = true; Deluxe.Log("leaf clicks disabled: " + ex.Message); return; }
            foreach (AutumnControl.Pile p in piles)
            {
                if (p.Kicked) continue;
                float cx, cy, rx, ry;
                LeafHit.Area(p.Pos, p.Rad, out cx, out cy, out rx, out ry);
                g.FillEllipse(LeafHitBrush, cx - rx, cy - ry, rx * 2f, ry * 2f);
            }
        }

        /// <summary>
        /// Backup for clicks on leaf piles: our window normally catches them (see DrawLeafHitAreas), but if the
        /// click went past it, the left button's press and release over an untouched pile still kick it.
        /// </summary>
        private void PollPileClick()
        {
            if (!LeafClicksOn) { leftDown = false; return; }
            bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (down == leftDown) return;
            leftDown = down;
            Point cur = Cursor.Position;
            Rectangle b = MainWindowBounds();
            Vector2 at = new Vector2(cur.X - b.X, cur.Y - b.Y);
            if (down) { leftDownAt = at; return; }
            if (clock.Elapsed.TotalSeconds - lastOverlayClickAt < 0.3) return; // our window got this one
            if (Vector2.Distance(at, leftDownAt) > 6f) return;                   // a drag, not a click
            try { if (autumn.KickAt(at, Time.time)) leafClicks++; }
            catch (Exception ex) { leafClicksBroken = true; Deluxe.Log("leaf clicks disabled: " + ex.Message); }
        }

        private void OnLeftClick(Point client)
        {
            lastOverlayClickAt = clock.Elapsed.TotalSeconds;
            Vector2 at = new Vector2(client.X, client.Y);
            if (LeafClicksOn && autumn.KickAt(at, Time.time))
            {
                leafClicks++;
                return;
            }
            if (animator.HasPose && GooseHit.IsOnGoose(animator.Pose, at))
            {
                if (cfg.Phrases && speaker != null && !speaker.Talking(clock.Elapsed.TotalSeconds)) SayPhrase();
                else HonkNow();
            }
        }

        private void ShowGooseMenu()
        {
            lastRightClickAt = clock.Elapsed.TotalSeconds;
            Deluxe.Log("right click on the goose at " + Cursor.Position);
            if (tray != null) tray.ShowMenuAt(Cursor.Position);
            else ControlPanel.Open(this);
        }

        // ---------------------------------------------------------------- goose mail

        private void DispatchMail()
        {
            FriendService f = Deluxe.Friends;
            Incoming x;
            if (f == null || !f.Inbox.TryPeek(out x)) return;
            double now = clock.Elapsed.TotalSeconds;

            if (Deluxe.Sleeping || Deluxe.HiddenForFullscreen)
            {
                if (!toldAboutMailWhileAsleep && !Deluxe.HiddenForFullscreen)
                {
                    toldAboutMailWhileAsleep = true;
                    Deluxe.Notify("Гусиная почта", "Пришло от «" + (x.FromName ?? "друга") + "». Разбуди гуся (Ctrl+Alt+P) — он принесёт.");
                }
                return;
            }
            toldAboutMailWhileAsleep = false;

            if (x.Kind == IncomingKind.Command && (x.Command == GooseCommand.Phrase || x.Command == GooseCommand.Say))
            {
                // said by this goose, whoever asked for it (a friend's goose or a phone)
                if (speaker == null || phrases == null || !cfg.Phrases)
                {
                    f.Inbox.TryDequeue(out x);
                    if (now - lastHonkIn >= 3) { lastHonkIn = now; Deluxe.Honk(); } // phrases are off: just a honk
                    return;
                }
                if (speaker.Busy || !Deluxe.IsCurrentTask("Wander") || now - lastDispatch < 2) return;
                f.Inbox.TryDequeue(out x);
                lastDispatch = now;
                if (x.FromCode != null && x.FromCode == f.MyCode) selfTestBackAt = now;
                if (x.Command == GooseCommand.Say) Say(x.Text, VoiceForText(), true, false);
                else SayKey(NextKey(), true);
                return;
            }
            if (x.Guest != null)
            {
                // another goose delivers in person, one visitor at a time
                if (guests.Busy) return;
                f.Inbox.TryDequeue(out x);
                Visit(f, x, now);
                return;
            }

            if (x.Kind == IncomingKind.Command && x.Command == GooseCommand.Honk)
            {
                f.Inbox.TryDequeue(out x);
                if (now - lastHonkIn >= 3) { lastHonkIn = now; Deluxe.Honk(); }
                return;
            }
            if (!Deluxe.IsCurrentTask("Wander") || now - lastDispatch < 2) return; // let it finish what it's doing
            f.Inbox.TryDequeue(out x);
            lastDispatch = now;
            if (x.Kind == IncomingKind.Command) RunOwnCommand(x.Command, now);
            else
            {
                DeliverTask.Pending = PayloadOf(x);
                if (!Deluxe.SetTask(DeliverTask.Id, false)) DeliverTask.Pending = null;
            }
        }

        private void Visit(FriendService f, Incoming x, double now)
        {
            if (x.FromCode != null && x.FromCode == f.MyCode) selfTestBackAt = clock.Elapsed.TotalSeconds;
            string task;
            DeliveryPayload payload = null;
            if (x.Kind == IncomingKind.Command)
            {
                switch (x.Command)
                {
                    case GooseCommand.Meme: task = "CollectMeme"; break;
                    case GooseCommand.Note: task = "CollectNotepad"; break;
                    case GooseCommand.Mud: task = "TrackMud"; break;
                    case GooseCommand.Come: task = ComeTask.Id; break;
                    case GooseCommand.Steal: task = MayStealMouse(now) ? "NabMouse" : VisitTask.Id; break;
                    default: task = VisitTask.Id; break;
                }
            }
            else
            {
                task = DeliverTask.Id;
                payload = PayloadOf(x);
            }
            Guest g = guests.Spawn(x.Guest, task, payload);
            if (g == null) { Deluxe.Log("Could not spawn a visitor for task " + task); return; }
            if (task == "NabMouse") g.Animator.Honk(g.Entity, Time.time);

            f.LastSenderCode = x.FromCode;
            f.LastSenderName = x.FromName;
            if (!f.HasFriend && x.FromCode != null && x.FromCode != f.MyCode)
            {
                f.FriendCode = x.FromCode;
                f.FriendName = string.IsNullOrEmpty(x.FromName) ? "Друг" : x.FromName;
                f.Save();
                Deluxe.Notify("Гусь к другу", "К тебе пришёл гусь «" + f.FriendName + "». Он теперь в друзьях: можно отвечать.");
            }
        }

        private void RunOwnCommand(GooseCommand cmd, double now)
        {
            switch (cmd)
            {
                case GooseCommand.Meme: Deluxe.SetTask("CollectMeme", false); break;
                case GooseCommand.Note: Deluxe.SetTask("CollectNotepad", false); break;
                case GooseCommand.Mud: Deluxe.SetTask("TrackMud", false); break;
                case GooseCommand.Come: Deluxe.SetTask(ComeTask.Id, false); break;
                case GooseCommand.Steal:
                    if (MayStealMouse(now)) Deluxe.SetTask("NabMouse", true);
                    else Deluxe.Honk();
                    break;
            }
        }

        private bool MayStealMouse(double now)
        {
            if (!GooseSettings.CanAttackMouse || !cfg.FriendCanStealMouse || now - lastStealIn < 60) return false;
            lastStealIn = now;
            return true;
        }

        private static DeliveryPayload PayloadOf(Incoming x)
        {
            DeliveryPayload p = new DeliveryPayload { FromName = x.FromName, FromCode = x.FromCode, Text = x.Text, FileName = x.FileName };
            if (x.Kind == IncomingKind.Image) p.ImageBytes = x.ImageBytes;
            if (x.Kind == IncomingKind.UnsupportedFile)
                p.Text = "Мне прислали файл «" + x.FileName + "», но я гусь и ношу только картинки PNG, JPG, GIF и BMP." +
                         (string.IsNullOrEmpty(x.Text) ? "" : "\n\nК нему было написано: " + x.Text);
            return p;
        }

        // ---------------------------------------------------------------- actions (tray, hotkeys, note window)

        private void Wake()
        {
            if (!Deluxe.Sleeping) return;
            Deluxe.Sleeping = false;
            ApplyEngineState();
        }

        public void Come() { Wake(); Deluxe.SetTask(ComeTask.Id, false); }

        public void HonkNow() { Wake(); Deluxe.Honk(); }

        public void RunGooseTask(string id) { Wake(); Deluxe.SetTask(id, id == "NabMouse"); }

        public void TogglePause()
        {
            if (failed) return;
            Deluxe.Sleeping = !Deluxe.Sleeping;
            ApplyEngineState();
        }

        public void ToggleMute() { GooseSettings.SilenceSounds = !GooseSettings.SilenceSounds; }

        public void ToggleMouseStealing() { GooseSettings.CanAttackMouse = !GooseSettings.CanAttackMouse; }

        public void OpenSettings()
        {
            string ini = Path.Combine(Deluxe.ModDir, "GooseDeluxe.ini");
            Process.Start("notepad.exe", "\"" + ini + "\"");
        }

        public void BalloonClicked() { Wake(); }

        private bool EnsureFriend(out string code, out string name)
        {
            FriendService f = Deluxe.Friends;
            code = null; name = null;
            if (f == null) return false;
            if (!f.HasFriend && f.LastSenderCode == null) EditFriend();
            code = f.FriendCode ?? f.LastSenderCode;
            name = f.FriendCode != null ? f.FriendName : (f.LastSenderName ?? "друг");
            return code != null;
        }

        public void SendNote(string code, string name)
        {
            FriendService f = Deluxe.Friends;
            if (f == null) return;
            if (code == null && !EnsureFriend(out code, out name)) return;
            using (SendNoteDialog dlg = new SendNoteDialog(name ?? "друга"))
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                string text = dlg.NoteText;
                string to = code;
                StartCarry(CarryKind.Note, name, done => f.SendNote(to, text, done));
            }
        }

        private void ReplyTo(DeliveryPayload p)
        {
            if (string.IsNullOrEmpty(p.FromCode)) return;
            SendNote(p.FromCode, string.IsNullOrEmpty(p.FromName) ? "друг" : p.FromName);
        }

        public void SendPicture()
        {
            FriendService f = Deluxe.Friends;
            string code, name;
            if (f == null || !EnsureFriend(out code, out name)) return;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Картинка для " + name;
                dlg.Filter = "Картинки|*.png;*.jpg;*.jpeg;*.gif;*.bmp";
                string memes = Deluxe.GooseDir != null ? Path.Combine(Path.Combine(Path.Combine(Deluxe.GooseDir, "Assets"), "Images"), "Memes") : null;
                if (memes != null && Directory.Exists(memes)) dlg.InitialDirectory = memes;
                if (dlg.ShowDialog() != DialogResult.OK) return;
                FileInfo fi = new FileInfo(dlg.FileName);
                if (fi.Length > FriendProtocol.MaxImageBytes)
                {
                    MessageBox.Show("Картинка больше 8 МБ — гусь её не унесёт.", "Гусь к другу", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                byte[] data = File.ReadAllBytes(dlg.FileName);
                string fileName = fi.Name;
                StartCarry(CarryKind.Photo, name, done => f.SendImage(code, data, fileName, done));
            }
        }

        public void SendPrank(GooseCommand cmd)
        {
            FriendService f = Deluxe.Friends;
            string code, name;
            if (f == null || !EnsureFriend(out code, out name)) return;
            StartCarry(CarryKind.None, name, done => f.SendCommand(code, cmd, done));
        }

        /// <summary>The goose runs off with it; the upload happens meanwhile and the goose waits for the result.</summary>
        private void StartCarry(CarryKind kind, string toName, Action<Action<bool>> send)
        {
            Wake();
            CarryJob job = new CarryJob { Kind = kind, To = toName ?? "" };
            send(ok => job.State = ok ? CarryJob.Sent : CarryJob.Failed);
            CarryTask.Pending = job;
            if (!Deluxe.SetTask(CarryTask.Id, false)) CarryTask.Pending = null;
        }

        public void CopyMyCode()
        {
            if (Deluxe.Friends == null) return;
            Clipboard.SetText(GooseCode.Display(Deluxe.Friends.MyCode));
            Deluxe.Notify("Код скопирован", GooseCode.Display(Deluxe.Friends.MyCode) + " — отправь его другу.");
        }

        public void CopyInvite()
        {
            if (Deluxe.Friends == null) return;
            Clipboard.SetText(Deluxe.Friends.InviteText());
            Deluxe.Notify("Приглашение скопировано", "Вставь его другу в мессенджер (Ctrl+V).");
        }

        public void CopyPhoneLink()
        {
            if (Deluxe.Friends == null) return;
            Clipboard.SetText(Deluxe.Friends.PhoneLink);
            Deluxe.Notify("Ссылка скопирована", "Кто откроет её на телефоне, сможет писать твоему гусю: га, мем, записка, сюда…");
        }

        public void EditFriend()
        {
            FriendService f = Deluxe.Friends;
            if (f == null) return;
            using (FriendDialog dlg = new FriendDialog(f.FriendCode ?? f.LastSenderCode, f.HasFriend ? f.FriendName : f.LastSenderName, f.MyName, f.MyCode))
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                f.FriendCode = dlg.Code;
                f.FriendName = dlg.FriendName.Length > 0 ? dlg.FriendName : "Друг";
                if (dlg.MyName.Length > 0) f.MyName = dlg.MyName;
                f.Save();
                Deluxe.Notify("Гусь к другу", "Друг «" + f.FriendName + "» сохранён.");
            }
        }

        public void Exit()
        {
            Cursor.Clip = Rectangle.Empty;
            Cleanup();
            Application.Exit();
            // meme windows run on their own non-background threads; don't let them keep the goose alive
            Thread killer = new Thread(() => { Thread.Sleep(2000); Environment.Exit(0); });
            killer.IsBackground = true;
            killer.Start();
        }

        // ---------------------------------------------------------------- control panel (IGooseControls)

        public DeluxeConfig Config { get { return cfg; } }
        public bool Paused { get { return Deluxe.Sleeping; } }
        public bool GooseSettingsAvailable { get { return GooseSettings.Available; } }
        public bool Muted { get { return GooseSettings.SilenceSounds; } set { GooseSettings.SilenceSounds = value; } }
        public bool GooseMayStealMouse { get { return GooseSettings.CanAttackMouse; } set { GooseSettings.CanAttackMouse = value; } }
        public bool FriendsEnabled { get { return Deluxe.Friends != null; } }
        public bool HasFriend { get { return Deluxe.Friends != null && (Deluxe.Friends.HasFriend || Deluxe.Friends.LastSenderCode != null); } }

        public string FriendName
        {
            get
            {
                FriendService f = Deluxe.Friends;
                if (f == null) return "";
                return f.HasFriend ? f.FriendName : (f.LastSenderName ?? "друг");
            }
        }

        public string MyCodeDisplay { get { return Deluxe.Friends != null ? GooseCode.Display(Deluxe.Friends.MyCode) : "—"; } }

        public void SendNote() { SendNote(null, null); }

        /// <summary>Applies a setting changed in the panel right away and saves just that line of the ini.</summary>
        public void ApplyConfig(string key)
        {
            switch (key)
            {
                case "FixSpeed":
                    if (timestep != null) { timestep.Enabled = cfg.FixSpeed; timestep.Reset(); }
                    break;
                case "HonestRandom":
                    if (!cfg.HonestRandom) deckFixer = null;
                    else if (deckFixer == null)
                    {
                        Func<Deck> deck = DeckFixer.FromGooseDatabase();
                        if (deck != null) deckFixer = new DeckFixer(deck);
                    }
                    break;
                case "PauseInFullscreen":
                    if (!cfg.PauseInFullscreen && Deluxe.HiddenForFullscreen)
                    {
                        Deluxe.HiddenForFullscreen = false;
                        ApplyEngineState();
                    }
                    break;
                case "RussianNotes":
                    RussianPack.Apply(Deluxe.GooseDir, cfg.RussianNotes);
                    break;
                case "RussianMemes":
                    russianMemes = MemeTranslator.Apply(Deluxe.GooseDir, cfg.RussianMemes);
                    break;
                case "Seasons":
                case "NewYearHat":
                    UpdateSeason();
                    break;
                case "Phrases":
                case "PhraseVoice":
                    if (speaker != null && phrases != null)
                    {
                        if (!cfg.Phrases) speaker.Stop();
                        else PrepareNext();
                    }
                    break;
            }
            cfg.Save(IniPath, key);
            Deluxe.Log("Setting " + key + "=" + cfg.ValueText(key));
        }

        private static string Ago(double seconds)
        {
            if (seconds < 90) return seconds.ToString("0") + " с назад";
            return (seconds / 60).ToString("0") + " мин назад";
        }

        private static string HostOf(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host : url;
        }

        private static string SeasonName(Season s)
        {
            switch (s)
            {
                case Season.Winter: return "зима";
                case Season.Spring: return "весна";
                case Season.Summer: return "лето";
                case Season.Autumn: return "осень";
                default: return "выключены";
            }
        }

        public List<DiagItem> RunDiagnostics()
        {
            List<DiagItem> list = new List<DiagItem>();
            double now = clock.Elapsed.TotalSeconds;
            Action<DiagLevel, string, string> add = (l, t, d) => list.Add(new DiagItem(l, t, d));

            add(DiagLevel.Ok, "Мод загружен", "GooseDeluxe " + Version + " — " + typeof(Controller).Assembly.Location);
            if (overlay != null && overlay.IsHandleCreated)
                add(DiagLevel.Ok, "Рисование гуся", "окно " + overlay.Width + "×" + overlay.Height + (Engine.Frozen ? ", гусь спит" : ", " + fps.ToString("0") + " кадров/с"));
            else add(DiagLevel.Fail, "Рисование гуся", "своё окно не создано — гусь рисуется по-старому (причина в журнале)");
            add(Engine.Available ? DiagLevel.Ok : DiagLevel.Warn, "Подключение к движку гуся",
                Engine.Available ? "пауза и прятки в играх работают полностью" : "не получилось — пауза и прятки работают частично");
            add(GooseSettings.Available ? DiagLevel.Ok : DiagLevel.Warn, "Настройки гуся (config.ini)",
                GooseSettings.Available ? "звук " + (Muted ? "выключен" : "включён") + ", кража курсора " + (GooseMayStealMouse ? "разрешена" : "запрещена")
                                        : "нет доступа — «Без звука» и «Красть курсор» из пульта не работают");

            if (!cfg.FixSpeed) add(DiagLevel.Info, "Правильная скорость", "выключена в настройках");
            else if (Engine.Frozen || Deluxe.HiddenForFullscreen || fps <= 0f) add(DiagLevel.Info, "Правильная скорость", "гусь сейчас спит — проверю, когда проснётся");
            else
            {
                bool good = ticksPerSecond > 105f && ticksPerSecond < 135f;
                add(good ? DiagLevel.Ok : DiagLevel.Warn, "Правильная скорость",
                    ticksPerSecond.ToString("0") + " шагов в секунду (нужно около 120) при " + fps.ToString("0") + " кадрах/с");
            }

            if (deckFixer != null) add(DiagLevel.Ok, "Честный рандом", "колода проделок перетасована " + deckFixer.Fixes + " раз");
            else add(cfg.HonestRandom ? DiagLevel.Warn : DiagLevel.Info, "Честный рандом", cfg.HonestRandom ? "колода задач не найдена" : "выключен в настройках");

            if (!cfg.Tray) add(DiagLevel.Info, "Значок у часов", "выключен в настройках (Tray=False)");
            else if (tray != null) add(DiagLevel.Ok, "Значок у часов", "создан. Не видно? Нажми стрелку ^ у часов и перетащи гуся на панель задач");
            else add(DiagLevel.Fail, "Значок у часов", "не создан: " + (trayError ?? "причина в журнале"));

            if (!cfg.Hotkeys) add(DiagLevel.Info, "Горячие клавиши", "выключены в настройках (Hotkeys=False)");
            else
            {
                List<string> parts = new List<string>();
                foreach (char k in HotkeyLetters)
                {
                    string st;
                    parts.Add("Ctrl+Alt+" + k + ": " + (hotkeyState.TryGetValue(k, out st) ? st : (hotkeyError ?? "не зарегистрирована")));
                }
                string last = lastHotkeyAt > 0 ? ". Последнее нажатие: " + lastHotkey + ", " + Ago(now - lastHotkeyAt) : ". Нажатий пока не было";
                add(poller != null ? DiagLevel.Ok : DiagLevel.Warn, "Горячие клавиши", string.Join("; ", parts) + (poller != null ? ". Запасной способ (опрос клавиатуры) включён" : "") + last);
            }
            add(DiagLevel.Ok, "Правый клик по гусю", "открывает меню" + (lastRightClickAt > 0 ? ", последний раз " + Ago(now - lastRightClickAt) : " — попробуй навести курсор на гуся и нажать правую кнопку"));

            if (!cfg.PauseInFullscreen) add(DiagLevel.Info, "Игры и кино на весь экран", "не прятаться (выключено)");
            else add(DiagLevel.Ok, "Игры и кино на весь экран", Deluxe.HiddenForFullscreen ? "сейчас что-то на весь экран — гусь спрятан" : "слежу: гусь спрячется, когда игра или видео будут на весь экран");

            add(DiagLevel.Ok, "Время года", SeasonName(season) + " (дата " + SeasonClock.Now().ToString("dd.MM.yyyy") + ", настройка «" + cfg.Seasons + "»)" + (newYear ? ", новогодняя шапка" : ""));
            if (autumn == null) add(DiagLevel.Info, "Осенние листья", "осенний мод автора гуся не найден (выключен или удалён) — листьев не будет");
            else add(DiagLevel.Ok, "Осенние листья", season == Season.Autumn || season == Season.None
                    ? "листья есть (куч сейчас: " + autumn.Count + ")" : "сейчас не осень — листья убираются (куч сейчас: " + autumn.Count + ")");
            if (autumn != null)
            {
                if (!cfg.ClickLeafPiles) add(DiagLevel.Info, "Кучи листьев кликом", "выключено в настройках");
                else if (!autumn.CanKick || leafClicksBroken) add(DiagLevel.Warn, "Кучи листьев кликом", "не работает с этой версией осеннего мода");
                else if (!cfg.LeafPiles) add(DiagLevel.Info, "Кучи листьев кликом", "кучи листьев выключены в настройках");
                else add(DiagLevel.Ok, "Кучи листьев кликом", "нажми на кучу — листья разлетятся" + (leafClicks > 0 ? ". Разбросано куч: " + leafClicks : ""));
            }
            if (season == Season.Winter)
                add(DiagLevel.Ok, "Зима", "снежинок " + winter.FlakeCount + ", сугробов " + winter.Drifts.Count + ", следов " + winter.PrintCount + ", снега у края " + winter.Bank.ToString("0") + " px");
            else add(DiagLevel.Info, "Зима", "сейчас не зима — снега нет. Проверить: кнопка «Тест: сугроб» или «Всегда зима» в настройках");

            FriendService f = Deluxe.Friends;
            if (f == null) add(DiagLevel.Info, "Гусиная почта", cfg.Friends ? "не запустилась (причина в журнале)" : "выключена в настройках");
            else if (f.Connected)
                add(DiagLevel.Ok, "Гусиная почта", "связь с " + HostOf(f.Server) + " есть. Твой код " + GooseCode.Display(f.MyCode) + (f.HasFriend ? ", друг: " + f.FriendName : ", друг не добавлен"));
            else add(DiagLevel.Warn, "Гусиная почта", "нет связи с " + HostOf(f.Server) + ", переподключаюсь. Может мешать интернет, провайдер, антивирус или прокси");
            if (selfTestSentAt > 0)
            {
                if (selfTestBackAt >= selfTestSentAt) add(DiagLevel.Ok, "Тест связи", "записка сходила на сервер и вернулась за " + (selfTestBackAt - selfTestSentAt).ToString("0.0") + " с");
                else if (now - selfTestSentAt > 20) add(DiagLevel.Warn, "Тест связи", "записка не вернулась за 20 с");
                else add(DiagLevel.Info, "Тест связи", "жду, когда записка вернётся… (" + (now - selfTestSentAt).ToString("0") + " с)");
            }

            int ru = 0;
            try
            {
                string notes = Path.Combine(Path.Combine(Path.Combine(Deluxe.GooseDir, "Assets"), "Text"), "NotepadMessages");
                if (Directory.Exists(notes)) ru = Directory.GetFiles(notes, "ru-*.txt").Length;
            }
            catch { }
            if (!cfg.RussianNotes) add(DiagLevel.Info, "Русские записки", "выключены");
            else add(ru > 0 ? DiagLevel.Ok : DiagLevel.Warn, "Русские записки", ru > 0 ? ru + " записок в блокноте гуся" : "не нашёл папку с записками гуся");
            if (!cfg.RussianMemes) add(DiagLevel.Info, "Мемы по-русски", "выключено — у мемов английские надписи");
            else add(russianMemes > 0 ? DiagLevel.Ok : DiagLevel.Warn, "Мемы по-русски",
                     russianMemes > 0 ? "переведено мемов: " + russianMemes + " из " + MemeTranslator.Memes.Length + " (оригиналы — в папке Memes\\en)"
                                      : "не нашёл мемов гуся, которые умею переводить");
            string driftParts = (cfg.DriftSmoke && cfg.Particles ? "дым" : "без дыма") + ", " + (cfg.DriftSound ? "визг шин" : "без визга") + ", " +
                                (cfg.DriftMusic ? "фонк: " + (driftAudio == null ? "?" : (driftAudio.UserTrack() != null ? Path.GetFileName(driftAudio.UserTrack()) + " (" +
                                 cfg.DriftMusicFrom.ToString("0.#") + "–" + cfg.DriftMusicTo.ToString("0.#") + " с)" : "встроенный бит (свой трек — в папку Фонк)")) : "без фонка");
            if (driftAudio != null && driftAudio.LastError != null) add(DiagLevel.Warn, "Дрифт", driftParts + ". Звук не заиграл: " + driftAudio.LastError);
            else add(DiagLevel.Ok, "Дрифт", driftParts + (GooseSettings.SilenceSounds ? " (звук гуся выключен — будет тихо)" : "") +
                     ". Сейчас занос: " + (animator.DriftAmount * 100).ToString("0") + "%. Проверить — кнопка «Тест дрифта» на вкладке «Пульт»");
            if (!cfg.RandomChase) add(DiagLevel.Info, "Погоня за курсором", "сам не гоняется (выключено в настройках)");
            else add(DiagLevel.Ok, "Погоня за курсором", "примерно раз в " + cfg.RandomChaseMinutes.ToString("0.#") + " мин" +
                     (randomChase.NextAt > 0 ? ", следующая через " + Math.Max(0, (randomChase.NextAt - now) / 60).ToString("0.#") + " мин" : "") +
                     ". Сразу — «Погнаться за курсором» в пульте или в меню");
            if (!cfg.Phrases) add(DiagLevel.Info, "Фразы", "выключены в настройках");
            else if (phrases == null || speaker == null) add(DiagLevel.Warn, "Фразы", "не запустились — смотри журнал");
            else
            {
                string voice = cfg.PhraseVoice == "Off" ? "без голоса, только облачко" : VoiceLine();
                string self = cfg.RandomPhrases
                    ? "сам подходит примерно раз в " + cfg.RandomPhraseMinutes.ToString("0.#") + " мин" +
                      (randomPhrase.NextAt > 0 ? " (следующий раз через " + Math.Max(0, (randomPhrase.NextAt - now) / 60).ToString("0.#") + " мин)" : "")
                    : "сам не подходит (выключено)";
                int count = phrases.Count;
                add(count > 0 && speaker.LastError == null ? DiagLevel.Ok : DiagLevel.Warn, "Фразы",
                    "фраз: " + count + "; " + voice + "; " + self + "; сказано: " + speaker.Said +
                    (speaker.LastError != null ? ". Ошибка: " + speaker.LastError : "") +
                    (GooseSettings.SilenceSounds ? ". Звук гуся выключен — только облачко" : "") +
                    ". Сразу — «Сказать фразу» в меню или клик по гусю. Свои фразы — в " + phrases.Path);
            }
            if (!cfg.NoRepeats) add(DiagLevel.Info, "Мемы и записки без повторов", "выключено в настройках");
            else add(DiagLevel.Ok, "Мемы и записки без повторов", "по кругу, без повторов подряд. Принесено мемов: " + forms.MemesShown + ", записок: " + forms.NotesShown +
                     (forms.LastMeme != null ? ". Последний мем: " + Path.GetFileName(forms.LastMeme) : "") +
                     ". Свои картинки можно положить в " + Path.Combine(Path.Combine(Path.Combine(Deluxe.GooseDir ?? "", "Assets"), "Images"), "Memes"));
            add(DiagLevel.Info, "Гости", guests == null || guests.All.Count == 0 ? "сейчас никого" : guests.All.Count + " на экране");

            string log = LogTail(400);
            int problems = 0;
            foreach (string line in log.Split('\n'))
                if (line.Contains("failed") || line.Contains("Disabled") || line.Contains("Exception")) problems++;
            add(problems == 0 ? DiagLevel.Ok : DiagLevel.Warn, "Журнал", (problems == 0 ? "ошибок нет" : "есть сообщения об ошибках: " + problems) + " — " + Path.Combine(Deluxe.ModDir, "GooseDeluxe.log"));
            return list;
        }

        private string VoiceLine()
        {
            recordings.Refresh(phrases.All, clock.Elapsed.TotalSeconds);
            List<string> keys = recordings.Keys();
            List<string> nameless = recordings.NamelessFiles();
            string rec = recordings.Files == 0 ? "готовых записей нет (папка «Голос» пуста)"
                : "готовые записи: " + recordings.Files + " файлов для " + keys.Count + " фраз" +
                  (nameless.Count > 0 ? " (без фразы в имени: " + string.Join(", ", nameless.ToArray()) + ")" : "") +
                  (recordingError != null ? ". Не читается: " + recordingError : "");
            if (cfg.PhraseVoice == "Records")
                return recordings.Files > 0 ? rec + " — гусь говорит только их" : rec + " — пока говорит по-гусиному; записи — в меню «Папка голоса»";
            return rec + "; фразы без записи — " + (cfg.PhraseVoice == "Goose" ? "по-гусиному (га-га)" : WindowsVoiceLine());
        }

        /// <summary>The Windows voice for the self-check. Separate, so a missing System.Speech can't break the rest.</summary>
        private string WindowsVoiceLine()
        {
            try
            {
                string name = WindowsVoiceName();
                if (name != null) return "голос: " + (speaker.LastVoice ?? "Windows «" + name + "», по-гусиному");
                string why = windowsVoiceError ?? WindowsVoiceProblem();
                return why == null ? "голос Windows ещё не проверен (проверится на первой фразе)"
                                   : "голос по-гусиному, потому что " + why + ". Русский голос: Параметры → Время и язык → Речь → Добавить голоса";
            }
            catch (Exception ex) { return "голос по-гусиному (синтез речи Windows недоступен: " + ex.Message + ")"; }
        }

        public string LogTail(int lines)
        {
            try
            {
                string path = Path.Combine(Deluxe.ModDir, "GooseDeluxe.log");
                if (!File.Exists(path)) return "(журнал пуст)";
                string text;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader r = new StreamReader(fs, System.Text.Encoding.UTF8))
                    text = r.ReadToEnd();
                string[] all = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
                int from = Math.Max(0, all.Length - lines);
                return string.Join("\n", all, from, all.Length - from);
            }
            catch (Exception ex) { return "(не удалось прочитать журнал: " + ex.Message + ")"; }
        }

        public void TestNote()
        {
            Wake();
            DeliverTask.Pending = new DeliveryPayload { FromName = "пульта", Text = "Проверка: гусь умеет носить записки. Га!" };
            if (!Deluxe.SetTask(DeliverTask.Id, false)) DeliverTask.Pending = null;
        }

        public void TestConnection(Action<string> report)
        {
            FriendService f = Deluxe.Friends;
            if (f == null) { report("Гусиная почта выключена: включи её во вкладке «Настройки» и перезапусти гуся."); return; }
            string host = HostOf(f.Server);
            selfTestSentAt = clock.Elapsed.TotalSeconds;
            selfTestBackAt = -1;
            f.SendNote(f.MyCode, "Проверка связи: эта записка сходила на " + host + " и вернулась. Гусиная почта работает!", ok => report(ok
                ? "Записка ушла на " + host + ". Если связь в порядке, через несколько секунд придёт гусь-гость с ней, а тут появится «Тест связи ✔»."
                : "Не получилось отправить на " + host + ": сервер недоступен (интернет, провайдер или антивирус)."));
        }

        public void TestSnow()
        {
            Wake();
            GooseEntity g = Deluxe.Goose;
            Vector2 size = Deluxe.ScreenSize();
            float dir = g.position.x < size.x / 2f ? 1f : -1f;
            Vector2 at = new Vector2(M.Clamp(g.position.x + dir * 260f, 80f, size.x - 80f), M.Clamp(g.position.y, 100f, size.y - 60f));
            SnowDrift d = winter.AddDrift(at, 44f * cfg.Scale, Time.time - 1f);
            d.keepUntil = Time.time + 30f;
            Deluxe.SetTask(ChaseSnowdriftTask.Id, false);
        }

        public void OpenModFolder()
        {
            Process.Start("explorer.exe", "\"" + Deluxe.ModDir + "\"");
        }

        public void OpenMemesFolder()
        {
            string dir = Path.Combine(Path.Combine(Path.Combine(Deluxe.GooseDir ?? "", "Assets"), "Images"), "Memes");
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        public void OpenPhonkFolder()
        {
            string dir = DriftAudio.PhonkFolder(Deluxe.ModDir);
            Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        public void ChaseCursor() { Wake(); StartChase(); }

        /// <summary>The goose goes for the cursor — and now and then yells one of its phrases on the way.</summary>
        private void StartChase()
        {
            Deluxe.SetTask(ChaseCursorTask.Id, true);
            if (cfg.Phrases && speaker != null && phrases != null && !speaker.Busy && rng.NextDouble() < 0.5)
                SayKey(NextKey(), false);
        }

        public void SayPhrase()
        {
            Wake();
            if (phrases == null || speaker == null) { HonkNow(); return; }
            SayKey(NextKey(), false);
        }

        private string VoiceFolder { get { return Path.Combine(Deluxe.ModDir, "Голос"); } }

        public void OpenVoiceFolder()
        {
            EnsureVoiceFolder();
            Process.Start("explorer.exe", "\"" + VoiceFolder + "\"");
        }

        private void EnsureVoiceFolder()
        {
            try
            {
                Directory.CreateDirectory(VoiceFolder);
                string help = Path.Combine(VoiceFolder, "Как добавить свои записи.txt");
                File.WriteAllText(help,
                        "Сюда кладутся готовые записи фраз гуся: WAV или MP3.\r\n\r\n" +
                        "Какой файл какая фраза — по списку в .txt рядом (строки вида «01.wav — Инженер ПТО! …»),\r\n" +
                        "или по номеру (01.wav — первая фраза из Фразы.txt), или по словам в имени файла\r\n" +
                        "(«Где акты скрытых работ.wav»). Несколько файлов одной фразы — дубли, гусь говорит их по очереди.\r\n\r\n" +
                        "Пока тут есть записи, гусь говорит только их (пульт → «Настройки» → «Голос фраз» — можно иначе).\r\n",
                        new System.Text.UTF8Encoding(true));
            }
            catch (Exception ex) { Deluxe.Log("voice folder: " + ex.Message); }
        }

        /// <summary>What the goose may say: with «Только готовые записи» (Records) and any recordings — only the
        /// recorded phrases; otherwise every phrase in the file, plus recordings named like phrases of their own.</summary>
        private IList<string> Pool()
        {
            IList<string> all = phrases.All;
            if (recordings == null) return all;
            recordings.Refresh(all, clock.Elapsed.TotalSeconds);
            List<string> recorded = recordings.Keys();
            if (cfg.PhraseVoice == "Records" && recorded.Count > 0) return recorded;
            List<string> pool = new List<string>(all);
            foreach (string k in recorded) if (!pool.Contains(k)) pool.Add(k);
            return pool;
        }

        private string NextKey() { return phrases == null ? null : phrases.Next(Pool()); }

        /// <summary>The voice for a text with no recording: «Records» means the user doesn't want the Windows voice —
        /// then the goose honks it.</summary>
        private string VoiceForText() { return cfg.PhraseVoice == "Records" ? "Goose" : cfg.PhraseVoice; }

        private Recordings.Take TakeFor(string key, bool advance)
        {
            return recordings != null && key != null && cfg.PhraseVoice != "Off" ? recordings.NextTake(key, advance) : null;
        }

        /// <summary>Says a phrase (or a nameless recording) by its key: its recording if there is one, in turn.</summary>
        private void SayKey(string key, bool approach)
        {
            if (key == null) { HonkNow(); return; }
            Recordings.Take take = TakeFor(key, true);
            if (take != null) Say(take.Text, "take:" + take.Path, approach, true);
            else Say(key, VoiceForText(), approach, false);
        }

        private void PrepareNext()
        {
            if (!cfg.Phrases || speaker == null || phrases == null) return;
            string key = phrases.Peek(Pool());
            if (key == null) return;
            Recordings.Take take = TakeFor(key, false);
            if (take != null) speaker.Prepare(take.Text, "take:" + take.Path);
            else speaker.Prepare(key, VoiceForText());
        }

        public void OpenPhrases()
        {
            if (phrases == null) return;
            Process.Start("notepad.exe", "\"" + phrases.Path + "\"");
        }

        /// <summary>Says a phrase in <paramref name="voice"/>; <paramref name="approach"/>: first walks up to the cursor.</summary>
        private void Say(string text, string voice, bool approach, bool recorded)
        {
            if (text != null) text = System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim(); // the bubble is one flowing text
            if (text == null || (text.Length == 0 && !recorded)) { HonkNow(); return; }
            double now = clock.Elapsed.TotalSeconds;
            // only a goose that's just wandering stops (or walks up to the cursor) to talk; one that's busy — carrying
            // a meme, a friend's note, chasing the cursor — says it on the go and carries on
            bool stop = Deluxe.IsCurrentTask("Wander") || Deluxe.IsCurrentTask(SayTask.Id);
            speaker.Say(text, voice, !GooseSettings.SilenceSounds && cfg.PhraseVoice != "Off", (int)(cfg.PhraseVolume * 10), stop, now);
            if (stop)
            {
                SayTask.Approach = approach;
                if (!Deluxe.SetTask(SayTask.Id, false)) speaker.Release(); // no such task (can't happen): say it where it stands
            }
            PrepareNext(); // the next one ready in advance
        }

        private void UpdatePhrases(bool awake)
        {
            if (speaker == null) return;
            double now = clock.Elapsed.TotalSeconds;
            if (!awake && speaker.Busy) speaker.Stop();
            if (GooseSettings.SilenceSounds && speechAudio != null) speechAudio.Stop();
            // the walk up to the cursor was cut short (another task, a call from the menu): say it anyway
            if (speaker.Held && !Deluxe.IsCurrentTask(SayTask.Id)) speaker.Release();
            speaker.Update(now);
            if (cfg.Phrases && cfg.RandomPhrases && awake && !speaker.Busy && Deluxe.IsCurrentTask("Wander") &&
                randomPhrase.Due(now, cfg.RandomPhraseMinutes))
                SayKey(NextKey(), true);
        }

        /// <summary>Voice files of older versions, and any older than a week, are deleted at start.</summary>
        private static void CleanVoiceFiles()
        {
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "GooseDeluxe");
                if (!Directory.Exists(root)) return;
                foreach (string d in Directory.GetDirectories(root))
                    if (Path.GetFileName(d) != Version) try { Directory.Delete(d, true); } catch { }
                string mine = Path.Combine(root, Version);
                if (Directory.Exists(mine))
                    foreach (string f in Directory.GetFiles(mine, "*.wav"))
                        if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7)) try { File.Delete(f); } catch { }
            }
            catch (Exception ex) { Deluxe.Log("voice files cleanup: " + ex.Message); }
        }

        /// <summary>Makes the sound of a phrase (on a worker thread): the Windows voice made goose-like, or the
        /// goose's honking when there's no Russian voice (or it's chosen).</summary>
        private Utterance BuildUtterance(string text, string voice)
        {
            string dir = Path.Combine(Path.Combine(Path.GetTempPath(), "GooseDeluxe"), Version);
            if (voice.StartsWith("take:", StringComparison.Ordinal))
            {
                string rec = voice.Substring(5);
                try { return Utterance.FromRecording(text, rec, dir, RecordingLength); }
                catch (Exception ex)
                {
                    // a file Windows can't read: said the goose's way instead, and the self-check tells which one
                    Deluxe.Log("recording " + rec + " failed: " + ex.Message);
                    recordingError = Path.GetFileName(rec) + ": " + ex.Message;
                    if (text.Length == 0) return Utterance.Silent(text, "запись не читается");
                    voice = "Goose";
                }
            }
            string file = Path.Combine(dir, Utterance.FileNameFor(voice, text));
            if (voice == "Off") return Utterance.Silent(text, "без голоса");
            if (voice == "Atomic")
            {
                try
                {
                    Utterance u = SayWithWindowsVoice(text, file);
                    if (u != null) return u;
                    windowsVoiceError = WindowsVoiceProblem();
                }
                catch (Exception ex)
                {
                    // no System.Speech at all (Wine) or a broken voice: the goose honks it instead
                    windowsVoiceError = "синтез речи Windows не работает: " + ex.Message;
                    Deluxe.Log("Windows voice failed: " + ex);
                }
                file = Path.Combine(dir, Utterance.FileNameFor("Goose", text));
            }
            return GooseVoice.Say(text, file);
        }

        private static double RecordingLength(string path)
        {
            try { return SpeechAudio.LengthSeconds(path); }
            catch { return 0; }
        }

        // Kept apart (and never inlined): where System.Speech can't be loaded, only these fail — inside the callers' try.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static Utterance SayWithWindowsVoice(string text, string file) { return WindowsVoice.Say(text, file); }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string WindowsVoiceName() { return WindowsVoice.Name; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string WindowsVoiceProblem() { return WindowsVoice.Problem; }

        /// <summary>The bubble over the goose's head (its top) — or under its feet if there's no room above.</summary>
        private void DrawBubble(Graphics g, GoosePose pose, GooseEntity me, Vector2 screen)
        {
            HatStyle hat = cfg.Hat != HatStyle.None ? cfg.Hat : (newYear ? HatStyle.Santa : HatStyle.None);
            float above = hat == HatStyle.None ? 14f : 32f; // clear of the hat
            float top = Math.Min(pose.neckHeadPoint.y, Math.Min(pose.head1EndPoint.y, pose.head2EndPoint.y)) - above * cfg.Scale;
            Vector2 head = new Vector2(pose.head2EndPoint.x * 0.5f + pose.neckHeadPoint.x * 0.5f, top);
            Vector2 feet = new Vector2(me.position.x, me.position.y + 10f * cfg.Scale);
            speaker.Draw(g, head, feet, screen, clock.Elapsed.TotalSeconds);
        }

        public void TestDrift()
        {
            Wake();
            animator.ForceDriftUntil = Time.time + 4f;
        }

        public void SweepLeaves()
        {
            if (autumn == null) return;
            if (Deluxe.Sleeping || Engine.Frozen)
            {
                // the goose's engine is stopped, so the leaves couldn't fly: just take them away
                autumn.Suppress();
                Engine.ClearFrozenCanvas();
                return;
            }
            leafClicks += autumn.KickAll(Time.time);
        }

        // ---------------------------------------------------------------- hotkeys & cleanup

        private void RegisterHotkeys()
        {
            overlay.HotkeyPressed += id =>
            {
                char key = id == WM_HOTKEY_COME ? 'G' : id == WM_HOTKEY_HONK ? 'H' : id == WM_HOTKEY_PAUSE ? 'P' : 'M';
                if (poller != null) poller.MarkFired(key);
                OnHotkey(key, "WM_HOTKEY");
            };
            Register(WM_HOTKEY_COME, 'G');
            Register(WM_HOTKEY_HONK, 'H');
            Register(WM_HOTKEY_PAUSE, 'P');
            Register(WM_HOTKEY_MENU, 'M');
        }

        private void Register(int id, char key)
        {
            if (RegisterHotKey(overlay.Handle, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, key))
            {
                hotkeys.Add(id);
                hotkeyState[key] = "зарегистрирована";
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                hotkeyState[key] = "занята другой программой (ошибка " + err + "), работает запасной способ";
                Deluxe.Log("Hotkey Ctrl+Alt+" + key + " is taken by another program (error " + err + "), polling instead");
            }
        }

        private void OnHotkey(char key, string how)
        {
            lastHotkey = "Ctrl+Alt+" + key + " (" + how + ")";
            lastHotkeyAt = clock.Elapsed.TotalSeconds;
            switch (key)
            {
                case 'G': Guard("hotkey", Come); break;
                case 'H': Guard("hotkey", HonkNow); break;
                case 'P': Guard("hotkey", TogglePause); break;
                case 'M': Guard("hotkey", () => ControlPanel.Open(this)); break;
            }
        }

        private bool cleaned;

        private void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            try { if (timer != null) timer.Stop(); } catch { }
            try { if (tray != null) tray.Dispose(); } catch { }
            try { if (overlay != null) foreach (int id in hotkeys) UnregisterHotKey(overlay.Handle, id); } catch { }
            try { if (Deluxe.Friends != null) Deluxe.Friends.Stop(); } catch { }
            try { if (driftAudio != null) driftAudio.Dispose(); } catch { }
            try { if (speechAudio != null) speechAudio.Dispose(); } catch { }
        }

        private static Icon AppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { return null; }
        }
    }
}
