using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace GooseDeluxe
{
    /// <summary>
    /// "Пульт гуся": actions, every setting (applied at once and saved), and a self-check with a report
    /// the user can copy and send. Opened by right-clicking the goose, clicking the tray icon or Ctrl+Alt+M.
    /// </summary>
    internal sealed class ControlPanel : Form
    {
        private static ControlPanel current;

        private readonly IGooseControls c;
        private readonly TabControl tabs;
        private Button pauseButton;
        private ListView checks;
        private TextBox logBox;
        private Label testStatus;
        private readonly List<Action> refreshers = new List<Action>();
        private bool loading;

        public static void Open(IGooseControls controls, int tab = 0)
        {
            if (current != null && !current.IsDisposed)
            {
                current.tabs.SelectedIndex = tab;
                current.Reload();
                if (tab == 2) current.RunChecks();
                if (current.WindowState == FormWindowState.Minimized) current.WindowState = FormWindowState.Normal;
                current.Activate();
                return;
            }
            current = new ControlPanel(controls);
            current.tabs.SelectedIndex = tab;
            current.Show();
            current.Activate();
            current.Reload();
            if (tab == 2) current.RunChecks();
        }

        public ControlPanel(IGooseControls controls)
        {
            c = controls;
            Text = "Пульт гуся — GooseDeluxe " + ModInfo.Version;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(560, 600);

            tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildActions());
            tabs.TabPages.Add(BuildSettings());
            tabs.TabPages.Add(BuildChecks());
            tabs.SelectedIndexChanged += (s, e) => { if (tabs.SelectedIndex == 2) RunChecks(); };
            Controls.Add(tabs);
            Activated += (s, e) => Reload();
            // the window may open without focus (e.g. from the tray): load the state now, not on activation
            Load += (s, e) => Reload();
            Reload();
        }

        // ------------------------------------------------------------------ helpers

        private void Safe(Action a)
        {
            try { a(); }
            catch (Exception ex) { Deluxe.Log("Panel action failed: " + ex); MessageBox.Show(this, "Не получилось: " + ex.Message, "Пульт гуся", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            Reload();
        }

        private Button Btn(string text, Action onClick)
        {
            Button b = new Button { Text = text, Dock = DockStyle.Fill, Height = 36, Margin = new Padding(4) };
            b.Click += (s, e) => Safe(onClick);
            return b;
        }

        private static TableLayoutPanel Grid(int columns)
        {
            TableLayoutPanel t = new TableLayoutPanel { ColumnCount = columns, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            for (int i = 0; i < columns; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            return t;
        }

        private static GroupBox Group(string title, Control content)
        {
            GroupBox g = new GroupBox { Text = title, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 6, 8, 8) };
            g.Controls.Add(content);
            return g;
        }

        /// <summary>Docked-top controls stack in reverse order of adding; this keeps them in reading order.</summary>
        private static void Stack(Control parent, params Control[] items)
        {
            for (int i = items.Length - 1; i >= 0; i--) parent.Controls.Add(items[i]);
        }

        private void Reload()
        {
            loading = true;
            try { foreach (Action r in refreshers) r(); }
            finally { loading = false; }
        }

        // ------------------------------------------------------------------ tab 1: actions

        private TabPage BuildActions()
        {
            TabPage page = new TabPage("Пульт") { Padding = new Padding(8), AutoScroll = true };

            TableLayoutPanel main = Grid(2);
            main.Controls.Add(Btn("Сказать фразу", c.SayPhrase));
            main.Controls.Add(Btn("Фразы (дописать свои)", c.OpenPhrases));
            main.Controls.Add(Btn("Папка голоса (свои записи)", c.OpenVoiceFolder));
            main.Controls.Add(Btn("Позвать гуся  (Ctrl+Alt+G)", c.Come));
            main.Controls.Add(Btn("Гудок  (Ctrl+Alt+H)", c.HonkNow));
            main.Controls.Add(Btn("Принести мем", () => c.RunGooseTask("CollectMeme")));
            main.Controls.Add(Btn("Принести записку", () => c.RunGooseTask("CollectNotepad")));
            main.Controls.Add(Btn("Наследить грязью", () => c.RunGooseTask("TrackMud")));
            main.Controls.Add(Btn("Украсть курсор", () => c.RunGooseTask("NabMouse")));
            main.Controls.Add(Btn("Убрать листья", c.SweepLeaves));
            main.Controls.Add(Btn("Папка мемов (добавить свои)", c.OpenMemesFolder));
            main.Controls.Add(Btn("Погнаться за курсором", c.ChaseCursor));
            main.Controls.Add(Btn("Тест дрифта (дым и фонк)", c.TestDrift));
            main.Controls.Add(Btn("Папка фонка (свой трек)", c.OpenPhonkFolder));
            pauseButton = Btn("Пауза  (Ctrl+Alt+P)", c.TogglePause);
            main.Controls.Add(pauseButton);
            main.Controls.Add(Btn("Выгнать гуся", () =>
            {
                if (MessageBox.Show(this, "Выгнать гуся? Вернуть его — запустить GooseDesktop.exe или ярлык «Гусь».", "Пульт гуся",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) c.Exit();
            }));
            refreshers.Add(() => pauseButton.Text = c.Paused ? "Разбудить гуся  (Ctrl+Alt+P)" : "Уложить спать  (Ctrl+Alt+P)");

            Label friendState = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 38, TextAlign = ContentAlignment.MiddleLeft };
            TableLayoutPanel friends = Grid(2);
            List<Button> needFriend = new List<Button>();
            Button sendNote = Btn("Отправить записку…", c.SendNote);
            Button sendPic = Btn("Отправить картинку…", c.SendPicture);
            Button visit = Btn("Сбегать в гости (гудок)", () => c.SendPrank(GooseCommand.Honk));
            Button meme = Btn("Принести другу мем", () => c.SendPrank(GooseCommand.Meme));
            Button mud = Btn("Наследить у друга", () => c.SendPrank(GooseCommand.Mud));
            Button steal = Btn("Украсть курсор у друга", () => c.SendPrank(GooseCommand.Steal));
            Button phrase = Btn("Сказать другу фразу", () => c.SendPrank(GooseCommand.Phrase));
            needFriend.AddRange(new[] { sendPic, visit, meme, mud, steal, phrase });
            Button editFriend = Btn("Добавить / изменить друга…", c.EditFriend);
            Button copyCode = Btn("Скопировать мой код", c.CopyMyCode);
            Button invite = Btn("Скопировать приглашение", c.CopyInvite);
            foreach (Button b in new[] { sendNote, sendPic, visit, meme, mud, steal, phrase, editFriend, copyCode, invite }) friends.Controls.Add(b);
            Panel friendBox = new Panel { Dock = DockStyle.Top, AutoSize = true };
            Stack(friendBox, friendState, friends);
            refreshers.Add(() =>
            {
                bool on = c.FriendsEnabled;
                foreach (Control b in friends.Controls) b.Enabled = on;
                foreach (Button b in needFriend) b.Enabled = on && c.HasFriend;
                friendState.Text = !on ? "Гусиная почта выключена (вкладка «Настройки» → «Гусиная почта», потом перезапусти гуся)."
                    : (c.HasFriend ? "Друг: " + c.FriendName + ".   Твой код: " + c.MyCodeDisplay
                                   : "Друг ещё не добавлен.   Твой код: " + c.MyCodeDisplay + " — отправь его другу.");
            });

            Label hint = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 52,
                ForeColor = Color.DimGray,
                Text = "Открыть этот пульт: правый клик по гусю, клик по значку гуся у часов (он может прятаться под стрелкой ^) или Ctrl+Alt+M.",
            };
            Stack(page, Group("Гусь", main), Group("Гусь к другу", friendBox), hint);
            return page;
        }

        // ------------------------------------------------------------------ tab 2: settings

        private CheckBox Check(string text, Func<bool> get, Action<bool> set)
        {
            CheckBox cb = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(4, 3, 4, 3) };
            refreshers.Add(() => cb.Checked = get());
            cb.CheckedChanged += (s, e) => { if (!loading) Safe(() => set(cb.Checked)); };
            return cb;
        }

        private CheckBox Setting(string text, string key)
        {
            return Check(text, () => (bool)typeof(DeluxeConfig).GetField(key).GetValue(c.Config), v =>
            {
                typeof(DeluxeConfig).GetField(key).SetValue(c.Config, v);
                c.ApplyConfig(key);
            });
        }

        private Control Choice(string label, string[] names, Func<int> get, Action<int> set)
        {
            TableLayoutPanel row = Grid(2);
            row.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 150f);
            row.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100f);
            ComboBox box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            box.Items.AddRange(names);
            refreshers.Add(() => box.SelectedIndex = Math.Max(0, Math.Min(names.Length - 1, get())));
            box.SelectedIndexChanged += (s, e) => { if (!loading && box.SelectedIndex >= 0) Safe(() => set(box.SelectedIndex)); };
            row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 7, 4, 4) });
            row.Controls.Add(box);
            return row;
        }

        private static readonly HatStyle[] Hats = { HatStyle.None, HatStyle.TopHat, HatStyle.Party, HatStyle.Santa };
        private static readonly string[] SeasonValues = { "Auto", "Winter", "Spring", "Summer", "Autumn", "Off" };

        private TabPage BuildSettings()
        {
            TabPage page = new TabPage("Настройки") { Padding = new Padding(8), AutoScroll = true };
            DeluxeConfig cfg = c.Config;

            // --- look
            TableLayoutPanel look = Grid(1);
            look.Controls.Add(Choice("Шляпа", new[] { "Без шляпы", "Цилиндр", "Праздничный колпак", "Шапка Деда Мороза" },
                () => Array.IndexOf(Hats, cfg.Hat), i => { cfg.Hat = Hats[i]; c.ApplyConfig("Hat"); }));
            TableLayoutPanel sizeRow = Grid(3);
            sizeRow.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 150f);
            sizeRow.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100f);
            sizeRow.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, 50f);
            TrackBar size = new TrackBar { Minimum = 5, Maximum = 30, TickFrequency = 5, Dock = DockStyle.Fill, Height = 32 };
            Label sizeText = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 7, 4, 4) };
            refreshers.Add(() => { size.Value = (int)Math.Round(M.Clamp(cfg.Scale, 0.5f, 3f) * 10f); sizeText.Text = cfg.Scale.ToString("0.0") + "×"; });
            size.ValueChanged += (s, e) =>
            {
                sizeText.Text = (size.Value / 10f).ToString("0.0") + "×";
                if (!loading) Safe(() => { cfg.Scale = size.Value / 10f; c.ApplyConfig("Scale"); });
            };
            sizeRow.Controls.Add(new Label { Text = "Размер гуся", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 7, 4, 4) });
            sizeRow.Controls.Add(size);
            sizeRow.Controls.Add(sizeText);
            look.Controls.Add(sizeRow);
            TableLayoutPanel lookChecks = Grid(2);
            lookChecks.Controls.Add(Setting("Шарф зимой", "WinterScarf"));
            lookChecks.Controls.Add(Setting("Новогодняя шапка", "NewYearHat"));
            look.Controls.Add(lookChecks);

            // --- behaviour
            TableLayoutPanel behave = Grid(2);
            CheckBox mute = Check("Без звука", () => c.Muted, v => c.Muted = v);
            CheckBox mouse = Check("Гусь может красть курсор", () => c.GooseMayStealMouse, v => c.GooseMayStealMouse = v);
            refreshers.Add(() => mute.Enabled = mouse.Enabled = c.GooseSettingsAvailable);
            behave.Controls.Add(mute);
            behave.Controls.Add(mouse);
            behave.Controls.Add(Setting("Прятаться в играх и кино", "PauseInFullscreen"));
            behave.Controls.Add(Setting("Правильная скорость", "FixSpeed"));
            behave.Controls.Add(Setting("Честный рандом проделок", "HonestRandom"));
            behave.Controls.Add(Setting("Мемы и записки без повторов", "NoRepeats"));
            behave.Controls.Add(Setting("Кучи листьев убираются кликом", "ClickLeafPiles"));
            behave.Controls.Add(Setting("Кучи листьев осенью", "LeafPiles"));
            behave.Controls.Add(Setting("Визг шин в заносе", "DriftSound"));
            behave.Controls.Add(Setting("Фонк в заносе", "DriftMusic"));
            behave.Controls.Add(Setting("Иногда сам гоняется за курсором", "RandomChase"));
            behave.Controls.Add(Setting("Гусь говорит фразы", "Phrases"));
            behave.Controls.Add(Setting("Сам подходит и говорит фразы", "RandomPhrases"));

            // --- animation
            TableLayoutPanel anim = Grid(2);
            string[,] animKeys =
            {
                { "Сглаживание", "AntiAlias" }, { "Мягкая тень", "SoftShadow" }, { "Ноги", "Legs" }, { "Перевалка", "Waddle" },
                { "Плавные повороты", "SmoothTurning" }, { "Сжатие при разгоне", "SquashStretch" }, { "Смотрит на курсор", "LookAtCursor" },
                { "Моргает", "Blink" }, { "Зевает, чистит перья", "IdleAnimations" }, { "Крылья", "Wings" },
                { "Открывает клюв", "HonkAnimation" }, { "Пыль, перья, снег", "Particles" }, { "Надписи «ГА!»", "HonkText" },
                { "Дым из-под лап в заносе", "DriftSmoke" },
            };
            for (int i = 0; i < animKeys.GetLength(0); i++) anim.Controls.Add(Setting(animKeys[i, 0], animKeys[i, 1]));

            // --- language & seasons
            TableLayoutPanel lang = Grid(1);
            lang.Controls.Add(Choice("Голос фраз", new[] { "Только готовые записи (папка «Голос»)", "Записи + голос Windows для остальных", "Записи + «га-га» для остальных", "Без голоса (только облачко)" },
                () => Math.Max(0, Array.IndexOf(DeluxeConfig.PhraseVoices, cfg.PhraseVoice)),
                i => { cfg.PhraseVoice = DeluxeConfig.PhraseVoices[i]; c.ApplyConfig("PhraseVoice"); }));
            lang.Controls.Add(Choice("Гудок", new[] { "«ГА-ГА-ГА!» (по-русски)", "«HONK!» (как в оригинале)" },
                () => cfg.Language == "EN" ? 1 : 0, i => { cfg.Language = i == 1 ? "EN" : "RU"; c.ApplyConfig("Language"); }));
            lang.Controls.Add(Choice("Времена года", new[] { "По дате", "Всегда зима", "Всегда весна", "Всегда лето", "Всегда осень", "Выключить" },
                () => Math.Max(0, Array.IndexOf(SeasonValues, cfg.Seasons)), i => { cfg.Seasons = SeasonValues[i]; c.ApplyConfig("Seasons"); }));
            TableLayoutPanel langChecks = Grid(1);
            langChecks.Controls.Add(Setting("Русские записки в блокноте гуся", "RussianNotes"));
            langChecks.Controls.Add(Setting("Надписи на мемах по-русски", "RussianMemes"));
            lang.Controls.Add(langChecks);

            // --- friends
            TableLayoutPanel friends = Grid(1);
            friends.Controls.Add(Setting("Гусиная почта (включится после перезапуска гуся)", "Friends"));
            friends.Controls.Add(Setting("Друг может присылать гуся за моим курсором", "FriendCanStealMouse"));

            Label note = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 36, ForeColor = Color.DimGray, Text = "Всё применяется сразу и сохраняется в GooseDeluxe.ini." };
            Stack(page, Group("Внешность", look), Group("Поведение", behave), Group("Анимации", anim), Group("Язык и времена года", lang), Group("Друзья", friends), note);
            return page;
        }

        // ------------------------------------------------------------------ tab 3: self-check

        private TabPage BuildChecks()
        {
            TabPage page = new TabPage("Проверка") { Padding = new Padding(8) };

            checks = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Top, Height = 290, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            checks.Columns.Add("", 28);
            checks.Columns.Add("Что проверяем", 190);
            checks.Columns.Add("Результат", 600);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
            Func<string, Action, Button> small = (t, a) =>
            {
                Button b = new Button { Text = t, AutoSize = true, Margin = new Padding(3) };
                b.Click += (s, e) => Safe(a);
                return b;
            };
            buttons.Controls.Add(small("Проверить заново", RunChecks));
            buttons.Controls.Add(small("Тест: гудок", () => { c.HonkNow(); testStatus.Text = "Гусь должен был гудеть и открыть клюв."; }));
            buttons.Controls.Add(small("Тест: записка", () => { c.TestNote(); testStatus.Text = "Гусь сейчас убежит за край и принесёт тестовую записку."; }));
            buttons.Controls.Add(small("Тест: связь", () => { testStatus.Text = "Отправляю записку самому себе через сервер…"; c.TestConnection(r => { if (!IsDisposed) { testStatus.Text = r; RunChecks(); } }); }));
            buttons.Controls.Add(small("Тест: сугроб", () => { c.TestSnow(); testStatus.Text = "Появится сугроб, и гусь в него врежется."; }));
            buttons.Controls.Add(small("Скопировать отчёт", CopyReport));
            buttons.Controls.Add(small("Папка мода", c.OpenModFolder));

            testStatus = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 34, ForeColor = Color.DarkSlateBlue, Text = "Если что-то не работает — нажми «Скопировать отчёт» и пришли его." };
            Label logTitle = new Label { Dock = DockStyle.Top, Text = "Последние строки GooseDeluxe.log:", AutoSize = false, Height = 20 };
            logBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Font = new Font(FontFamily.GenericMonospace, 8f) };
            page.Controls.Add(logBox);
            Stack(page, checks, buttons, testStatus, logTitle);
            return page;
        }

        private List<DiagItem> lastChecks = new List<DiagItem>();

        private void RunChecks()
        {
            try { FillChecks(); }
            catch (Exception ex) { Deluxe.Log("Self-check failed: " + ex); testStatus.Text = "Проверка упала: " + ex.Message; }
        }

        private void FillChecks()
        {
            lastChecks = c.RunDiagnostics();
            checks.BeginUpdate();
            checks.Items.Clear();
            foreach (DiagItem d in lastChecks)
            {
                ListViewItem item = new ListViewItem(d.Mark);
                item.SubItems.Add(d.Title);
                item.SubItems.Add(d.Detail);
                item.ForeColor = d.Level == DiagLevel.Fail ? Color.Firebrick : d.Level == DiagLevel.Warn ? Color.DarkOrange : d.Level == DiagLevel.Ok ? Color.ForestGreen : Color.DimGray;
                item.ToolTipText = d.Detail;
                checks.Items.Add(item);
            }
            checks.EndUpdate();
            checks.ShowItemToolTips = true;
            logBox.Text = c.LogTail(40).Replace("\r\n", "\n").Replace("\n", "\r\n");
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }

        private void CopyReport()
        {
            if (lastChecks.Count == 0) RunChecks();
            Clipboard.SetText(DiagReport.Build(lastChecks, c.LogTail(40)));
            testStatus.Text = "Отчёт скопирован — вставь его в чат (Ctrl+V).";
        }
    }
}
