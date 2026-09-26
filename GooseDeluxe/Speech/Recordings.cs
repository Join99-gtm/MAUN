using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GooseDeluxe
{
    /// <summary>
    /// Ready-made voice lines: audio files in the «Голос» folder next to the mod. A file is matched to a phrase
    /// by the words of its name («Остановите_стройку_я_сойду_02.wav» → «Остановите стройку, я сойду!»); a
    /// name that doesn't match any phrase but reads like one becomes a phrase of its own; a nameless one
    /// («Fish_01.wav») is still played, without a bubble. Several files of one phrase are takes, played in turn.
    /// Used on the goose's thread.
    /// </summary>
    internal sealed class Recordings
    {
        public static readonly string[] Extensions = { ".wav", ".mp3", ".wma", ".m4a", ".aac" };
        /// <summary>Marks a key that is a file without a phrase (no bubble text).</summary>
        public const char Nameless = '\u0001';
        private const double RescanSeconds = 3;

        public sealed class Take
        {
            public string Path;
            public string Text = "";  // the phrase, "" if the name says nothing
            public string Key;        // Text, or Nameless + Path
        }

        private static readonly HashSet<string> Noise = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fish", "audio", "voice", "take", "final", "copy", "output", "outputs", "record", "rec", "tts", "clone", "mp3", "wav",
            "голос", "запись", "дубль", "копия", "фраза", "вариант", "гусь", "гуся", "атомик", "atomic", "heart", "харт",
        };

        private readonly string folder;
        private List<Take> takes = new List<Take>();
        private readonly Dictionary<string, int> turn = new Dictionary<string, int>();
        private string signature = "";
        private double checkedAt = -100;

        public string Folder { get { return folder; } }
        public int Files { get { return takes.Count; } }
        public string LastError { get; private set; }

        public Recordings(string folder) { this.folder = folder; }

        /// <summary>Reads the folder again if its files changed (at most every few seconds).</summary>
        public void Refresh(IList<string> phrases, double now)
        {
            if (now - checkedAt < RescanSeconds && now >= checkedAt) return;
            checkedAt = now;
            try
            {
                List<string> files = new List<string>();
                StringBuilder sig = new StringBuilder();
                if (Directory.Exists(folder))
                    foreach (string f in Directory.GetFiles(folder))
                        if (Array.IndexOf(Extensions, System.IO.Path.GetExtension(f).ToLowerInvariant()) >= 0) files.Add(f);
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string f in files)
                {
                    FileInfo fi = new FileInfo(f);
                    sig.Append(fi.Name).Append('|').Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append('\n');
                }
                foreach (string p in phrases) sig.Append(p).Append('\n');
                string s = sig.ToString();
                if (s == signature) return;
                signature = s;
                takes = Scan(files, phrases);
                LastError = null;
            }
            catch (Exception ex) { LastError = ex.Message; }
        }

        private static List<Take> Scan(List<string> files, IList<string> phrases)
        {
            List<Take> list = new List<Take>();
            HashSet<string> seen = new HashSet<string>();
            using (MD5 md5 = MD5.Create())
            {
                foreach (string f in files)
                {
                    try
                    {
                        if (new FileInfo(f).Length > 50L * 1024 * 1024) continue; // not a voice line
                        string hash;
                        using (FileStream s = File.OpenRead(f)) hash = Convert.ToBase64String(md5.ComputeHash(s));
                        if (!seen.Add(hash)) continue; // the same recording twice: one take
                    }
                    catch (IOException) { continue; } // still being written or locked: next time
                    string name = System.IO.Path.GetFileNameWithoutExtension(f);
                    string text = Match(name, phrases) ?? TextFromName(name);
                    list.Add(new Take { Path = f, Text = text, Key = text.Length > 0 ? text : Nameless + f });
                }
            }
            return list;
        }

        /// <summary>Every phrase (or nameless file) that has a recording, in a stable order.</summary>
        public List<string> Keys()
        {
            List<string> keys = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Take t in takes) if (seen.Add(t.Key)) keys.Add(t.Key);
            return keys;
        }

        public int CountFor(string key)
        {
            int n = 0;
            foreach (Take t in takes) if (t.Key == key) n++;
            return n;
        }

        /// <summary>The next take of a phrase (in turn); <paramref name="advance"/>: this one is being used now.</summary>
        public Take NextTake(string key, bool advance)
        {
            if (key == null) return null;
            List<Take> mine = new List<Take>();
            foreach (Take t in takes) if (t.Key == key) mine.Add(t);
            if (mine.Count == 0) return null;
            int i;
            turn.TryGetValue(key, out i);
            Take pick = mine[i % mine.Count];
            if (advance) turn[key] = (i + 1) % mine.Count;
            return pick;
        }

        public List<string> NamelessFiles()
        {
            List<string> names = new List<string>();
            foreach (Take t in takes) if (t.Text.Length == 0) names.Add(System.IO.Path.GetFileName(t.Path));
            return names;
        }

        // ------------------------------------------------------------------ names → phrases

        /// <summary>The phrase a file name stands for: the one sharing most of its words (at least two, and
        /// most of the shorter side's), or null.</summary>
        public static string Match(string fileName, IList<string> phrases)
        {
            List<string> words = Words(fileName, true);
            if (words.Count == 0 || phrases == null) return null;
            string best = null;
            double bestScore = 0;
            int bestCommon = 0;
            foreach (string p in phrases)
            {
                List<string> pw = Words(p, false);
                if (pw.Count == 0) continue;
                int common = 0;
                foreach (string w in words)
                    foreach (string q in pw)
                        if (Same(w, q)) { common++; break; }
                double score = common / (double)Math.Min(words.Count, pw.Count);
                if (common >= Math.Min(2, pw.Count) && score >= 0.5 &&
                    (score > bestScore + 1e-9 || (Math.Abs(score - bestScore) < 1e-9 && (common > bestCommon || (common == bestCommon && best != null && p.Length < best.Length)))))
                {
                    best = p;
                    bestScore = score;
                    bestCommon = common;
                }
            }
            return best;
        }

        /// <summary>A file named like a phrase («Где акты скрытых работ.wav») without one in the list: the name is
        /// the text. Take numbers, «Fish», «копия» and the like are dropped; one word left isn't a phrase.</summary>
        public static string TextFromName(string fileName)
        {
            string s = System.Text.RegularExpressions.Regex.Replace(fileName ?? "", "[_]+", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, "\\(\\d+\\)|\\[\\d+\\]", " ");
            List<string> kept = new List<string>();
            foreach (string raw in s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim('-', '.', ',');
                string bare = t.ToLowerInvariant();
                if (t.Length == 0 || Noise.Contains(bare)) continue;
                if (IsNumber(bare) || System.Text.RegularExpressions.Regex.IsMatch(bare, "^(v|take|дубль)\\d+$")) continue;
                kept.Add(t);
            }
            int letters = 0;
            foreach (string t in kept) if (t.Length > 1 && char.IsLetter(t[0])) letters++;
            if (letters < 2) return "";
            string text = string.Join(" ", kept.ToArray());
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static bool IsNumber(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>Words that tell phrases apart: lower case, ё → е, no take numbers or «fish», nothing of one or
        /// two letters («я», «а», «не»).</summary>
        private static List<string> Words(string s, bool fileName)
        {
            List<string> list = new List<string>();
            StringBuilder w = new StringBuilder();
            foreach (char ch in (s ?? "") + " ")
            {
                char c = char.ToLowerInvariant(ch) == 'ё' ? 'е' : char.ToLowerInvariant(ch);
                if (char.IsLetterOrDigit(c)) { w.Append(c); continue; }
                if (w.Length > 0)
                {
                    string t = w.ToString();
                    w.Length = 0;
                    if (t.Length <= 2 && !IsNumber(t)) continue;
                    if (fileName && (Noise.Contains(t) || IsNumber(t))) continue;
                    list.Add(t);
                }
            }
            return list;
        }

        /// <summary>The same word, give or take its ending («стройку» — «стройка»).</summary>
        private static bool Same(string a, string b)
        {
            if (a == b) return true;
            int n = Math.Min(a.Length, b.Length);
            if (n < 4) return false;
            int stem = Math.Min(5, n);
            return string.CompareOrdinal(a, 0, b, 0, stem) == 0 && Math.Abs(a.Length - b.Length) <= 3;
        }
    }
}
