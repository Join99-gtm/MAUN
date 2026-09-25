using System;
using System.Drawing;
using System.Windows.Forms;

namespace GooseDeluxe
{
    /// <summary>"Записка для …": a short text the goose will carry to a friend.</summary>
    internal sealed class SendNoteDialog : Form
    {
        private readonly TextBox box;

        public string NoteText { get { return box.Text.Trim(); } }

        public SendNoteDialog(string friendName)
        {
            Text = "Записка для " + friendName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(400, 262);

            Label hint = new Label
            {
                Text = "Гусь унесёт записку за край экрана, а у " + friendName + " из-за края выйдет твой гусь с ней в клюве.",
                Location = new Point(12, 10),
                Size = new Size(376, 36),
            };
            box = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                MaxLength = FriendProtocol.MaxTextLength,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(12, 50),
                Size = new Size(376, 140),
            };
            Label warn = new Label
            {
                Text = "Записки идут через открытый сервер ntfy.sh — пароли и секреты не пиши.",
                ForeColor = Color.DimGray,
                Location = new Point(12, 194),
                Size = new Size(376, 20),
            };
            Button ok = new Button { Text = "Отправить", Location = new Point(212, 222), Size = new Size(84, 30) };
            Button cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Location = new Point(304, 222), Size = new Size(84, 30) };
            ok.Click += (s, e) =>
            {
                if (NoteText.Length == 0) { box.Focus(); return; }
                DialogResult = DialogResult.OK;
            };
            box.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ok.PerformClick(); }
            };
            CancelButton = cancel;
            Controls.AddRange(new Control[] { hint, box, warn, ok, cancel });
        }
    }

    /// <summary>Friend's goose code, friend's name and how to sign our own notes.</summary>
    internal sealed class FriendDialog : Form
    {
        private readonly TextBox code, friendName, myName;

        public string Code { get { return GooseCode.Normalize(code.Text); } }
        public string FriendName { get { return friendName.Text.Trim(); } }
        public string MyName { get { return myName.Text.Trim(); } }

        public FriendDialog(string currentCode, string currentFriendName, string currentMyName, string myCode)
        {
            Text = "Добавить друга";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(420, 268);

            int y = 12;
            Func<string, Label> label = t =>
            {
                Label l = new Label { Text = t, Location = new Point(12, y), Size = new Size(396, 20) };
                y += 22;
                return l;
            };
            Func<string, TextBox> field = v =>
            {
                TextBox tb = new TextBox { Text = v ?? "", Location = new Point(12, y), Size = new Size(396, 24) };
                y += 36;
                return tb;
            };

            Label l1 = label("Код гуся друга (у него в меню гуся: «Гусь к другу» → «Мой код»):");
            code = field(currentCode != null ? GooseCode.Display(currentCode) : "");
            Label l2 = label("Как зовут друга:");
            friendName = field(currentFriendName);
            Label l3 = label("Твоё имя (его увидят над твоим гусём у друга):");
            myName = field(currentMyName);
            Label mine = new Label
            {
                Text = "Твой код: " + GooseCode.Display(myCode) + " — отправь его другу.",
                ForeColor = Color.DimGray,
                Location = new Point(12, y),
                Size = new Size(396, 20),
            };
            Button ok = new Button { Text = "Сохранить", Location = new Point(232, 230), Size = new Size(84, 30) };
            Button cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Location = new Point(324, 230), Size = new Size(84, 30) };
            ok.Click += (s, e) =>
            {
                if (Code == null)
                {
                    MessageBox.Show(this, "Это не похоже на код гуся. Он выглядит так: GUS-ABCD-EFGH-JKLM.", "Гусь к другу", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    code.Focus();
                    return;
                }
                if (Code == myCode)
                {
                    MessageBox.Show(this, "Это код твоего же гуся. Нужен код гуся друга.", "Гусь к другу", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
            };
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { l1, code, l2, friendName, l3, myName, mine, ok, cancel });
        }
    }
}
