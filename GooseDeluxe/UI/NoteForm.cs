using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GooseDeluxe
{
    /// <summary>
    /// A note or picture from a friend, dragged in by a goose. Looks like a sticky note, never steals
    /// focus, and offers "reply by goose" when the sender was a goose.
    /// </summary>
    internal sealed class NoteForm : Form, INoteWindow
    {
        private static readonly Color Paper = Color.FromArgb(255, 252, 240, 170);
        private readonly MemoryStream imageStream;
        private readonly Image image;
        private bool showRequested;

        public NoteForm(DeliveryPayload p, Action<DeliveryPayload> reply)
        {
            string from = string.IsNullOrEmpty(p.FromName) ? "друга" : p.FromName;
            Text = (p.IsImage ? "Картинка от " : "Записка от ") + from;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Paper;
            Location = new Point(-2000, -2000); // parked off-screen until the goose drags it in

            Panel bottom = null;
            if (reply != null && !string.IsNullOrEmpty(p.FromCode))
            {
                bottom = new Panel { Dock = DockStyle.Bottom, Height = 36, BackColor = Paper };
                Button answer = new Button
                {
                    Text = "Ответить гусём",
                    AutoSize = true,
                    Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                    FlatStyle = FlatStyle.System,
                };
                answer.Click += (s, e) => reply(p);
                bottom.Controls.Add(answer);
                bottom.Layout += (s, e) => answer.Location = new Point(bottom.ClientSize.Width - answer.Width - 8, (bottom.ClientSize.Height - answer.Height) / 2);
            }

            if (p.IsImage)
            {
                try
                {
                    // GDI+ needs the stream alive for the image's lifetime (and for GIF animation)
                    imageStream = new MemoryStream(p.ImageBytes);
                    image = Image.FromStream(imageStream);
                }
                catch
                {
                    image = null;
                }
            }

            if (image != null)
            {
                PictureBox pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = image, BackColor = Color.White };
                Controls.Add(pic);
                Size fit = Fit(image.Size, new Size(420, 420), new Size(160, 120));
                ClientSize = new Size(fit.Width, fit.Height + (bottom != null ? bottom.Height : 0));
            }
            else
            {
                string text = p.IsImage ? "Друг прислал картинку, но она не открылась." : (p.Text ?? "");
                TextBox box = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    WordWrap = true,
                    BorderStyle = BorderStyle.None,
                    BackColor = Paper,
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe Print", 11f),
                    Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                    TabStop = false,
                };
                box.ScrollBars = text.Length > 220 ? ScrollBars.Vertical : ScrollBars.None;
                Panel pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 6, 4), BackColor = Paper };
                pad.Controls.Add(box);
                Controls.Add(pad);
                int lines = Math.Min(9, 2 + text.Length / 28 + CountLines(text));
                ClientSize = new Size(300, 30 + lines * 20 + (bottom != null ? bottom.Height : 0));
                box.Select(0, 0);
            }
            if (bottom != null) Controls.Add(bottom);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // ---- INoteWindow ----

        public bool IsGone { get { return IsDisposed || Disposing; } }

        public void MoveTo(int x, int y)
        {
            if (IsGone) return;
            if (Left != x || Top != y) Location = new Point(x, y);
        }

        public void ShowNote()
        {
            if (showRequested) return;
            showRequested = true;
            // not inside the goose's paint handler: the mod's timer shows it a moment later
            Deluxe.UiQueue.Enqueue(() => { if (!IsGone && !Visible) Show(); });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                if (image != null) image.Dispose();
                if (imageStream != null) imageStream.Dispose();
            }
        }

        private static int CountLines(string s)
        {
            int n = 0;
            foreach (char c in s) if (c == '\n') n++;
            return n;
        }

        private static Size Fit(Size img, Size max, Size min)
        {
            float k = Math.Min(1f, Math.Min(max.Width / (float)Math.Max(1, img.Width), max.Height / (float)Math.Max(1, img.Height)));
            int w = Math.Max(min.Width, (int)(img.Width * k));
            int h = Math.Max(min.Height, (int)(img.Height * k));
            return new Size(w, h);
        }
    }
}
