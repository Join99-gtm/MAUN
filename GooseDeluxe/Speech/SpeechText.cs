using System;
using System.Collections.Generic;
using System.Text;

namespace GooseDeluxe
{
    /// <summary>
    /// What the goose's phrases look like to a voice: words with their syllables and the pauses after them
    /// (for timing the speech bubble and for the honk voice), and the text as a Russian speech synthesizer
    /// should read it (abbreviations spelled out: «ПТО» is «пэ-тэ-о», not «пто»).
    /// </summary>
    internal static class SpeechText
    {
        private const string Vowels = "аеёиоуыэюяaeiouy";

        internal sealed class Word
        {
            public int Start, End;       // [Start, End) in the text
            public int Syllables;
            public double PauseAfter;    // seconds of silence after the word (punctuation)
            public char Mark;            // '!', '?', '.', '…' when a sentence ends after the word, else '\0'
            public bool Shout;           // written in capitals (not an abbreviation)
            public string Vowels = "";   // the vowels the word is sung on, one per syllable (а о у э и ы)
            public string Onsets = "";   // the consonant before each syllable's vowel, ' ' for none
        }

        // abbreviations read letter by letter; any all-capital word of 2-3 letters without a vowel is too
        private static readonly HashSet<string> Abbreviations = new HashSet<string>(StringComparer.Ordinal)
        {
            "ПТО", "КС", "ОВ", "ВК", "АР", "КЖ", "КМ", "ЭОМ", "ППР", "ПОС", "ИД", "СМР", "ТЗ", "ГИП", "НДС", "ЖБИ",
            "ИТР", "ОТК", "ВОР", "ЛСР", "РД", "ПД", "ТУ", "ОС", "СК", "ГП", "ТП", "АВР", "ВРУ", "ЩР",
        };

        private static readonly Dictionary<char, string> LetterNames = new Dictionary<char, string>
        {
            { 'А', "а" }, { 'Б', "бэ" }, { 'В', "вэ" }, { 'Г', "гэ" }, { 'Д', "дэ" }, { 'Е', "е" }, { 'Ё', "ё" }, { 'Ж', "жэ" },
            { 'З', "зэ" }, { 'И', "и" }, { 'Й', "й" }, { 'К', "ка" }, { 'Л', "эль" }, { 'М', "эм" }, { 'Н', "эн" }, { 'О', "о" },
            { 'П', "пэ" }, { 'Р', "эр" }, { 'С', "эс" }, { 'Т', "тэ" }, { 'У', "у" }, { 'Ф', "эф" }, { 'Х', "ха" }, { 'Ц', "цэ" },
            { 'Ч', "че" }, { 'Ш', "ша" }, { 'Щ', "ща" }, { 'Ы', "ы" }, { 'Э', "э" }, { 'Ю', "ю" }, { 'Я', "я" },
        };

