using System;
using System.Drawing;
using System.Windows.Forms;

namespace GooseDeluxe
{
    /// <summary>The goose's icon in the notification area: everything the mod can do, one right-click away.</summary>
    internal sealed class Tray : IDisposable
    {
        private readonly NotifyIcon icon;
        private readonly ContextMenuStrip menu;
        private readonly Controller c;
        private readonly ToolStripMenuItem pauseItem, muteItem, mouseItem;
        private readonly ToolStripMenuItem friendsMenu, sendNoteItem, sendPicItem, visitItem, prankStealItem, prankMemeItem, prankMudItem;
        private readonly ToolStripMenuItem myCodeItem, inviteItem, phoneItem, friendItem, statusItem;

        public Tray(Controller controller, Icon appIcon)
        {
            c = controller;
            menu = new ContextMenuStrip();

            ToolStripMenuItem panel = Add(menu.Items, "Пульт и настройки…", "Ctrl+Alt+M", (s, e) => ControlPanel.Open(c));
            panel.Font = new Font(panel.Font, FontStyle.Bold);
            Add(menu.Items, "Проверка…", null, (s, e) => ControlPanel.Open(c, 2));
            menu.Items.Add(new ToolStripSeparator());
            Add(menu.Items, "Позвать гуся", "Ctrl+Alt+G", (s, e) => c.Come());
            Add(menu.Items, "Гудок", "Ctrl+Alt+H", (s, e) => c.HonkNow());
            Add(menu.Items, "Принести мем", null, (s, e) => c.RunGooseTask("CollectMeme"));
            Add(menu.Items, "Принести записку", null, (s, e) => c.RunGooseTask("CollectNotepad"));
            Add(menu.Items, "Наследить грязью", null, (s, e) => c.RunGooseTask("TrackMud"));
            Add(menu.Items, "Украсть курсор", null, (s, e) => c.RunGooseTask("NabMouse"));
            menu.Items.Add(new ToolStripSeparator());

            friendsMenu = new ToolStripMenuItem("Гусь к другу");
            sendNoteItem = Add(friendsMenu.DropDownItems, "Отправить записку…", null, (s, e) => c.SendNote(null, null));
            sendPicItem = Add(friendsMenu.DropDownItems, "Отправить картинку…", null, (s, e) => c.SendPicture());
            friendsMenu.DropDownItems.Add(new ToolStripSeparator());
            visitItem = Add(friendsMenu.DropDownItems, "Сбегать в гости (гудок)", null, (s, e) => c.SendPrank(GooseCommand.Honk));
            prankMemeItem = Add(friendsMenu.DropDownItems, "Принести другу мем", null, (s, e) => c.SendPrank(GooseCommand.Meme));
            prankMudItem = Add(friendsMenu.DropDownItems, "Наследить у друга", null, (s, e) => c.SendPrank(GooseCommand.Mud));
            prankStealItem = Add(friendsMenu.DropDownItems, "Украсть курсор у друга", null, (s, e) => c.SendPrank(GooseCommand.Steal));
            friendsMenu.DropDownItems.Add(new ToolStripSeparator());
            myCodeItem = Add(friendsMenu.DropDownItems, "Мой код", null, (s, e) => c.CopyMyCode());
            inviteItem = Add(friendsMenu.DropDownItems, "Скопировать приглашение для друга", null, (s, e) => c.CopyInvite());
            phoneItem = Add(friendsMenu.DropDownItems, "Скопировать ссылку-пульт для телефона", null, (s, e) => c.CopyPhoneLink());
            friendItem = Add(friendsMenu.DropDownItems, "Добавить друга…", null, (s, e) => c.EditFriend());
            statusItem = new ToolStripMenuItem("Связь: …") { Enabled = false };
            friendsMenu.DropDownItems.Add(statusItem);
            menu.Items.Add(friendsMenu);
            menu.Items.Add(new ToolStripSeparator());

            pauseItem = Add(menu.Items, "Пауза (гусь спит)", "Ctrl+Alt+P", (s, e) => c.TogglePause());
            muteItem = Add(menu.Items, "Без звука", null, (s, e) => c.ToggleMute());
            mouseItem = Add(menu.Items, "Гусь может красть курсор", null, (s, e) => c.ToggleMouseStealing());
            Add(menu.Items, "Настройки…", null, (s, e) => ControlPanel.Open(c, 1));
            menu.Items.Add(new ToolStripSeparator());
            Add(menu.Items, "Выгнать гуся", null, (s, e) => c.Exit());

            menu.Opening += (s, e) => Refresh();

            icon = new NotifyIcon
            {
                Icon = appIcon ?? SystemIcons.Application,
                Text = "Гусь (GooseDeluxe)",
                ContextMenuStrip = menu,
                Visible = true,
            };
            icon.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                try { ControlPanel.Open(c); }
                catch (Exception ex) { Deluxe.Log("Panel failed to open: " + ex); }
            };
            icon.BalloonTipClicked += (s, e) => c.BalloonClicked();
        }

        private static ToolStripMenuItem Add(ToolStripItemCollection items, string text, string keys, EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            if (keys != null) item.ShortcutKeyDisplayString = keys;
            item.Click += (s, e) =>
            {
                try { onClick(s, e); }
                catch (Exception ex) { Deluxe.Log("Tray action '" + text + "' failed: " + ex); }
            };
            items.Add(item);
            return item;
        }

        private void Refresh()
        {
            pauseItem.Checked = Deluxe.Sleeping;
            muteItem.Checked = GooseSettings.SilenceSounds;
            muteItem.Enabled = GooseSettings.Available;
            mouseItem.Checked = GooseSettings.CanAttackMouse;
            mouseItem.Enabled = GooseSettings.Available;

            FriendService f = Deluxe.Friends;
            friendsMenu.Enabled = f != null;
            if (f == null) return;
            bool friend = f.HasFriend;
            string name = friend ? f.FriendName : "друг";
            sendNoteItem.Text = "Отправить записку " + (friend ? "(" + name + ")" : "") + "…";
            foreach (ToolStripMenuItem i in new[] { sendPicItem, visitItem, prankMemeItem, prankMudItem, prankStealItem }) i.Enabled = friend;
            myCodeItem.Text = "Мой код: " + GooseCode.Display(f.MyCode) + " (скопировать)";
            friendItem.Text = friend ? "Друг: " + f.FriendName + " — изменить…" : "Добавить друга…";
            statusItem.Text = f.Connected ? "Связь есть (" + HostOf(f.Server) + ")" : "Нет связи с " + HostOf(f.Server) + ", переподключаюсь…";
            inviteItem.Enabled = phoneItem.Enabled = true;
        }

        private static string HostOf(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host : url;
        }

        /// <summary>The same menu at the mouse (right-click on the goose).</summary>
        public void ShowMenuAt(Point at)
        {
            Refresh();
            menu.Show(at);
            // without being the foreground window the menu wouldn't close when clicking elsewhere
            Native.SetForegroundWindow(menu.Handle);
        }

        public void Balloon(string title, string text)
        {
            try { icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info); } catch { }
        }

        public void Dispose()
        {
            try { icon.Visible = false; icon.Dispose(); } catch { }
            try { menu.Dispose(); } catch { }
        }
    }
}
