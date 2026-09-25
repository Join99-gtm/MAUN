using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using GooseDeluxe;

namespace UiPreview
{
    /// <summary>A goose that only pretends: records what the panel asked for.</summary>
    internal sealed class FakeControls : IGooseControls
    {
        public readonly List<string> Calls = new List<string>();
        private readonly DeluxeConfig cfg = new DeluxeConfig();
        private bool paused, muted, steal = true;
        public DeluxeConfig Config { get { return cfg; } }
        public void Come() { Calls.Add("Come"); }
        public void HonkNow() { Calls.Add("Honk"); }
        public void RunGooseTask(string id) { Calls.Add("Task " + id); }
        public bool Paused { get { return paused; } }
        public void TogglePause() { paused = !paused; Calls.Add("Pause " + paused); }
        public void Exit() { Calls.Add("Exit"); }
        public bool GooseSettingsAvailable { get { return true; } }
        public bool Muted { get { return muted; } set { muted = value; Calls.Add("Muted " + value); } }
        public bool GooseMayStealMouse { get { return steal; } set { steal = value; Calls.Add("Steal " + value); } }
        public void ApplyConfig(string key) { Calls.Add("Apply " + key + "=" + cfg.ValueText(key)); }
        public bool FriendsEnabled { get { return true; } }
        public bool HasFriend { get { return true; } }
        public string FriendName { get { return "Вася"; } }
        public string MyCodeDisplay { get { return "GUS-7KQ2-M9XA-P4TD"; } }
        public void SendNote() { Calls.Add("SendNote"); }
        public void SendPicture() { Calls.Add("SendPicture"); }
        public void SendPrank(GooseCommand command) { Calls.Add("Prank " + command); }
        public void CopyMyCode() { }
        public void CopyInvite() { }
        public void EditFriend() { }
        public List<DiagItem> RunDiagnostics()
        {
            return new List<DiagItem>
            {
                new DiagItem(DiagLevel.Ok, "Мод загружен", "GooseDeluxe " + ModInfo.Version + " — C:\\Users\\koopi\\Desktop\\Гусь\\Assets\\Mods\\GooseDeluxe\\GooseDeluxe.dll"),
                new DiagItem(DiagLevel.Ok, "Рисование гуся", "окно 1920×1032, 64 кадров/с"),
                new DiagItem(DiagLevel.Ok, "Подключение к движку гуся", "пауза и прятки в играх работают полностью"),
                new DiagItem(DiagLevel.Ok, "Правильная скорость", "120 шагов в секунду (нужно около 120) при 64 кадрах/с"),
                new DiagItem(DiagLevel.Ok, "Значок у часов", "создан. Не видно? Нажми стрелку ^ у часов и перетащи гуся на панель задач"),
                new DiagItem(DiagLevel.Ok, "Горячие клавиши", "Ctrl+Alt+G: зарегистрирована; Ctrl+Alt+H: занята другой программой (ошибка 1409), работает запасной способ"),
                new DiagItem(DiagLevel.Info, "Зима", "сейчас не зима — снега нет"),
                new DiagItem(DiagLevel.Warn, "Гусиная почта", "нет связи с ntfy.sh, переподключаюсь"),
                new DiagItem(DiagLevel.Fail, "Пример ошибки", "так выглядит проваленная проверка"),
            };
        }
        public string LogTail(int lines) { return "2026-09-25 14:00:01 GooseDeluxe " + ModInfo.Version + " starting\n2026-09-25 14:00:02 Hooked. engine=True settings=True deck=True season=Autumn autumnMod=True friends=GUS-7KQ2-M9XA-P4TD"; }
        public void TestNote() { Calls.Add("TestNote"); }
        public void TestConnection(Action<string> report) { report("Записка ушла на ntfy.sh."); }
        public void TestSnow() { Calls.Add("TestSnow"); }
        public void OpenModFolder() { }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "panel0";
            string output = args.Length > 1 ? args[1] : "shot.png";
            Deluxe.ModDir = Path.GetTempPath();
            Application.EnableVisualStyles();
            FakeControls fake = new FakeControls();
            Form form;
            switch (mode)
            {
                case "panel0": case "panel1": case "panel2":
                    ControlPanel.Open(fake, mode[5] - '0');
                    form = Application.OpenForms[0];
                    break;
                case "send": form = new SendNoteDialog("Вася"); break;
                case "friend": form = new FriendDialog("ABCDEFGHJKMN", "Вася", "Коля", "7KQ2M9XAP4TD"); break;
                case "note":
                    form = new NoteForm(new DeliveryPayload { FromName = "Вася", FromCode = "ABCDEFGHJKMN", Text = "Привет! Как там мой гусь у тебя?\nНе обижает?" }, p => { });
                    break;
                case "clicks":
                    return ClickTest(fake);
                default: Console.Error.WriteLine("unknown mode"); return 2;
            }
            if (!form.Visible) form.Show();
            form.Location = new Point(20, 20);
            Timer t = new Timer { Interval = 1500 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                Rectangle b = form.Bounds;
                Process p = Process.Start(new ProcessStartInfo("import", "-window root -crop " + (b.Width + 20) + "x" + (b.Height + 20) + "+10+10 \"" + output + "\"") { UseShellExecute = false });
                p.WaitForExit();
                Console.WriteLine(mode + ": " + b.Width + "x" + b.Height + " -> " + output + " (controls: " + Count(form) + ")");
                Application.Exit();
            };
            t.Start();
            Application.Run();
            return 0;
        }

