using System;
using System.IO;
using System.Text;

namespace GooseDeluxe
{
    /// <summary>
    /// «Русские гуси не хонкают». Russian honk words and Not-epad notes. The goose picks notes from
    /// Assets/Text/NotepadMessages/*.txt, so we add ours there and park its English ones as *.txt.en
    /// (renamed back, and ours removed, when RussianNotes is switched off).
    /// </summary>
    internal static class RussianPack
    {
        public static readonly string[] HonkWordsRu = { "ГА-ГА-ГА!", "ГА!", "га-га", "ГАААА!", "ГА-ГА!" };
        public static readonly string[] HonkWordsEn = { "HONK!", "HJONK!", "honk", "HONK HONK", "hjonk" };

        // gooseASCII1.txt stays: it's a picture, not words
        private static readonly string[] EnglishNotes =
        {
            "am goose.txt", "good work.txt", "hard to type.txt", "i cause problems.txt", "peace was never.txt"
        };

        private static readonly string[] Notes =
        {
            "я гусь. га.",
            "хорошо поработал.\nа теперь дай хлеба",
            "ывапролджэ\nсорри\nтрудно печатать лапами",
            "я создаю проблемы специально",
            "«мира никогда не было в планах»\n   — гусь (я)",
            "я видел, что ты открыл.\nникому не скажу.\nпока.",
            "ГА-ГА-ГА\n(это значит «привет»)",
            "твой курсор теперь мой.\nшучу.\nили нет.",
            "попей воды. гусь следит.",
            "эту записку писал гусь.\nклюв устал.",
            "ctrl+z не поможет.\nя уже здесь.",
            "где хлеб, лебовски?",
            "не закрывай меня пж\n...\nну всё, я обиделся",
            "сохрани файл.\nсейчас.\nСЕЙЧАС.",
            "я не гусь. я лебедь.\nпросто ещё не вырос.",
        };

        public static string[] HonkWords(string language)
        {
            return string.Equals(language, "EN", StringComparison.OrdinalIgnoreCase) ? HonkWordsEn : HonkWordsRu;
        }

        private static string NoteFile(int i) { return "ru-" + (i + 1).ToString("00") + ".txt"; }
        private static string NoteText(int i) { return Notes[i].Replace("\n", "\r\n"); }

        public static void Apply(string gooseDir, bool russian)
        {
            if (string.IsNullOrEmpty(gooseDir)) return;
            string dir = Path.Combine(Path.Combine(Path.Combine(gooseDir, "Assets"), "Text"), "NotepadMessages");
            if (!Directory.Exists(dir)) return;

            for (int i = 0; i < Notes.Length; i++)
            {
                string path = Path.Combine(dir, NoteFile(i));
                try
                {
                    if (russian)
                    {
                        // UTF-8 with BOM: the goose reads it right, and so does any Notepad
                        if (!File.Exists(path)) File.WriteAllText(path, NoteText(i), new UTF8Encoding(true));
                    }
                    else if (File.Exists(path) && File.ReadAllText(path) == NoteText(i))
                    {
                        File.Delete(path); // only if the user didn't edit it
                    }
                }
                catch (Exception ex) { Deluxe.Log("Russian note " + path + ": " + ex.Message); }
            }

            foreach (string name in EnglishNotes)
            {
                string txt = Path.Combine(dir, name), parked = txt + ".en";
                try
                {
                    if (russian && File.Exists(txt) && !File.Exists(parked)) File.Move(txt, parked);
                    if (!russian && File.Exists(parked) && !File.Exists(txt)) File.Move(parked, txt);
                }
                catch (Exception ex) { Deluxe.Log("English note " + txt + ": " + ex.Message); }
            }
        }
    }
}
