using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GooseDeluxe
{
    public enum HatStyle { None, TopHat, Party, Santa }

    /// <summary>
    /// Settings read from GooseDeluxe.ini next to the mod DLL. Unlike the goose's own
    /// config parser this one never deletes the file, ignores unknown keys and parses
    /// numbers culture-independently.
    /// </summary>
    internal sealed class DeluxeConfig
    {
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

        public static DeluxeConfig Load(string path)
        {
            DeluxeConfig c = new DeluxeConfig();
            if (!File.Exists(path))
            {
                try { File.WriteAllText(path, c.ToIni(), Encoding.UTF8); } catch { }
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
            string hat;
            if (kv.TryGetValue("Hat", out hat))
            {
                try { c.Hat = (HatStyle)Enum.Parse(typeof(HatStyle), hat, true); } catch { }
            }
            return c;
        }

        public string ToIni()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; GooseDeluxe settings. True/False, restart the goose to apply.");
            sb.AppendLine("AntiAlias=" + AntiAlias);
            sb.AppendLine("SoftShadow=" + SoftShadow);
            sb.AppendLine("Legs=" + Legs);
            sb.AppendLine("Waddle=" + Waddle);
            sb.AppendLine("SmoothTurning=" + SmoothTurning);
            sb.AppendLine("SquashStretch=" + SquashStretch);
            sb.AppendLine("LookAtCursor=" + LookAtCursor);
            sb.AppendLine("Blink=" + Blink);
            sb.AppendLine("IdleAnimations=" + IdleAnimations);
            sb.AppendLine("Wings=" + Wings);
            sb.AppendLine("HonkAnimation=" + HonkAnimation);
            sb.AppendLine("Particles=" + Particles);
            sb.AppendLine("HonkText=" + HonkText);
            sb.AppendLine("; Hat: None, TopHat, Party, Santa");
            sb.AppendLine("Hat=" + Hat);
            sb.AppendLine("; Visual size of the goose, 0.5 - 3.0 (use a dot as the decimal separator)");
            sb.AppendLine("Scale=" + Scale.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static bool GetBool(Dictionary<string, string> kv, string key, bool def)
        {
            string s; bool b;
            if (kv.TryGetValue(key, out s))
            {
                if (bool.TryParse(s, out b)) return b;
                if (s == "1" || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
                if (s == "0" || s.Equals("no", StringComparison.OrdinalIgnoreCase) || s.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
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