        private static int Count(Control c)
        {
            int n = 1;
            foreach (Control k in c.Controls) n += Count(k);
            return n;
        }

        /// <summary>Clicks every button and toggles every setting of the panel; checks each reaches the goose.</summary>
        private static int ClickTest(FakeControls fake)
        {
            ControlPanel.Open(fake, 0);
            Form panel = Application.OpenForms[0];
            int fails = 0;
            List<Button> buttons = new List<Button>();
            List<CheckBox> checks = new List<CheckBox>();
            List<ComboBox> combos = new List<ComboBox>();
            List<TrackBar> tracks = new List<TrackBar>();
            Collect(panel, buttons, checks, combos, tracks);
            if (tracks.Count > 0 && tracks[0].Value != 10) { Console.WriteLine("FAIL size slider not loaded on open: " + tracks[0].Value); fails++; }
            foreach (Control l in AllControls(panel))
                if (l is Label && l.Text.StartsWith("Друг: Вася")) goto friendOk;
            Console.WriteLine("FAIL friend line empty on open"); fails++;
            friendOk:
            Application.DoEvents();
            string[] expect = { "Come", "Honk", "Task CollectMeme", "Task CollectNotepad", "Task TrackMud", "Task NabMouse", "Pause True",
                                "SendNote", "SendPicture", "Prank Honk", "Prank Meme", "Prank Mud", "Prank Steal", "TestNote", "TestSnow" };
            foreach (Button b in buttons)
            {
                if (b.Text.StartsWith("Выгнать") || b.Text.StartsWith("Скопировать") || b.Text.StartsWith("Папка") || b.Text.StartsWith("Добавить")) continue;
                // WinForms only clicks visible buttons: bring the button's tab to the front first
                for (Control p = b.Parent; p != null; p = p.Parent)
                    if (p is TabPage) ((TabControl)p.Parent).SelectedTab = (TabPage)p;
                Application.DoEvents();
                b.PerformClick();
                Application.DoEvents();
            }
            foreach (string e in expect)
                if (!fake.Calls.Contains(e)) { Console.WriteLine("FAIL button did not reach the goose: " + e); fails++; }
            int applied = 0;
            foreach (CheckBox c in checks)
            {
                int before = fake.Calls.Count;
                c.Checked = !c.Checked;
                Application.DoEvents();
                if (fake.Calls.Count > before) applied++;
                else { Console.WriteLine("FAIL checkbox did nothing: " + c.Text); fails++; }
            }
            foreach (ComboBox cb in combos)
            {
                int before = fake.Calls.Count;
                cb.SelectedIndex = (cb.SelectedIndex + 1) % cb.Items.Count;
                Application.DoEvents();
                if (fake.Calls.Count == before) { Console.WriteLine("FAIL list did nothing: " + string.Join("/", ItemsOf(cb))); fails++; }
            }
            foreach (TrackBar tb in tracks)
            {
                int before = fake.Calls.Count;
                tb.Value = 20;
                Application.DoEvents();
                if (fake.Calls.Count == before) { Console.WriteLine("FAIL size slider did nothing"); fails++; }
            }
            Console.WriteLine("buttons " + buttons.Count + ", checkboxes " + checks.Count + " (" + applied + " applied), lists " + combos.Count + ", sliders " + tracks.Count);
            Console.WriteLine("calls: " + string.Join(" | ", fake.Calls));
            DeluxeConfig c2 = fake.Config;
            if (c2.Scale != 2.0f) { Console.WriteLine("FAIL scale not applied: " + c2.Scale); fails++; }
            Console.WriteLine(fails == 0 ? "CLICK TEST PASSED" : "CLICK TEST FAILURES: " + fails);
            return fails == 0 ? 0 : 1;
        }

        private static IEnumerable<Control> AllControls(Control c)
        {
            foreach (Control k in c.Controls)
            {
                yield return k;
                foreach (Control x in AllControls(k)) yield return x;
            }
        }

        private static IEnumerable<string> ItemsOf(ComboBox cb) { foreach (object o in cb.Items) yield return o.ToString(); }

        private static void Collect(Control c, List<Button> b, List<CheckBox> ch, List<ComboBox> co, List<TrackBar> tr)
        {
            foreach (Control k in c.Controls)
            {
                if (k is Button) b.Add((Button)k);
                else if (k is CheckBox) ch.Add((CheckBox)k);
                else if (k is ComboBox) co.Add((ComboBox)k);
                else if (k is TrackBar) tr.Add((TrackBar)k);
                Collect(k, b, ch, co, tr);
            }
        }
    }
}
