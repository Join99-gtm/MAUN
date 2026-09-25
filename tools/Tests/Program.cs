using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GooseDeluxe;
using GooseShared;
using SamEngine;

namespace Tests
{
    internal static class Program
    {
        private static int passed, failed;
        private static readonly string Tmp = Path.Combine(Path.GetTempPath(), "goosedeluxe-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--write-default-ini")
            {
                File.WriteAllText(args[1], new DeluxeConfig().ToIni(null), new UTF8Encoding(true));
                return 0;
            }
            Directory.CreateDirectory(Tmp);
            Deluxe.ModDir = Tmp;
            Run("Deck (honest random)", TestDeck);
            Run("Fixed timestep", TestTimestep);
            Run("Goose codes", TestCodes);
            Run("Protocol", TestProtocol);
            Run("Config", TestConfig);
            Run("Russian pack", TestRussianPack);
            Run("Task: come here", TestCome);
            Run("Task: carry to a friend", TestCarry);
            Run("Task: deliver a note", TestDeliver);
            Run("Visiting geese", TestGuests);
            Run("Seasons: calendar and settings", TestSeasonClock);
            Run("Seasons: winter scene", TestWinter);
            Run("Task: run through a snowdrift", TestSnowdriftRun);
            Run("Пульт: горячие клавиши, клик по гусю, сохранение, отчёт", TestPanelLogic);
            string gooseDir = args.Length > 1 ? args[1] : null;
            if (gooseDir != null) Run("Seasons: the real Autumn mod's leaves", () => TestAutumnMod(gooseDir));
            else Console.WriteLine("(skipping Autumn mod test: pass the goose folder as the 2nd argument)");
            string mock = args.Length > 0 ? args[0] : null;
            if (mock != null) Run("ntfy end-to-end (mock server)", () => NetTests.Run(mock, Check));
            else Console.WriteLine("(skipping network tests: pass the path to mock_ntfy.py)");
            Console.WriteLine();
            Console.WriteLine((failed == 0 ? "ALL PASSED" : "FAILURES: " + failed) + " (" + passed + " checks passed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            Console.WriteLine("== " + name);
            try { test(); }
            catch (Exception ex) { Check("no exception in " + name, false, ex.ToString()); }
        }

        public static void Check(string what, bool ok, string detail = null)
        {
            if (ok) passed++; else failed++;
            Console.WriteLine((ok ? "   ok   " : "   FAIL ") + what + (detail != null ? "  [" + detail + "]" : ""));
        }

        // ------------------------------------------------------------------ deck

        private static List<int> Draw(Deck deck, DeckFixer fixer, int n)
        {
            List<int> seq = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                if (fixer != null) fixer.Update(); // once per frame, before the goose may draw
                seq.Add(deck.Next());
            }
            return seq;
        }

