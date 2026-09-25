using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace GooseDeluxe
{
    /// <summary>
    /// The goose's meme and Not-epad windows. It builds them when the task starts (SimpleImageForm /
    /// SimpleTextForm, with a file picked at plain random) and shows them seconds later from another thread,
    /// once it has walked off-screen. In between, still on the goose's thread and before the window has a
    /// handle, we put in the next file from a no-repeat deck and give the Not-epad a Russian title.
    /// </summary>
    internal sealed class GooseForms
    {
        public const string RussianNotepadTitle = "Гусиный «Блокнот»";
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };

        private readonly NoRepeatDeck memes = new NoRepeatDeck(), notes = new NoRepeatDeck();
        private object lastTaskData;

        public int MemesShown, NotesShown;
        public string LastMeme { get { return memes.Last; } }
        public string LastNote { get { return notes.Last; } }

        /// <summary>Call on the goose's thread whenever it may have started a task.</summary>
        public void Update(object taskData, DeluxeConfig cfg, string gooseDir)
        {
            if (taskData == null || ReferenceEquals(taskData, lastTaskData)) return;
            lastTaskData = taskData;
            FieldInfo f = taskData.GetType().GetField("mainForm", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Form form = f == null ? null : f.GetValue(taskData) as Form;
            if (form == null || form.IsHandleCreated || string.IsNullOrEmpty(gooseDir)) return;

            string kind = form.GetType().Name;
            if (kind == "SimpleImageForm" && cfg.NoRepeats) SwapMeme(form, Path.Combine(Path.Combine(Path.Combine(gooseDir, "Assets"), "Images"), "Memes"));
            else if (kind == "SimpleTextForm")
            {
                if (!string.Equals(cfg.Language, "EN", StringComparison.OrdinalIgnoreCase)) form.Text = RussianNotepadTitle;
                if (cfg.NoRepeats) SwapNote(form, Path.Combine(Path.Combine(Path.Combine(gooseDir, "Assets"), "Text"), "NotepadMessages"));
            }
        }

        private void SwapMeme(Form form, string dir)
        {
            PictureBox box = Find<PictureBox>(form);
            if (box == null || !Directory.Exists(dir)) return;
            List<string> files = new List<string>();
            foreach (string file in Directory.GetFiles(dir))
                if (Array.IndexOf(ImageExtensions, Path.GetExtension(file).ToLowerInvariant()) >= 0) files.Add(file);
            files.Sort(StringComparer.OrdinalIgnoreCase);
            string pick = memes.Next(files);
            if (pick == null) return;
            Image img;
            try { img = Image.FromFile(pick); }
            catch (Exception ex) { Deluxe.Log("meme " + Path.GetFileName(pick) + " can't be opened: " + ex.Message); return; }
            Image old = box.Image;
            box.Image = img;
            if (old != null) old.Dispose(); // the goose's pick keeps its file locked until disposed
            MemesShown++;
            Deluxe.Log("meme: " + Path.GetFileName(pick));
        }

        private void SwapNote(Form form, string dir)
        {
            TextBox box = Find<TextBox>(form);
            if (box == null || !Directory.Exists(dir)) return;
            List<string> files = new List<string>(Directory.GetFiles(dir, "*.txt"));
            // the pattern also matches "*.txt~" style names on some systems; keep real .txt files only
            files.RemoveAll(p => !p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            string pick = notes.Next(files);
            if (pick == null) return;
            try { box.Text = File.ReadAllText(pick); }
            catch (Exception ex) { Deluxe.Log("note " + Path.GetFileName(pick) + " can't be read: " + ex.Message); return; }
            box.Select(box.Text.Length, 0);
            NotesShown++;
            Deluxe.Log("note: " + Path.GetFileName(pick));
        }

        private static T Find<T>(Control parent) where T : Control
        {
            foreach (Control c in parent.Controls)
            {
                T t = c as T;
                if (t != null) return t;
            }
            return null;
        }
    }
}