        private static readonly string[] DigitNames = { "ноль", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять" };

        public static bool IsVowel(char c) { return Vowels.IndexOf(char.ToLowerInvariant(c)) >= 0; }

        /// <summary>The vowel a letter is sung as: я → а, ё → о, ю → у, е → э, i → и…</summary>
        public static char Plain(char v)
        {
            switch (char.ToLowerInvariant(v))
            {
                case 'а': case 'я': case 'a': return 'а';
                case 'о': case 'ё': case 'o': return 'о';
                case 'у': case 'ю': case 'u': return 'у';
                case 'э': case 'е': case 'e': return 'э';
                case 'и': case 'i': return 'и';
                default: return 'ы';
            }
        }

        public static List<Word> Words(string text)
        {
            List<Word> words = new List<Word>();
            if (string.IsNullOrEmpty(text)) return words;
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsLetterOrDigit(text[i]))
                {
                    int j = i;
                    while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '\'' || text[j] == '’')) j++;
                    words.Add(MakeWord(text, i, j));
                    i = j;
                    continue;
                }
                if (words.Count > 0)
                {
                    Word w = words[words.Count - 1];
                    char c = text[i];
                    double pause = PauseFor(c);
                    if (c == '.' && i + 2 < text.Length && text[i + 1] == '.' && text[i + 2] == '.') { pause = 0.45; c = '…'; i += 2; }
                    w.PauseAfter = Math.Max(w.PauseAfter, pause);
                    if (c == '?' || (c == '!' && w.Mark != '?') || ((c == '.' || c == '…') && w.Mark == '\0')) w.Mark = c;
                }
                i++;
            }
            return words;
        }

        private static Word MakeWord(string text, int from, int to)
        {
            string w = text.Substring(from, to - from);
            Word word = new Word { Start = from, End = to };
            StringBuilder vowels = new StringBuilder(), onsets = new StringBuilder();

            int caps = 0;
            while (caps < w.Length && char.IsUpper(w[caps])) caps++;
            string capsPart = w.Substring(0, caps);
            int spelled = caps >= 2 && (Abbreviations.Contains(capsPart) || (caps == w.Length && caps <= 3 && !HasVowel(capsPart))) ? caps : 0;
            for (int k = 0; k < spelled; k++)
            {
                string name;
                if (!LetterNames.TryGetValue(w[k], out name)) name = "э";
                AddSyllables(name, vowels, onsets);
            }
            string rest = w.Substring(spelled);
            if (rest.Length == 1 && char.IsUpper(rest[0]) && !IsVowel(rest[0]) && NextToHyphen(text, from, to))
            {
                string name; // «П-Т-О»: letters spelled one by one
                AddSyllables(LetterNames.TryGetValue(rest[0], out name) ? name : "э", vowels, onsets);
                rest = "";
            }
            if (rest.Length > 0)
            {
                bool digits = true;
                foreach (char ch in rest) if (ch < '0' || ch > '9') digits = false; // not char.IsDigit: «٣», «３» are digits too
                if (digits)
                    foreach (char ch in rest) AddSyllables(DigitNames[ch - '0'], vowels, onsets);
                else if (HasVowel(rest)) AddSyllables(rest.ToLowerInvariant(), vowels, onsets);
                else if (vowels.Length == 0 && rest.Length > 1) { vowels.Append('ы'); onsets.Append(char.ToLowerInvariant(rest[0])); } // «хм», «брр»
                // a lone «в», «к», «с» is said with the next word: no syllable of its own
            }
            word.Vowels = vowels.ToString();
            word.Onsets = onsets.ToString();
            word.Syllables = word.Vowels.Length;
            word.Shout = spelled == 0 && caps == w.Length && caps >= 2 && HasVowel(w);
            return word;
        }

        /// <summary>One syllable per vowel; a run of the same vowel («дураааак») stays one (long) syllable.</summary>
        private static void AddSyllables(string s, StringBuilder vowels, StringBuilder onsets)
        {
            char before = ' ';
            for (int k = 0; k < s.Length; k++)
            {
                char c = s[k];
                if (IsVowel(c))
                {
                    if (k > 0 && char.ToLowerInvariant(s[k - 1]) == char.ToLowerInvariant(c)) continue;
                    vowels.Append(Plain(c));
                    onsets.Append(before);
                    before = ' ';
                }
                else if (char.IsLetter(c) && c != 'ь' && c != 'ъ') before = char.ToLowerInvariant(c);
            }
        }

        private static bool NextToHyphen(string text, int from, int to)
        {
            return (from > 0 && text[from - 1] == '-') || (to < text.Length && text[to] == '-');
        }

        public static bool HasVowel(string s)
        {
            foreach (char c in s) if (IsVowel(c)) return true;
            return false;
        }

        private static double PauseFor(char c)
        {
            switch (c)
            {
                case ',': return 0.16;
                case ';': case ':': return 0.2;
                case '—': case '–': return 0.14;
                case '-': return 0.05;
                case '.': case '!': case '?': return 0.3;
                case '…': return 0.45;
                case '(': case ')': return 0.1;
                default: return 0;
            }
        }

        /// <summary>How many syllables the phrase has (for a voice-less bubble's pace).</summary>
        public static int SyllableCount(string text)
        {
            int n = 0;
            foreach (Word w in Words(text)) n += w.Syllables;
            return n;
        }

        /// <summary>
        /// When each word shows up if the whole phrase takes <paramref name="seconds"/>: in proportion to the
        /// syllables before it, pauses included. Returns the start time of every word.
        /// </summary>
        public static double[] Schedule(List<Word> words, double seconds)
        {
            double[] at = new double[words.Count];
            double total = 0;
            foreach (Word w in words) total += w.Syllables * 0.14 + 0.04 + w.PauseAfter;
            if (words.Count > 0) total -= words[words.Count - 1].PauseAfter;
            double scale = total > 0 ? seconds / total : 0, t = 0;
            for (int i = 0; i < words.Count; i++)
            {
                at[i] = t * scale;
                t += words[i].Syllables * 0.14 + 0.04 + words[i].PauseAfter;
            }
            return at;
        }

        /// <summary>
        /// The phrase as the Windows voice should read it: abbreviations spelled («ПТОшник» → «пэтэошник»,
        /// «КС-2» → «ка-эс-2»), hyphenated syllables joined («Ни-ху-я» → «ни ху я»), stretched vowels shortened.
        /// </summary>
        public static string ForSynthesizer(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            StringBuilder sb = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                if (!char.IsLetter(text[i])) { sb.Append(text[i] == '-' && i > 0 && i + 1 < text.Length && char.IsLetter(text[i - 1]) && char.IsLetter(text[i + 1]) && IsSyllableHyphen(text, i) ? ' ' : text[i]); i++; continue; }
                int j = i;
                while (j < text.Length && char.IsLetter(text[j])) j++;
                string w = text.Substring(i, j - i);
                int caps = 0;
                while (caps < w.Length && char.IsUpper(w[caps])) caps++;
                string capsPart = w.Substring(0, caps);
                bool spell = caps >= 2 && (Abbreviations.Contains(capsPart) || (caps == w.Length && caps <= 3 && !HasVowel(capsPart)));
                if (spell)
                {
                    List<string> names = new List<string>();
                    foreach (char ch in capsPart) { string n; names.Add(LetterNames.TryGetValue(ch, out n) ? n : ch.ToString()); }
                    string rest = w.Substring(caps).ToLowerInvariant();
                    sb.Append(rest.Length > 0 ? string.Join("", names.ToArray()) + rest : string.Join("-", names.ToArray()));
                }
                else if (w.Length == 1 && char.IsUpper(w[0]) && !IsVowel(w[0]) && NextToHyphen(text, i, j))
                {
                    string n;
                    sb.Append(LetterNames.TryGetValue(w[0], out n) ? n : w);
                }
                else sb.Append(Unstretch(w));
                i = j;
            }
            return sb.ToString();
        }

        /// <summary>«Ни-ху-я», «П-Т-О»: a hyphen between single syllables or letters (not «гусь-технадзор»).</summary>
        private static bool IsSyllableHyphen(string text, int at)
        {
            int a = at - 1;
            while (a > 0 && char.IsLetter(text[a - 1])) a--;
            int b = at + 1;
            while (b < text.Length && char.IsLetter(text[b])) b++;
            return at - a <= 2 || b - at - 1 <= 2;
        }

        private static string Unstretch(string w)
        {
            StringBuilder sb = new StringBuilder(w.Length);
            int run = 0;
            for (int k = 0; k < w.Length; k++)
            {
                run = k > 0 && char.ToLowerInvariant(w[k]) == char.ToLowerInvariant(w[k - 1]) && IsVowel(w[k]) ? run + 1 : 0;
                if (run < 2) sb.Append(w[k]);
            }
            return sb.ToString();
        }
    }
}