        private static Dictionary<string, int> Orders(List<int> seq, int size)
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            for (int i = 0; i + size <= seq.Count; i += size)
            {
                string key = string.Join("", seq.Skip(i).Take(size));
                d[key] = d.TryGetValue(key, out int c) ? c + 1 : 1;
            }
            return d;
        }

        private static void TestDeck()
        {
            // the bug, reproduced with the goose's own SamEngine.Deck
            Dictionary<string, int> original = Orders(Draw(new Deck(3), null, 30000), 3);
            Check("original Deck(3): only " + original.Count + " of 6 orders ever happen", original.Count == 2, string.Join(" ", original.Keys));

            Deck deck = new Deck(3);
            DeckFixer fixer = new DeckFixer(() => deck, new Random(1234));
            List<int> seq = Draw(deck, fixer, 60000);
            Dictionary<string, int> fixedOrders = Orders(seq, 3);
            Check("fixed Deck(3): all 6 orders happen", fixedOrders.Count == 6, string.Join(" ", fixedOrders.Select(kv => kv.Key + "=" + kv.Value)));
            // the no-repeat rule removes the orders starting with the previous deck's last card, so expect roughly
            // (not exactly) uniform frequencies: every order between 12% and 22% of decks
            int decks = seq.Count / 3;
            Check("fixed Deck(3): no order is starved or dominant", fixedOrders.Values.All(v => v > decks * 0.12 && v < decks * 0.22));
            bool repeats = false;
            for (int i = 1; i < seq.Count; i++) if (seq[i] == seq[i - 1]) repeats = true;
            Check("fixed deck never does the same trick twice in a row", !repeats);
            Check("fixer ran once per deck", fixer.Fixes == decks, fixer.Fixes + " vs " + decks);

            Deck deck4 = new Deck(4);
            DeckFixer fixer4 = new DeckFixer(() => deck4, new Random(99));
            Dictionary<string, int> o4 = Orders(Draw(deck4, fixer4, 4 * 20000), 4);
            Dictionary<string, int> o4orig = Orders(Draw(new Deck(4), null, 4 * 20000), 4);
            Check("Deck(4): original " + o4orig.Count + "/24 orders, fixed " + o4.Count + "/24", o4orig.Count == 6 && o4.Count == 24);
        }

        // ------------------------------------------------------------------ timestep

        private static int Simulate(double fps, double seconds, bool enabled = true)
        {
            double t = 0;
            int calls = 0;
            FixedTimestep ts = new FixedTimestep(g => calls++, () => t) { Enabled = enabled };
            ts.Tick(null); // first frame: no history yet, not part of the measurement
            calls = 0;
            int frames = (int)Math.Round(fps * seconds);
            for (int f = 0; f < frames; f++) { t += 1.0 / fps; ts.Tick(null); }
            return calls;
        }

        private static void TestTimestep()
        {
            foreach (double fps in new[] { 30.0, 64.0, 100.0, 144.0 })
            {
                int calls = Simulate(fps, 10);
                Check("10 s at " + fps + " FPS -> 1200 goose ticks (got " + calls + ")", Math.Abs(calls - 1200) <= 2);
            }
            Check("disabled: one tick per frame", Simulate(64, 10, false) == 640);
            Check("a whole hour at 64 FPS drifts by less than a tick", Math.Abs(Simulate(64, 3600) - 432000) <= 1);

            double t = 0;
            int n = 0;
            FixedTimestep ts = new FixedTimestep(g => n++, () => t);
            ts.Tick(null);
            t += 3.0; // the PC hung for 3 s
            ts.Tick(null);
            Check("a 3 s stall catches up at most 8 steps", ts.LastSteps == FixedTimestep.MaxStepsPerFrame, ts.LastSteps.ToString());

            List<bool> seen = new List<bool>();
            t = 0;
            FixedTimestep click = new FixedTimestep(g => seen.Add(Input.leftMouseButton.Clicked), () => t);
            click.Tick(null);
            seen.Clear();
            Input.leftMouseButton = new ButtonState { Held = true, Clicked = true };
            t += 3.0 / 120.0 + 1e-6;
            click.Tick(null);
            Check("a click is seen by the first sub-step only", seen.Count == 3 && seen[0] && !seen[1] && !seen[2], string.Join(",", seen));
            Check("the click is still there for other mods afterwards", Input.leftMouseButton.Clicked);
            Input.leftMouseButton = new ButtonState();

            int before = n;
            Deluxe.Sleeping = true;
            t += 1.0;
            ts.Tick(null);
            Deluxe.Sleeping = false;
            Check("paused: the goose doesn't tick", n == before && ts.LastSteps == 0);
            t += 1.0 / 60;
            ts.Tick(null);
            Check("after a pause it resumes without a time jump", ts.LastSteps <= 2, ts.LastSteps.ToString());
        }

        // ------------------------------------------------------------------ codes

        private static void TestCodes()
        {
            HashSet<string> codes = new HashSet<string>();
            bool allValid = true;
            for (int i = 0; i < 2000; i++)
            {
                string c = GooseCode.Generate();
                codes.Add(c);
                if (GooseCode.Normalize(c) != c || c.Length != 12) allValid = false;
            }
            Check("2000 generated codes are valid and unique", allValid && codes.Count == 2000);
            Check("Normalize(gus-abcd-efgh-jkmn)", GooseCode.Normalize("gus-abcd-efgh-jkmn") == "ABCDEFGHJKMN");
            Check("Normalize with spaces", GooseCode.Normalize("  GUS ABCD EFGH JKMN ") == "ABCDEFGHJKMN");
            Check("Normalize a pasted invite line", GooseCode.Normalize("меню гуся → код GUS-ABCD-EFGH-JKMN, жми") == "ABCDEFGHJKMN");
            Check("confusable letters rejected (O/0/I/1)", GooseCode.Normalize("GUS-ABCD-EFGH-JKO0") == null && GooseCode.Normalize("ABCDEFGHJK1I") == null);
            Check("wrong length rejected", GooseCode.Normalize("ABCD-EFGH") == null && GooseCode.Normalize("") == null && GooseCode.Normalize(null) == null);
            Check("Display", GooseCode.Display("ABCDEFGHJKMN") == "GUS-ABCD-EFGH-JKMN");
            Check("Topic", GooseCode.Topic("ABCDEFGHJKMN") == "goosedeluxe-abcdefghjkmn");
        }

        // ------------------------------------------------------------------ protocol

        private static Incoming P(string json, string server = "https://ntfy.sh")
        {
            return FriendProtocol.Parse(NtfyMessage.Parse(json), server);
        }

        private static void TestProtocol()
        {
            var cmds = new Dictionary<string, GooseCommand>
            {
                { "га", GooseCommand.Honk }, { "ГА-ГА-ГА!!!", GooseCommand.Honk }, { "гагага", GooseCommand.Honk }, { "Honk", GooseCommand.Honk },
                { "мем", GooseCommand.Meme }, { " Мем! ", GooseCommand.Meme }, { "ко мне", GooseCommand.Come }, { "Кража", GooseCommand.Steal },
                { "ЗАПИСКА", GooseCommand.Note }, { "грязь", GooseCommand.Mud }, { "привет", GooseCommand.None }, { "га где хлеб", GooseCommand.None },
                { "", GooseCommand.None }, { "мем мем мем мем мем мем мем", GooseCommand.None },
            };
            bool allOk = true;
            foreach (var kv in cmds)
                if (FriendProtocol.ParseCommand(kv.Key) != kv.Value) { allOk = false; Console.WriteLine("      '" + kv.Key + "' -> " + FriendProtocol.ParseCommand(kv.Key)); }
            Check("command words (" + cmds.Count + " cases)", allOk);
            foreach (GooseCommand c in Enum.GetValues(typeof(GooseCommand)))
                if (c != GooseCommand.None && FriendProtocol.ParseCommand(FriendProtocol.CommandWord(c)) != c) allOk = false;
            Check("every command word parses back", allOk);

            string tags = FriendProtocol.Tags("ABCDEFGHJKMN", new[] { "#FFFFFF", "#ff0000", "#000000" }, HatStyle.Santa);
            Check("tags", tags == "gd,from-abcdefghjkmn,c-ffffff-ff0000-000000,hat-santa", tags);

            Incoming note = P("{\"id\":\"a1\",\"time\":1,\"event\":\"message\",\"topic\":\"t\",\"message\":\"Привет\\nмир\",\"title\":\"Вася\",\"tags\":[\"gd\",\"from-abcdefghjkmn\",\"c-ffffff-ff0000-000000\",\"hat-santa\"]}");
            Check("goose note: kind/text", note != null && note.Kind == IncomingKind.Note && note.Text == "Привет\nмир");
            Check("goose note: sender code and name", note != null && note.FromCode == "ABCDEFGHJKMN" && note.FromName == "Вася");
            Check("goose note: visitor look", note != null && note.Guest != null && note.Guest.Name == "Вася" && note.Guest.Orange == "#ff0000" && note.Guest.Hat == HatStyle.Santa);

            Incoming phone = P("{\"id\":\"b\",\"event\":\"message\",\"message\":\"мем\"}");
            Check("phone command: no visitor, no reply address", phone != null && phone.Kind == IncomingKind.Command && phone.Command == GooseCommand.Meme && phone.Guest == null && phone.FromCode == null);
            Incoming noFrom = P("{\"id\":\"c\",\"event\":\"message\",\"message\":\"hi\",\"tags\":[\"gd\"]}");
            Check("gd tag without sender is treated as a phone message", noFrom != null && noFrom.Guest == null && noFrom.Kind == IncomingKind.Note);
            Check("keepalive / open ignored", P("{\"id\":\"k\",\"event\":\"keepalive\"}") == null && P("{\"id\":\"o\",\"event\":\"open\"}") == null);
            Check("garbage ignored", P("not json") == null && P("{\"event\":\"message\",\"message\":\"   \"}") == null);

            Incoming img = P("{\"id\":\"d\",\"event\":\"message\",\"message\":\"You received a file: cat.png\",\"attachment\":{\"name\":\"cat.png\",\"type\":\"image/png\",\"size\":1000,\"url\":\"https://ntfy.sh/file/xyz.png\"}}");
            Check("image on the server", img != null && img.Kind == IncomingKind.Image && img.AttachmentUrl == "https://ntfy.sh/file/xyz.png" && img.Text == null && img.FileName == "cat.png");
            Incoming evil = P("{\"id\":\"e\",\"event\":\"message\",\"attachment\":{\"name\":\"x.png\",\"type\":\"image/png\",\"size\":10,\"url\":\"https://evil.example/file/x.png\"}}");
            Check("attachment on another host is never downloaded", evil != null && evil.Kind == IncomingKind.UnsupportedFile && evil.AttachmentUrl == null);
            Incoming downgrade = P("{\"id\":\"f\",\"event\":\"message\",\"attachment\":{\"name\":\"x.png\",\"type\":\"image/png\",\"size\":10,\"url\":\"http://ntfy.sh/file/x.png\"}}");
            Check("http attachment on an https server rejected", downgrade != null && downgrade.Kind == IncomingKind.UnsupportedFile);
            Incoming webp = P("{\"id\":\"g\",\"event\":\"message\",\"attachment\":{\"name\":\"x.webp\",\"type\":\"image/webp\",\"size\":10,\"url\":\"https://ntfy.sh/file/x.webp\"}}");
            Check("webp is a file the goose can't carry", webp != null && webp.Kind == IncomingKind.UnsupportedFile);
            Incoming huge = P("{\"id\":\"h\",\"event\":\"message\",\"attachment\":{\"name\":\"x.png\",\"type\":\"image/png\",\"size\":9000000,\"url\":\"https://ntfy.sh/file/x.png\"}}");
            Check("images over 8 MB refused", huge != null && huge.Kind == IncomingKind.UnsupportedFile);
            Incoming ctl = P("{\"id\":\"i\",\"event\":\"message\",\"message\":\"a\\u0007b\\r\\nc\"}");
            Check("control characters stripped", ctl != null && ctl.Text == "ab\nc", ctl != null ? ctl.Text : "null");
            Incoming longText = P("{\"id\":\"j\",\"event\":\"message\",\"message\":\"" + new string('я', 2000) + "\"}");
            Check("text capped at 500 chars", longText != null && longText.Text.Length == 500);
            Check("colour parsing", FriendProtocol.ToColor("#ff8000", System.Drawing.Color.Black).ToArgb() == System.Drawing.Color.FromArgb(255, 255, 128, 0).ToArgb()
                                   && FriendProtocol.ToColor("oops", System.Drawing.Color.Black).ToArgb() == System.Drawing.Color.Black.ToArgb());
        }

        // ------------------------------------------------------------------ config

        private static void TestConfig()
        {
            string path = Path.Combine(Tmp, "GooseDeluxe.ini");
            // the file v0.1 wrote, edited by the user
            File.WriteAllText(path,
                "; GooseDeluxe settings. True/False, restart the goose to apply.\r\nAntiAlias=True\r\nWings=False\r\nHat=Santa\r\nScale=1,5\r\nFoo=bar\r\n",
                new UTF8Encoding(true));
            DeluxeConfig c = DeluxeConfig.Load(path);
            Check("old values kept (Wings=False, Hat=Santa, Scale=1,5)", !c.Wings && c.Hat == HatStyle.Santa && Math.Abs(c.Scale - 1.5f) < 1e-6);
            Check("new settings default on", c.FixSpeed && c.HonestRandom && c.PauseInFullscreen && c.Tray && c.Friends && c.Language == "RU");
            string text = File.ReadAllText(path);
            Check("missing keys appended for editing", text.Contains("FixSpeed=True") && text.Contains("NtfyServer=https://ntfy.sh") && text.Contains("добавлено GooseDeluxe 0.2"));
            Check("user's lines untouched (incl. unknown key)", text.Contains("Wings=False") && text.Contains("Foo=bar") && text.Contains("Scale=1,5"));
            Check("keys not duplicated", text.Split('\n').Count(l => l.StartsWith("Hat=")) == 1 && text.Split('\n').Count(l => l.StartsWith("AntiAlias=")) == 1);
            long len = new FileInfo(path).Length;
            DeluxeConfig.Load(path);
            Check("second load appends nothing", new FileInfo(path).Length == len);

            File.WriteAllText(path, "NtfyServer=javascript:alert(1)\r\nLanguage=Klingon\r\n");
            c = DeluxeConfig.Load(path);
            Check("bad server URL and language ignored", c.NtfyServer == "https://ntfy.sh" && c.Language == "RU");
            File.Delete(path);
            c = DeluxeConfig.Load(path);
            Check("fresh ini written with every key", File.Exists(path) && File.ReadAllText(path).Contains("FriendCanStealMouse=True"));
        }

        // ------------------------------------------------------------------ russian pack

        private static void TestRussianPack()
        {
            string goose = Path.Combine(Tmp, "goose");
            string notes = Path.Combine(goose, "Assets", "Text", "NotepadMessages");
            Directory.CreateDirectory(notes);
            string[] english = { "am goose.txt", "good work.txt", "gooseASCII1.txt", "hard to type.txt", "i cause problems.txt", "peace was never.txt" };
            foreach (string e in english) File.WriteAllText(Path.Combine(notes, e), "english");

            RussianPack.Apply(goose, true);
            string[] txt = Directory.GetFiles(notes, "*.txt").Select(Path.GetFileName).ToArray();
            Check("15 Russian notes added", txt.Count(n => n.StartsWith("ru-")) == 15);
            Check("English notes parked, the ASCII goose stays", txt.Contains("gooseASCII1.txt") && !txt.Contains("am goose.txt") && File.Exists(Path.Combine(notes, "am goose.txt.en")));
            byte[] first = File.ReadAllBytes(Path.Combine(notes, "ru-01.txt"));
            Check("notes are UTF-8 with BOM", first.Length > 3 && first[0] == 0xEF && first[1] == 0xBB && first[2] == 0xBF && Encoding.UTF8.GetString(first, 3, first.Length - 3) == "я гусь. га.");
            RussianPack.Apply(goose, true);
            Check("applying twice changes nothing", Directory.GetFiles(notes).Length == 15 + 6);

            File.WriteAllText(Path.Combine(notes, "ru-02.txt"), "моя записка", new UTF8Encoding(true));
            RussianPack.Apply(goose, false);
            txt = Directory.GetFiles(notes).Select(Path.GetFileName).ToArray();
            Check("switching off restores the English notes", english.All(e => txt.Contains(e)) && !txt.Any(n => n.EndsWith(".en")));
            Check("switching off removes our notes but keeps an edited one", txt.Count(n => n.StartsWith("ru-")) == 1 && txt.Contains("ru-02.txt"));
        }

        // ------------------------------------------------------------------ tasks

        private static void TestCome()
        {
            FakeWorld w = new FakeWorld();
            GooseEntity g = w.NewGoose(new Vector2(300f, 500f));
            Deluxe.Goose = g;
            Input.mouseX = 900; Input.mouseY = 300;
            Check("switch to Come", Deluxe.SetTask(ComeTask.Id, false) && w.TaskOf(g) == ComeTask.Id);
            w.Run(g, 10f, () => w.TaskOf(g) == "Wander");
            float d = Vector2.Distance(g.position, new Vector2(900f, 300f));
            // it stops ~50 px below and beside the cursor, so that its head is next to it
            Check("the goose ran up to the cursor", d < 70f, "distance " + d.ToString("0"));
            Check("and honked once", w.Honks == 1, w.Honks.ToString());
            Check("then went back to wandering", w.TaskOf(g) == "Wander");
        }

        private static void TestCarry()
        {
            foreach (bool succeed in new[] { true, false })
            {
                FakeWorld w = new FakeWorld();
                GooseEntity g = w.NewGoose(new Vector2(500f, 400f));
                Deluxe.Goose = g;
                CarryJob job = new CarryJob { Kind = CarryKind.Note, To = "Вася" };
                CarryTask.Pending = job;
                Deluxe.SetTask(CarryTask.Id, false);
                string tag = succeed ? "[sent] " : "[failed] ";
                Check(tag + "note in the beak from the start", Deluxe.Carrying == CarryKind.Note);
                w.Run(g, 8f, () => g.position.x <= -79f);
                Check(tag + "ran off the nearest (left) side", g.position.x <= -79f, g.position.x.ToString("0"));
                w.Run(g, 0.1f);
                Check(tag + "beak empty behind the edge", Deluxe.Carrying == CarryKind.None);
                w.Run(g, 3f);
                Check(tag + "waits off-screen while the upload runs", g.position.x <= -79f && w.TaskOf(g) == CarryTask.Id);
                job.State = succeed ? CarryJob.Sent : CarryJob.Failed;
                bool carriedBack = false;
                w.Run(g, 10f, () => { if (Deluxe.Carrying == CarryKind.Note) carriedBack = true; return w.TaskOf(g) == "Wander"; });
                Check(tag + "came back and is wandering again", w.TaskOf(g) == "Wander" && g.position.x > 50f);
                if (succeed)
                {
                    Check("[sent] came back empty and honked", !carriedBack && w.Honks == 1);
                }
                else
                {
                    Check("[failed] came back still holding the note", carriedBack && w.Honks == 0);
                    Check("[failed] the user is told why", w.Notifications.Any(n => n.Contains("Не получилось")));
                }
                Check(tag + "beak empty at the end", Deluxe.Carrying == CarryKind.None);
            }
        }

        private static void TestDeliver()
        {
            FakeWorld w = new FakeWorld();
            GooseEntity g = w.NewGoose(new Vector2(900f, 400f));
            Deluxe.Goose = g;
            DeliverTask.Pending = new DeliveryPayload { FromName = "телефон", Text = "Привет!" };
            Deluxe.SetTask(DeliverTask.Id, false);
            w.Run(g, 40f, () => w.Windows.Count > 0 && w.Windows[0].Moves.Count > 120);
            if (w.Windows.Count > 0 && w.Windows[0].Moves.Count > 0)
            {
                // mid-drag, coming from the right: the window's left edge, half-way down, is in the beak
                System.Drawing.Point now = w.Windows[0].Moves.Last();
                Vector2 beak = Deluxe.BeakOf(g);
                Check("mid-drag the window hangs from the beak", Math.Abs(now.X - beak.x) <= 3 && Math.Abs(now.Y + 90 - beak.y) <= 3, now + " vs beak " + beak.x.ToString("0") + "," + beak.y.ToString("0"));
            }
            w.Run(g, 40f, () => w.TaskOf(g) == "Wander");
            FakeWindow win = w.Windows.FirstOrDefault();
            Check("a note window was created and shown", win != null && win.Shown && win.Payload.Text == "Привет!");
            if (win != null && win.Moves.Count > 0)
            {
                System.Drawing.Point last = win.Moves.Last();
                Check("dragged in from the right edge", win.Moves.First().X > FakeWorld.Screen.x - 40 && last.X < win.Moves.First().X, win.Moves.First() + " -> " + last);
                Check("ends fully on screen", last.X >= 0 && last.X + win.Width <= FakeWorld.Screen.x && last.Y >= 0 && last.Y + win.Height <= FakeWorld.Screen.y, last.ToString());
            }
            Check("honked on delivery and is wandering", w.Honks == 1 && w.TaskOf(g) == "Wander");

            // the user closes the note while it's being dragged
            FakeWorld w2 = new FakeWorld();
            GooseEntity g2 = w2.NewGoose(new Vector2(300f, 300f));
            Deluxe.Goose = g2;
            DeliverTask.Pending = new DeliveryPayload { Text = "x" };
            Deluxe.SetTask(DeliverTask.Id, false);
            w2.Run(g2, 20f, () => w2.Windows.Count > 0 && w2.Windows[0].Moves.Count > 30);
            w2.Windows[0].Gone = true;
            w2.Run(g2, 1f);
            Check("closing the note mid-drag frees the goose, no honk", w2.TaskOf(g2) == "Wander" && w2.Honks == 0);
        }

        // ------------------------------------------------------------------ seasons

        private static void TestSeasonClock()
        {
            Season[] expected = { Season.Winter, Season.Winter, Season.Spring, Season.Spring, Season.Spring, Season.Summer,
                                  Season.Summer, Season.Summer, Season.Autumn, Season.Autumn, Season.Autumn, Season.Winter };
            bool monthsOk = true;
            for (int m = 1; m <= 12; m++) if (SeasonClock.Of(new DateTime(2026, m, 15)) != expected[m - 1]) monthsOk = false;
            Check("every month maps to its season", monthsOk);
            Check("winter in Dec/Jan/Feb, autumn in Sep-Nov", SeasonClock.Of(new DateTime(2026, 12, 1)) == Season.Winter && SeasonClock.Of(new DateTime(2027, 2, 28)) == Season.Winter
                  && SeasonClock.Of(new DateTime(2026, 9, 1)) == Season.Autumn && SeasonClock.Of(new DateTime(2026, 11, 30)) == Season.Autumn
                  && SeasonClock.Of(new DateTime(2026, 3, 1)) == Season.Spring && SeasonClock.Of(new DateTime(2026, 8, 31)) == Season.Summer);
            Func<DateTime> saved = SeasonClock.Now;
            SeasonClock.Now = () => new DateTime(2026, 9, 25);
            Check("Auto follows the date (September -> autumn)", SeasonClock.Current("Auto") == Season.Autumn);
            SeasonClock.Now = () => new DateTime(2027, 1, 5);
            Check("changing the date to January -> winter", SeasonClock.Current("Auto") == Season.Winter);
            Check("forced season wins over the date", SeasonClock.Current("Summer") == Season.Summer);
            Check("Off -> no seasons at all", SeasonClock.Current("Off") == Season.None);
            SeasonClock.Now = saved;
            Check("New Year window 20 Dec - 10 Jan", SeasonClock.IsNewYear(new DateTime(2026, 12, 20)) && SeasonClock.IsNewYear(new DateTime(2027, 1, 10))
                  && !SeasonClock.IsNewYear(new DateTime(2026, 12, 19)) && !SeasonClock.IsNewYear(new DateTime(2027, 1, 11)));
            Check("Russian setting names", SeasonClock.NormalizeSetting("Зима") == "Winter" && SeasonClock.NormalizeSetting(" осень ") == "Autumn"
                  && SeasonClock.NormalizeSetting("выкл") == "Off" && SeasonClock.NormalizeSetting("когда-нибудь") == null);

            string path = Path.Combine(Tmp, "Seasons.ini");
            File.WriteAllText(path, new DeluxeConfig().ToIni(null).Replace("Seasons=Auto", "Seasons=Зима"), new UTF8Encoding(true));
            Check("ini: Seasons=Зима is understood", DeluxeConfig.Load(path).Seasons == "Winter");
            File.WriteAllText(path, "Seasons=когда-нибудь\r\n");
            Check("ini: nonsense season falls back to Auto", DeluxeConfig.Load(path).Seasons == "Auto");
            // the file 0.2 wrote: only the season keys are missing
            string v02 = new DeluxeConfig().ToIni(null);
            v02 = string.Join("\n", v02.Split('\n').Where(l => !l.StartsWith("Seasons=") && !l.StartsWith("WinterScarf=") && !l.StartsWith("NewYearHat=")));
            File.WriteAllText(path, v02, new UTF8Encoding(true));
            DeluxeConfig.Load(path);
            string after = File.ReadAllText(path);
            Check("ini from 0.2 gets the three season keys, marked 0.3", after.Contains("добавлено GooseDeluxe 0.3") && after.Contains("Seasons=Auto") && after.Contains("WinterScarf=True") && after.Contains("NewYearHat=True"));
        }

        private static GooseEntity Walker(Vector2 at)
        {
            GooseEntity g = new GooseEntity(e => { }, (r, p, d) => { }, (e, gfx) => { });
            g.position = at;
            g.rig.feets = new ProceduralFeets();
            return g;
        }

        private static void TestWinter()
        {
            Vector2 screen = new Vector2(1280f, 720f);
            WinterScene w = new WinterScene(new Random(5));
            ParticleSystem ps = new ParticleSystem();
            List<GooseEntity> nobody = new List<GooseEntity>();
            float t = 0f;
            Action<float> run = seconds => { for (int i = 0; i < (int)(seconds * 60); i++) { t += 1f / 60f; w.Update(1f / 60f, t, screen, nobody, ps, 1f); ps.Update(1f / 60f, t); } };

            run(30f);
            Check("not winter: no snow at all", w.FlakeCount == 0 && w.Drifts.Count == 0 && w.Bank == 0f);
            w.Active = true;
            int maxFlakes = 0; float maxIntensity = 0f;
            for (int k = 0; k < 600; k++) { run(1f); maxFlakes = Math.Max(maxFlakes, w.FlakeCount); maxIntensity = Math.Max(maxIntensity, w.Intensity); }
            Check("winter: it snows (" + maxFlakes + " flakes at the peak)", maxFlakes >= 30 && maxFlakes <= 140);
            Check("winter: snowfall comes and goes", maxIntensity > 0.8f);
            Check("winter: drifts appear (" + w.Drifts.Count + ")", w.Drifts.Count >= 1 && w.Drifts.Count <= WinterScene.MaxDrifts);
            Check("winter: snow piles up along the bottom (" + w.Bank.ToString("0.0") + " px)", w.Bank > 3f && w.Bank <= WinterScene.MaxBank);

            SnowDrift d = w.PickDrift();
            GooseEntity goose = Walker(new Vector2(d.pos.x - 200f, d.pos.y));
            List<GooseEntity> geese = new List<GooseEntity> { goose };
            int before = ps.Count;
            w.Update(1f / 60f, t += 1f / 60f, screen, geese, ps, 1f);
            Check("a goose far away doesn't touch the drift", !d.Kicked);
            goose.position = d.pos;
            goose.velocity = new Vector2(400f, 0f);
            w.Update(1f / 60f, t += 1f / 60f, screen, geese, ps, 1f);
            Check("charging into a drift bursts it into snow", d.Kicked && ps.Count > before + 20, "particles " + before + " -> " + ps.Count);
            for (int i = 0; i < 60; i++) w.Update(1f / 60f, t += 1f / 60f, screen, geese, ps, 1f);
            Check("the burst drift is gone", !w.Drifts.Contains(d));

            goose.rig.feets.lFootMoveTimeStart = t;          // left foot in the air
            w.Update(1f / 60f, t += 1f / 60f, screen, geese, ps, 1f);
            goose.rig.feets.lFootMoveTimeStart = -1f;        // ... and down
            w.Update(1f / 60f, t += 1f / 60f, screen, geese, ps, 1f);
            Check("a step leaves a footprint in the snow", w.PrintCount == 1);
            run(WinterScene.PrintLife + 1f);
            Check("footprints fade away", w.PrintCount == 0);

            w.Active = false;
            run(90f);
            Check("winter over: snow stops, drifts and the bank melt", w.FlakeCount == 0 && w.Drifts.Count == 0 && w.Bank == 0f && !w.AnythingToDraw);
            goose.rig.feets.lFootMoveTimeStart = t;
            w.Update(1f / 60f, t += 1f / 60f, screen, geese, ps, 1f);
            goose.rig.feets.lFootMoveTimeStart = -1f;
            w.Update(1f / 60f, t += 1f / 60f, screen, geese, ps, 1f);
            Check("no footprints outside winter", w.PrintCount == 0);
        }

        private static void TestSnowdriftRun()
        {
            FakeWorld world = new FakeWorld();
            GooseEntity g = world.NewGoose(new Vector2(200f, 400f));
            Deluxe.Goose = g;
            WinterScene winter = new WinterScene(new Random(3)) { Active = true };
            Deluxe.Winter = winter;
            SnowDrift d = winter.AddDrift(new Vector2(700f, 420f), 40f, 0f);
            ParticleSystem ps = new ParticleSystem();
            Check("switch to the snowdrift run", Deluxe.SetTask(ChaseSnowdriftTask.Id, false) && world.TaskOf(g) == ChaseSnowdriftTask.Id);
            List<GooseEntity> geese = new List<GooseEntity> { g };
            for (int frame = 0; frame < 60 * 12 && world.TaskOf(g) != "Wander"; frame++)
            {
                world.Run(g, 1f / 60f);
                winter.Update(1f / 60f, world.Now, FakeWorld.Screen, geese, ps, 1f);
            }
            Check("the goose charged through the drift", d.Kicked);
            Check("ran past it rather than stopping in front", g.position.x > 700f + 40f, g.position.x.ToString("0"));
            Check("honked with joy and went back to wandering", world.Honks == 1 && world.TaskOf(g) == "Wander");
            Deluxe.Winter = null;
        }

        private static void TestAutumnMod(string gooseDir)
        {
            string dll = Path.Combine(gooseDir, "Assets", "Mods", "Autumn", "Autumn.dll");
            if (!File.Exists(dll)) { Check("Autumn.dll found", false, dll); return; }
            System.Reflection.Assembly asm = System.Reflection.Assembly.LoadFrom(dll);
            AutumnControl ctl = AutumnControl.Find();
            Check("the Autumn mod's leaf list is found by reflection", ctl != null);
            if (ctl == null) return;
            System.Collections.IList piles = (System.Collections.IList)asm.GetType("Autumn.ModEntryPoint").GetField("piles").GetValue(null);
            Type leafPile = asm.GetType("LeafPile");
            piles.Add(Activator.CreateInstance(leafPile));
            piles.Add(Activator.CreateInstance(leafPile));
            Check("two leaf piles as the Autumn mod would make them", ctl.Count == 2);
            Check("outside autumn they are removed", ctl.Suppress() == 2 && piles.Count == 0);
        }

        // ------------------------------------------------------------------ control panel logic

        private static void TestPanelLogic()
        {
            // запасной способ горячих клавиш: опрос клавиатуры
            HashSet<int> down = new HashSet<int>();
            double t = 0;
            HotkeyPoller poller = new HotkeyPoller(vk => down.Contains(vk), () => t);
            char[] keys = { 'G', 'H', 'P', 'M' };
            Check("без нажатий ничего не срабатывает", poller.Poll(keys).Count == 0);
            down.Add('H');
            Check("H без Ctrl+Alt не срабатывает", poller.Poll(keys).Count == 0);
            down.Add(HotkeyPoller.VK_CONTROL); down.Add(HotkeyPoller.VK_MENU);
            down.Remove('H'); poller.Poll(keys); t += 0.1;
            down.Add('H');
            List<char> fired = poller.Poll(keys);
            Check("Ctrl+Alt+H срабатывает один раз", fired.Count == 1 && fired[0] == 'H');
            t += 0.1;
            Check("удержание не повторяет", poller.Poll(keys).Count == 0);
            down.Remove('H'); t += 0.1; poller.Poll(keys);
            poller.MarkFired('M'); down.Add('M'); t += 0.1;
            Check("нажатие, уже пойманное системой (WM_HOTKEY), не дублируется", poller.Poll(keys).Count == 0);
            down.Remove('M'); t += 1.0; poller.Poll(keys); down.Add('M'); t += 0.1;
            Check("следующее нажатие снова работает", poller.Poll(keys).Count == 1);

            // правый клик: попадание в гуся
            GoosePose p = new GoosePose { scale = 1f, bodyCenter = new Vector2(100, 100), neckBase = new Vector2(115, 100), neckHeadPoint = new Vector2(118, 80) };
            Check("клик по телу — это гусь", GooseHit.IsOnGoose(p, new Vector2(105, 104)));
            Check("клик по голове — это гусь", GooseHit.IsOnGoose(p, new Vector2(120, 78)));
            Check("клик рядом — не гусь", !GooseHit.IsOnGoose(p, new Vector2(160, 100)) && !GooseHit.IsOnGoose(p, new Vector2(100, 140)));
            p.scale = 2f;
            Check("у большого гуся и зона больше", GooseHit.IsOnGoose(p, new Vector2(140, 100)));

            // сохранение одной настройки, не трогая остальное
            string path = Path.Combine(Tmp, "Save.ini");
            File.WriteAllText(path, "; мой комментарий\r\nHat=None\r\nScale = 1\r\nFoo=bar\r\n", new UTF8Encoding(true));
            DeluxeConfig c = new DeluxeConfig { Hat = HatStyle.Santa, Scale = 1.5f, Seasons = "Winter" };
            c.Save(path, "Hat", "Scale", "Seasons");
            string text = File.ReadAllText(path);
            Check("строки заменены на месте", text.Contains("Hat=Santa") && text.Contains("Scale=1.5") && !text.Contains("Hat=None") && !text.Contains("Scale = 1\r"));
            Check("новая строка дописана, чужое не тронуто", text.Contains("Seasons=Winter") && text.Contains("; мой комментарий") && text.Contains("Foo=bar"));
            DeluxeConfig back = DeluxeConfig.Load(path);
            Check("и читается обратно", back.Hat == HatStyle.Santa && Math.Abs(back.Scale - 1.5f) < 1e-6 && back.Seasons == "Winter");

            // отчёт для «Скопировать отчёт»
            string report = DiagReport.Build(new List<DiagItem>
            {
                new DiagItem(DiagLevel.Ok, "Мод загружен", "0.4.0"),
                new DiagItem(DiagLevel.Fail, "Значок у часов", "не создан"),
            }, "строка лога");
            Check("отчёт содержит проверки и лог", report.Contains("✔ Мод загружен: 0.4.0") && report.Contains("✖ Значок у часов: не создан") && report.Contains("строка лога"));
        }

        // ------------------------------------------------------------------ guests

        private static Guests NewGuests(FakeWorld w)
        {
            Guests guests = new Guests(() => w.Tick, () => w.UpdateRig, () => new GooseAnimator(Deluxe.Cfg, Deluxe.Particles) { SilentStart = true });
            Deluxe.IsGuest = guests.IsGuest;
            Deluxe.AnimatorFor = e => guests.AnimatorOf(e);
            return guests;
        }

        private static void Frames(FakeWorld w, Guests guests, float seconds, Func<bool> until = null)
        {
            int frames = (int)(seconds * 60f);
            for (int i = 0; i < frames; i++)
            {
                w.Now += 1f / 60f;
                Time.time = w.Now;
                guests.Update(2); // 60 FPS -> two 1/120 s steps per frame
                if (until != null && until()) return;
            }
        }

        private static void TestGuests()
        {
            FakeWorld w = new FakeWorld();
            Deluxe.Goose = w.NewGoose(new Vector2(640f, 400f));
            Guests guests = NewGuests(w);
            GuestLook look = new GuestLook { Name = "Вася", Orange = "#ff0000", Hat = HatStyle.Santa };
            Guest v = guests.Spawn(look, DeliverTask.Id, new DeliveryPayload { FromName = "Вася", FromCode = "ABCDEFGHJKMN", Text = "Привет от гостя" });
            Check("visitor spawned just off-screen", v != null && (v.Entity.position.x < 0 || v.Entity.position.x > FakeWorld.Screen.x));
            Check("visitor wears its owner's colours", v != null && v.Entity.renderData.brushGooseOrange.Color.ToArgb() == System.Drawing.Color.FromArgb(255, 255, 0, 0).ToArgb());
            Check("the player's goose is not a guest, the visitor is", !guests.IsGuest(Deluxe.Goose) && guests.IsGuest(v.Entity) && guests.Busy);
            Frames(w, guests, 90f, () => v.GoneAt >= 0f);
            FakeWindow win = w.Windows.FirstOrDefault();
            Check("visitor dragged the note in", win != null && win.Shown && win.Moves.Count > 10 && win.Payload.FromCode == "ABCDEFGHJKMN");
            if (win != null && win.Moves.Count > 0)
            {
                System.Drawing.Point last = win.Moves.Last();
                Check("note ends on screen", last.X >= 0 && last.X + win.Width <= FakeWorld.Screen.x, last.ToString());
            }
            Check("visitor honked, then left", w.Honks >= 1 && w.TaskLog.Contains(LeaveTask.Id) && v.GoneAt >= 0f);
            Check("a visitor that left no longer blocks the next one", !guests.Busy);
            Frames(w, guests, 11f);
            Check("and is forgotten once its footprints faded", guests.All.Count == 0);
            Check("the player's goose was never touched", w.TaskOf(Deluxe.Goose) == "Wander");

            FakeWorld w2 = new FakeWorld();
            Deluxe.Goose = w2.NewGoose(new Vector2(640f, 400f));
            Guests g2 = NewGuests(w2);
            Guest visit = g2.Spawn(new GuestLook { Name = "Петя" }, VisitTask.Id, null);
            Frames(w2, g2, 30f, () => visit.GoneAt >= 0f);
            Check("a visit: walks in, honks twice, leaves", w2.Honks == 2 && visit.GoneAt >= 0f, "honks " + w2.Honks);

            FakeWorld w3 = new FakeWorld();
            Deluxe.Goose = w3.NewGoose(new Vector2(640f, 400f));
            Guests g3 = NewGuests(w3);
            Guest meme = g3.Spawn(new GuestLook(), "CollectMeme", null);
            Frames(w3, g3, 20f, () => meme.GoneAt >= 0f);
            Check("a visitor never wanders: after a goose task it goes home", w3.TaskLog.Contains("Wander") && w3.TaskLog.Last() == LeaveTask.Id && meme.GoneAt >= 0f);
        }
    }
}
