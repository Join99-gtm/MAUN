using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GooseDeluxe
{
    /// <summary>
    /// The goose's phrases: «Фразы.txt» next to the mod, one per line, the user can add their own. Written from
    /// the list built into the mod the first time (never overwritten afterwards), read again whenever it
    /// changes, and dealt round and round without repeats.
    /// </summary>
    internal sealed class PhraseBook
    {
        public const string FileName = "Фразы.txt";
        public const int MaxLength = 300;
        private const string Resource = "GooseDeluxe.Phrases.txt";

        private readonly NoRepeatDeck deck;
        private List<string> phrases = new List<string>();
        private DateTime stamp = DateTime.MinValue;
        private long size = -1;

        public string Path { get; private set; }
        public string LastError { get; private set; }
        public int Count { get { Refresh(); return phrases.Count; } }

        public PhraseBook(string modDir, Random rng = null)
        {
            deck = new NoRepeatDeck(rng);
            Path = System.IO.Path.Combine(modDir, FileName);
            try
            {
                if (!File.Exists(Path)) File.WriteAllText(Path, DefaultText(), new UTF8Encoding(true));
            }
            catch (Exception ex) { LastError = ex.Message; }
            Refresh();
        }

        /// <summary>The next phrase (null if there are none).</summary>
        public string Next() { Refresh(); return deck.Next(phrases); }

        /// <summary>The phrase <see cref="Next"/> will give, to get its sound ready in advance.</summary>
        public string Peek() { Refresh(); return deck.Peek(phrases); }

        private void Refresh()
        {
            try
            {
                FileInfo fi = new FileInfo(Path);
                if (!fi.Exists)
                {
                    if (phrases.Count == 0) phrases = Parse(DefaultText());
                    return;
                }
                if (fi.LastWriteTimeUtc == stamp && fi.Length == size) return;
                stamp = fi.LastWriteTimeUtc;
                size = fi.Length;
                List<string> read = Parse(File.ReadAllText(Path)); // UTF-8 with or without BOM
                if (read.Count > 0 || phrases.Count == 0) phrases = read;
                LastError = null;
            }
            catch (Exception ex) { LastError = ex.Message; } // e.g. open in an editor that locks it: keep the old list
        }

        /// <summary>One phrase per line; empty lines and «#» comments skipped; list bullets and the quotes
        /// around a whole phrase (as it was sent: * «…») taken off.</summary>
        public static List<string> Parse(string text)
        {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return list;
            foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string s = raw.Trim().TrimStart('﻿').Trim();
                if (s.Length == 0 || s[0] == '#') continue;
                if (s.Length > 1 && (s[0] == '*' || s[0] == '•' || s[0] == '-' || s[0] == '—') && s[1] == ' ') s = s.Substring(2).Trim();
                if (s.Length > 1 && ((s[0] == '«' && s[s.Length - 1] == '»') || (s[0] == '"' && s[s.Length - 1] == '"'))
                    && s.IndexOf(s[s.Length - 1], 1) == s.Length - 1)
                    s = s.Substring(1, s.Length - 2).Trim();
                if (s.Length > MaxLength) s = s.Substring(0, MaxLength).TrimEnd() + "…";
                if (s.Length > 0 && seen.Add(s)) list.Add(s); // a line pasted twice is still one phrase
            }
            return list;
        }

        /// <summary>The phrases built into the mod (the file <see cref="FileName"/> in the source).</summary>
        public static string DefaultText()
        {
            using (Stream s = typeof(PhraseBook).Assembly.GetManifestResourceStream(Resource))
            {
                if (s == null) return "";
                using (StreamReader r = new StreamReader(s, Encoding.UTF8, true)) return r.ReadToEnd();
            }
        }
    }
}
