using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace GooseDeluxe
{
    /// <summary>
    /// The goose's mailbox: its own code and name, the friend it writes to, the ntfy subscription and
    /// the outgoing requests. Identity lives in GooseFriend.ini next to the mod so hand-edited
    /// GooseDeluxe.ini is never rewritten. Incoming messages are queued; the UI thread picks them up.
    /// </summary>
    internal sealed class FriendService
    {
        public const int MaxQueued = 8;

        public string MyCode { get; private set; }
        public string MyName;
        public string FriendCode;
        public string FriendName;
        public string LastSenderCode;
        public string LastSenderName;

        public readonly ConcurrentQueue<Incoming> Inbox = new ConcurrentQueue<Incoming>();
        public Func<string[]> MyColors = () => new[] { "#ffffff", "#ffa500", "#d3d3d3" };
        public Func<HatStyle> MyHat = () => HatStyle.None;
        public Action<Action> RunOnUi = a => Deluxe.UiQueue.Enqueue(a);

        private readonly string storePath;
        private readonly NtfyClient client;
        private bool started;

        public bool Connected { get { return client.Connected; } }
        public string Server { get { return client.Server; } }
        public NtfyClient Client { get { return client; } }

        public FriendService(string server, string storePath)
        {
            this.storePath = storePath;
            client = new NtfyClient(server);
            client.Log = s => Deluxe.Log(s);
            Load();
        }

        public bool HasFriend { get { return !string.IsNullOrEmpty(FriendCode); } }
        public string PhoneLink { get { return Server + "/" + GooseCode.Topic(MyCode); } }

        public string InviteText()
        {
            return "Привет! Это мой гусь." + Environment.NewLine +
                   "1) Поставь себе мод GooseDeluxe (архив, который я тебе кинул) и добавь моего гуся:" + Environment.NewLine +
                   "   меню гуся в трее → «Гусь к другу» → «Добавить друга…» → код " + GooseCode.Display(MyCode) + Environment.NewLine +
                   "2) Или командуй моим гусём прямо с телефона: " + PhoneLink + Environment.NewLine +
                   "   пиши туда: га, мем, записка, сюда, грязь, кража, фраза, «скажи …» — или любой текст, гусь принесёт его мне запиской.";
        }

        public void Start()
        {
            if (started) return;
            started = true;
            client.Subscribe(GooseCode.Topic(MyCode), OnMessage);
        }

        public void Stop() { client.Stop(); }

        private void OnMessage(NtfyMessage m)
        {
            Incoming x = FriendProtocol.Parse(m, client.Server);
            if (x == null) return;
            if (x.Kind == IncomingKind.Image)
            {
                x.ImageBytes = client.Download(x.AttachmentUrl, FriendProtocol.MaxImageBytes);
                if (x.ImageBytes == null) return;
            }
            if (Inbox.Count >= MaxQueued)
            {
                Deluxe.Log("Inbox full, dropped a message from " + (x.FromName ?? "?"));
                return;
            }
            Inbox.Enqueue(x);
        }

        private string Tags() { return FriendProtocol.Tags(MyCode, MyColors(), MyHat()); }

        public void SendNote(string toCode, string text, Action<bool> done)
        {
            Background(() => client.PublishText(GooseCode.Topic(toCode), text, MyName, Tags()), done);
        }

        public void SendCommand(string toCode, GooseCommand cmd, Action<bool> done)
        {
            Background(() => client.PublishText(GooseCode.Topic(toCode), FriendProtocol.CommandWord(cmd), MyName, Tags()), done);
        }

        public void SendImage(string toCode, byte[] data, string fileName, Action<bool> done)
        {
            Background(() => client.PublishFile(GooseCode.Topic(toCode), data, fileName, null, MyName, Tags()), done);
        }

        private void Background(Func<bool> work, Action<bool> done)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                try { ok = work(); }
                catch (Exception ex) { Deluxe.Log("send failed: " + ex); }
                if (done != null) RunOnUi(() => done(ok));
            });
        }

        // ---- GooseFriend.ini ----

        private void Load()
        {
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(storePath))
                {
                    foreach (string raw in File.ReadAllLines(storePath))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                        int eq = line.IndexOf('=');
                        if (eq > 0) kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
            }
            catch (Exception ex) { Deluxe.Log("GooseFriend.ini unreadable: " + ex.Message); }

            string v;
            MyCode = kv.TryGetValue("MyCode", out v) ? GooseCode.Normalize(v) : null;
            MyName = kv.TryGetValue("MyName", out v) && v.Length > 0 ? v : SafeUserName();
            FriendCode = kv.TryGetValue("FriendCode", out v) ? GooseCode.Normalize(v) : null;
            FriendName = kv.TryGetValue("FriendName", out v) && v.Length > 0 ? v : "Друг";
            // The code must be this computer's own. A goose folder copied to someone else carries this file along —
            // then both geese had one code (it happened: «у меня и у друга тот же код»). The file remembers whose
            // computer it was made on; files from before 0.8.3 don't, so those get a new code once too.
            string here = MachineId(), madeOn;
            kv.TryGetValue("Machine", out madeOn);
            bool copied = MyCode != null && madeOn != here;
            if (copied)
            {
                Deluxe.Log("GooseFriend.ini " + (string.IsNullOrEmpty(madeOn) ? "from an older version" : "copied from another computer") + ": a new goose code");
                if (!string.IsNullOrEmpty(madeOn))
                {
                    // someone else's file: their name and their friend aren't ours
                    MyName = SafeUserName();
                    FriendCode = null;
                    FriendName = "Друг";
                }
            }
            bool fresh = MyCode == null || copied;
            if (fresh) MyCode = GooseCode.Generate();
            machine = here;
            if (fresh || !File.Exists(storePath)) Save();
        }

        private string machine;

        /// <summary>This computer and Windows user, as a short hash (Windows' MachineGuid, computer and user name).</summary>
        private static string MachineId()
        {
            string guid = "";
            try
            {
                using (Microsoft.Win32.RegistryKey root = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                using (Microsoft.Win32.RegistryKey k = root.OpenSubKey("SOFTWARE\\Microsoft\\Cryptography"))
                    if (k != null) guid = Convert.ToString(k.GetValue("MachineGuid"));
            }
            catch { }
            string raw = (guid + "|" + Environment.MachineName + "|" + Environment.UserName).ToLowerInvariant();
            using (System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)), 0, 8).Replace("-", "").ToLowerInvariant();
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; Гусь к другу. MyCode — адрес твоего гуся: кто его знает, может писать твоему гусю.");
                sb.AppendLine("MyCode=" + GooseCode.Display(MyCode));
                sb.AppendLine("MyName=" + (MyName ?? ""));
                sb.AppendLine("FriendCode=" + (FriendCode != null ? GooseCode.Display(FriendCode) : ""));
                sb.AppendLine("FriendName=" + (FriendName ?? ""));
                sb.AppendLine("; чей это компьютер: если папку гуся скопируют на другой, там у гуся будет свой код");
                sb.AppendLine("Machine=" + (machine ?? MachineId()));
                File.WriteAllText(storePath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex) { Deluxe.Log("GooseFriend.ini not saved: " + ex.Message); }
        }

        private static string SafeUserName()
        {
            try { string n = Environment.UserName; return string.IsNullOrEmpty(n) ? "Друг" : n; }
            catch { return "Друг"; }
        }
    }
}
