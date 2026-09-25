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
    internal sealed class Controller
    {
        public const string Version = "0.2.0";

        private const int WM_HOTKEY_COME = 1, WM_HOTKEY_HONK = 2, WM_HOTKEY_PAUSE = 3;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

        private DeluxeConfig cfg;
        private OverlayWindow overlay;
        private ParticleSystem particles;
        private GooseAnimator animator;
        private GooseRenderer renderer;
        private FixedTimestep timestep;
        private DeckFixer deckFixer;
        private Guests guests;
        private Tray tray;
        private System.Windows.Forms.Timer timer;
        private GooseEntity.RenderFunction originalRender;
        private GooseEntity.TickFunction originalTick;
        private readonly List<int> hotkeys = new List<int>();

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

            particles = new ParticleSystem();
            animator = new GooseAnimator(cfg, particles);
            renderer = new GooseRenderer(cfg);
            Deluxe.Particles = particles;
            Deluxe.Animator = animator;

            try { RussianPack.Apply(Deluxe.GooseDir, cfg.RussianNotes); }
            catch (Exception ex) { Deluxe.Log("Russian pack: " + ex.Message); }

            InjectionPoints.PreTickEvent += OnPreTick;
            InjectionPoints.PostTickEvent += OnPostTick;
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
                Guard("tray", () =>
                {
                    tray = new Tray(this, AppIcon());
                    Deluxe.Notify = (t, m) => tray.Balloon(t, m);
                });
            }
            if (cfg.Hotkeys) Guard("hotkeys", RegisterHotkeys);
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
            Deluxe.Log("Hooked. engine=" + Engine.Available + " settings=" + GooseSettings.Available + " deck=" + (deckFixer != null) +
                       " friends=" + (Deluxe.Friends != null ? GooseCode.Display(Deluxe.Friends.MyCode) : "off"));
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

        private void OnPostTick(GooseEntity goose)
        {
            if (!hooked || failed || guests == null) return;
            try { guests.Update(timestep != null ? timestep.LastSteps : 1); }
            catch (Exception ex) { Deluxe.Log("Visitors removed after an error: " + ex); guests.Clear(); }
        }

        private void OnPostRender(GooseEntity goose, Graphics unused)
        {
            if (!hooked || failed) return;
            try
            {
                float now = Time.time;
                float dt = lastRenderTime < 0f ? 1f / 60f : M.Clamp(now - lastRenderTime, 0f, 0.1f);
                lastRenderTime = now;
                DrawFrame(dt, now);
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
                    List<KeyValuePair<float, Action>> draws = new List<KeyValuePair<float, Action>>();

                    GooseEntity me = Deluxe.Goose;
                    animator.Asleep = Deluxe.Sleeping;
                    GoosePose mine = animator.Update(me, dt, now);
                    mine.carry = Deluxe.Carrying;
                    draws.Add(new KeyValuePair<float, Action>(me.position.y, () => renderer.Draw(g, mine, me, now, cfg.Hat, null)));

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
                        draws.Add(new KeyValuePair<float, Action>(v.Entity.position.y, () => renderer.Draw(g, pose, v.Entity, now, v.Look.Hat, label)));
                    }
                    draws.Sort((a, b) => a.Key.CompareTo(b.Key)); // lower on screen = closer = drawn last
                    foreach (KeyValuePair<float, Action> d in draws) d.Value();

                    particles.Update(dt, now);
                    particles.Draw(g, now);
                }
            }
            overlay.Present();
            if (!Deluxe.HiddenForFullscreen) overlay.KeepOnTop();
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
            try
            {
                if (originalRender != null) goose.render = originalRender;
                if (originalTick != null) goose.tick = originalTick;
                Deluxe.Sleeping = Deluxe.HiddenForFullscreen = false;
                Engine.Thaw();
                if (overlay != null) { overlay.Hide(); overlay.Dispose(); overlay = null; }
                if (timer != null) timer.Stop();
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
            DispatchMail();
            // while frozen the goose doesn't paint, so a sleeping goose is animated from here
            if (Engine.Frozen && Deluxe.Sleeping && !Deluxe.HiddenForFullscreen)
            {
                // the goose's own "hold ESC to quit" is frozen too; keep it working (not during fullscreen
                // games though, where ESC is often held to skip a cutscene)
                double now = clock.Elapsed.TotalSeconds;
                if ((GetAsyncKeyState(0x1B) & 0x8000) != 0)
                {
                    if (escSince < 0) escSince = now;
                    else if (now - escSince > 3) { Exit(); return; }
                }
                else escSince = -1;
                Time.TickTime();
                DrawFrame(0.1f, Time.time);
            }
            else escSince = -1;
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

        // ---------------------------------------------------------------- hotkeys & cleanup

        private void RegisterHotkeys()
        {
            overlay.HotkeyPressed += id =>
            {
                if (id == WM_HOTKEY_COME) Guard("hotkey", Come);
                else if (id == WM_HOTKEY_HONK) Guard("hotkey", HonkNow);
                else if (id == WM_HOTKEY_PAUSE) Guard("hotkey", TogglePause);
            };
            Register(WM_HOTKEY_COME, 'G');
            Register(WM_HOTKEY_HONK, 'H');
            Register(WM_HOTKEY_PAUSE, 'P');
        }

        private void Register(int id, char key)
        {
            if (RegisterHotKey(overlay.Handle, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, key)) hotkeys.Add(id);
            else Deluxe.Log("Hotkey Ctrl+Alt+" + key + " is taken by another program (error " + Marshal.GetLastWin32Error() + ")");
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
        }

        private static Icon AppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { return null; }
        }
    }
}
