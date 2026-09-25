using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GooseDeluxe
{
    public enum HatStyle { None, TopHat, Party, Santa }

    /// <summary>
    /// Settings read from GooseDeluxe.ini next to the mod DLL. Unlike the goose's own config parser this
    /// one never deletes the file, ignores unknown keys and parses numbers culture-independently.
    /// Keys added in newer versions are appended to an existing file, so they show up for editing.
    /// </summary>
    internal sealed class DeluxeConfig
    {
        // graphics & animation (0.1)
        public bool AntiAlias = true;
        public bool SoftShadow = true;
        public bool Legs = true;
        public bool Waddle = true;
        public bool SmoothTurning = true;
        public bool SquashStretch = true;
        public bool LookAtCursor = true;
        public bool Blink = true;
        public bool IdleAnimations = true;
        public bool Wings = true;
        public bool HonkAnimation = true;
        public bool Particles = true;
        public bool HonkText = true;
        public HatStyle Hat = HatStyle.None;
        public float Scale = 1.0f;

        // fixes & comfort (0.2)
        public bool FixSpeed = true;
        public bool HonestRandom = true;
        public bool PauseInFullscreen = true;
        public bool Tray = true;
        public bool Hotkeys = true;
        public string Language = "RU";
        public bool RussianNotes = true;

        // friends (0.2)
        public bool Friends = true;
        public bool FriendCanStealMouse = true;
        public string NtfyServer = "https://ntfy.sh";

        private static readonly string[] Keys =
        {
            "AntiAlias", "SoftShadow", "Legs", "Waddle", "SmoothTurning", "SquashStretch", "LookAtCursor", "Blink",
            "IdleAnimations", "Wings", "HonkAnimation", "Particles", "HonkText", "Hat", "Scale",
            "FixSpeed", "HonestRandom", "PauseInFullscreen", "Tray", "Hotkeys", "Language", "RussianNotes",
            "Friends", "FriendCanStealMouse", "NtfyServer",
        };

        public static DeluxeConfig Load(string path)
        {
            DeluxeConfig c = new DeluxeConfig();
            if (!File.Exists(path))
            {
                try { File.WriteAllText(path, c.ToIni(null), new UTF8Encoding(true)); } catch { }
                return c;
            }
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            c.AntiAlias = GetBool(kv, "AntiAlias", c.AntiAlias);
            c.SoftShadow = GetBool(kv, "SoftShadow", c.SoftShadow);
            c.Legs = GetBool(kv, "Legs", c.Legs);
            c.Waddle = GetBool(kv, "Waddle", c.Waddle);
            c.SmoothTurning = GetBool(kv, "SmoothTurning", c.SmoothTurning);
            c.SquashStretch = GetBool(kv, "SquashStretch", c.SquashStretch);
            c.LookAtCursor = GetBool(kv, "LookAtCursor", c.LookAtCursor);
            c.Blink = GetBool(kv, "Blink", c.Blink);
            c.IdleAnimations = GetBool(kv, "IdleAnimations", c.IdleAnimations);
            c.Wings = GetBool(kv, "Wings", c.Wings);
            c.HonkAnimation = GetBool(kv, "HonkAnimation", c.HonkAnimation);
            c.Particles = GetBool(kv, "Particles", c.Particles);
            c.HonkText = GetBool(kv, "HonkText", c.HonkText);
            c.Scale = Clamp(GetFloat(kv, "Scale", c.Scale), 0.5f, 3f);
            string v;
            if (kv.TryGetValue("Hat", out v))
            {
                try { c.Hat = (HatStyle)Enum.Parse(typeof(HatStyle), v, true); } catch { }
            }
            c.FixSpeed = GetBool(kv, "FixSpeed", c.FixSpeed);
            c.HonestRandom = GetBool(kv, "HonestRandom", c.HonestRandom);
            c.PauseInFullscreen = GetBool(kv, "PauseInFullscreen", c.PauseInFullscreen);
            c.Tray = GetBool(kv, "Tray", c.Tray);
            c.Hotkeys = GetBool(kv, "Hotkeys", c.Hotkeys);
            if (kv.TryGetValue("Language", out v) && (v.Equals("EN", StringComparison.OrdinalIgnoreCase) || v.Equals("RU", StringComparison.OrdinalIgnoreCase)))
                c.Language = v.ToUpperInvariant();
            c.RussianNotes = GetBool(kv, "RussianNotes", c.RussianNotes);
            c.Friends = GetBool(kv, "Friends", c.Friends);
            c.FriendCanStealMouse = GetBool(kv, "FriendCanStealMouse", c.FriendCanStealMouse);
            if (kv.TryGetValue("NtfyServer", out v))
            {
                Uri u;
                if (Uri.TryCreate(v, UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp))
                    c.NtfyServer = v.TrimEnd('/');
            }

            List<string> missing = new List<string>();
            foreach (string k in Keys) if (!kv.ContainsKey(k)) missing.Add(k);
            if (missing.Count > 0)
            {
                try
                {
                    string existing = File.ReadAllText(path);
                    string sep = existing.EndsWith("\n") ? "" : Environment.NewLine;
                    File.AppendAllText(path, sep + Environment.NewLine + "; --- добавлено GooseDeluxe 0.2 ---" + Environment.NewLine + c.ToIni(missing), Encoding.UTF8);
                }
                catch { }
            }
            return c;
        }

        /// <summary>The ini text for all keys, or only for <paramref name="only"/>.</summary>
        public string ToIni(ICollection<string> only)
        {
            StringBuilder sb = new StringBuilder();
            Action<string, string, string> add = (key, value, comment) =>
            {
                if (only != null && !only.Contains(key)) return;
                if (comment != null) sb.AppendLine("; " + comment);
                sb.AppendLine(key + "=" + value);
            };
            if (only == null) sb.AppendLine("; Настройки GooseDeluxe. True/False, после правки перезапусти гуся.");
            add("AntiAlias", B(AntiAlias), null);
            add("SoftShadow", B(SoftShadow), null);
            add("Legs", B(Legs), null);
            add("Waddle", B(Waddle), null);
            add("SmoothTurning", B(SmoothTurning), null);
            add("SquashStretch", B(SquashStretch), null);
            add("LookAtCursor", B(LookAtCursor), null);
            add("Blink", B(Blink), null);
            add("IdleAnimations", B(IdleAnimations), null);
            add("Wings", B(Wings), null);
            add("HonkAnimation", B(HonkAnimation), null);
            add("Particles", B(Particles), null);
            add("HonkText", B(HonkText), null);
            add("Hat", Hat.ToString(), "Шляпа: None, TopHat, Party, Santa");
            add("Scale", Scale.ToString(CultureInfo.InvariantCulture), "Размер гуся, 0.5 - 3.0");
            add("FixSpeed", B(FixSpeed), "Гусь ходит с той скоростью, что задумал автор (в оригинале он медленнее)");
            add("HonestRandom", B(HonestRandom), "Честно случайный порядок проделок (в оригинале он почти всегда один и тот же)");
            add("PauseInFullscreen", B(PauseInFullscreen), "Прятаться, пока открыта игра, видео или презентация на весь экран");
            add("Tray", B(Tray), "Значок гуся в трее с меню");
            add("Hotkeys", B(Hotkeys), "Ctrl+Alt+G — позвать гуся, Ctrl+Alt+H — гудок, Ctrl+Alt+P — пауза");
            add("Language", Language, "RU — «ГА-ГА-ГА!», EN — «HONK!»");
            add("RussianNotes", B(RussianNotes), "Русские записки в блокноте гуся (английские убираются в *.txt.en)");
            add("Friends", B(Friends), "Гусь к другу: записки, картинки и визиты через сервер ntfy");
            add("FriendCanStealMouse", B(FriendCanStealMouse), "Разрешить другу присылать гуся за твоим курсором");
            add("NtfyServer", NtfyServer, "Сервер для гусиной почты (можно свой ntfy)");
            return sb.ToString();
        }

        private static string B(bool b) { return b ? "True" : "False"; }

        private static bool GetBool(Dictionary<string, string> kv, string key, bool def)
        {
            string s; bool b;
            if (kv.TryGetValue(key, out s))
            {
                if (bool.TryParse(s, out b)) return b;
                if (s == "1" || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s.Equals("on", StringComparison.OrdinalIgnoreCase) || s.Equals("да", StringComparison.OrdinalIgnoreCase)) return true;
                if (s == "0" || s.Equals("no", StringComparison.OrdinalIgnoreCase) || s.Equals("off", StringComparison.OrdinalIgnoreCase) || s.Equals("нет", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return def;
        }

        private static float GetFloat(Dictionary<string, string> kv, string key, float def)
        {
            string s; float f;
            if (kv.TryGetValue(key, out s) && float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return f;
            return def;
        }

        private static float Clamp(float v, float min, float max) { return Math.Min(Math.Max(v, min), max); }
    }
}
