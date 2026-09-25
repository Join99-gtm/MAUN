using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace GooseDeluxe
{
    /// <summary>
    /// Live access to the goose's own settings (GooseDesktop.GooseConfig.settings) so the tray can mute
    /// the goose or forbid mouse stealing without a restart. Changes are also written to config.ini
    /// line by line: no BOM, other lines untouched, because the goose's parser deletes the whole file
    /// when anything in it looks unfamiliar.
    /// </summary>
    internal static class GooseSettings
    {
        private static object settings;
        private static FieldInfo silenceField, attackMouseField, customColorsField, whiteField, orangeField, outlineField;

        public static bool Available { get { return settings != null && silenceField != null && attackMouseField != null; } }

        public static bool Init()
        {
            try
            {
                Assembly exe = Assembly.GetEntryAssembly();
                Type cfg = exe == null ? null : exe.GetType("GooseDesktop.GooseConfig");
                FieldInfo f = cfg == null ? null : cfg.GetField("settings", BindingFlags.Public | BindingFlags.Static);
                settings = f == null ? null : f.GetValue(null);
                if (settings == null) return false;
                Type t = settings.GetType();
                silenceField = t.GetField("SilenceSounds");
                attackMouseField = t.GetField("Task_CanAttackMouse");
                customColorsField = t.GetField("UseCustomColors");
                whiteField = t.GetField("GooseDefaultWhite");
                orangeField = t.GetField("GooseDefaultOrange");
                outlineField = t.GetField("GooseDefaultOutline");
                return Available;
            }
            catch (Exception ex)
            {
                Deluxe.Log("GooseSettings unavailable: " + ex.Message);
                settings = null;
                return false;
            }
        }

        public static bool SilenceSounds
        {
            get { return Available && (bool)silenceField.GetValue(settings); }
            set
            {
                if (!Available) return;
                silenceField.SetValue(settings, value);
                Persist("SilenceSounds", value ? "True" : "False");
            }
        }

        public static bool CanAttackMouse
        {
            get { return !Available || (bool)attackMouseField.GetValue(settings); }
            set
            {
                if (!Available) return;
                attackMouseField.SetValue(settings, value);
                Persist("Task_CanAttackMouse", value ? "True" : "False");
            }
        }

        /// <summary>The goose's colours as "#rrggbb" strings (white, orange, outline), for visits to a friend.</summary>
        public static string[] Colors()
        {
            string[] defaults = { "#ffffff", "#ffa500", "#d3d3d3" };
            try
            {
                if (settings == null || customColorsField == null || !(bool)customColorsField.GetValue(settings)) return defaults;
                return new[] { (string)whiteField.GetValue(settings), (string)orangeField.GetValue(settings), (string)outlineField.GetValue(settings) };
            }
            catch { return defaults; }
        }

        public static void Persist(string key, string value)
        {
            try
            {
                if (Deluxe.GooseDir == null) return;
                string path = Path.Combine(Deluxe.GooseDir, "config.ini");
                if (!File.Exists(path)) return;
                string text = File.ReadAllText(path);
                Regex line = new Regex("(?m)^" + Regex.Escape(key) + "=[^\\r\\n]*");
                text = line.IsMatch(text)
                    ? line.Replace(text, key + "=" + value, 1)
                    : text.TrimEnd('\r', '\n') + "\n" + key + "=" + value + "\n";
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Deluxe.Log("Could not save " + key + " to config.ini: " + ex.Message);
            }
        }
    }
}
