using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using GooseDeluxe;

namespace Tests
{
    /// <summary>Two FriendServices talking through tools/mock_ntfy.py, the way two geese talk through ntfy.sh.</summary>
    internal static class NetTests
    {
        private static readonly byte[] Png =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0xF0,
            0x1F, 0x00, 0x05, 0x00, 0x01, 0xFF, 0x89, 0x99, 0x3D, 0x1D, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45,
            0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
        };

        public static void Run(string mockScript, Action<string, bool, string> check)
        {
            string dir = Path.Combine(Path.GetTempPath(), "goosedeluxe-net-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            string portFile = Path.Combine(dir, "port");
            Process mock = Process.Start(new ProcessStartInfo("python3", "\"" + mockScript + "\" \"" + portFile + "\"") { UseShellExecute = false });
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                while (!File.Exists(portFile) || new FileInfo(portFile).Length == 0)
                {
                    if (sw.Elapsed.TotalSeconds > 10) throw new Exception("mock server did not start");
                    Thread.Sleep(50);
                }
                Thread.Sleep(100);
                string server = "http://127.0.0.1:" + File.ReadAllText(portFile).Trim();
                Body(server, dir, check);
            }
            finally
            {
                try { mock.Kill(); } catch { }
            }
        }

        private static bool WaitFor(Func<bool> cond, double seconds)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                if (cond()) return true;
                Thread.Sleep(50);
            }
            return cond();
        }

        private static Incoming Take(FriendService f, Func<Incoming, bool> match, double seconds)
        {
            Incoming found = null;
            WaitFor(() =>
            {
                List<Incoming> keep = new List<Incoming>();
                Incoming x;
                while (f.Inbox.TryDequeue(out x))
                {
                    if (found == null && match(x)) found = x; else keep.Add(x);
                }
                foreach (Incoming k in keep) f.Inbox.Enqueue(k);
                return found != null;
            }, seconds);
            return found;
        }

        private static bool Send(Action<Action<bool>> send, double seconds = 10)
        {
            bool? result = null;
            send(ok => result = ok);
            WaitFor(() => result.HasValue, seconds);
            return result == true;
        }

        private static void Body(string server, string dir, Action<string, bool, string> check)
        {
            FriendService a = new FriendService(server, Path.Combine(dir, "A.ini")) { RunOnUi = act => act() };
            FriendService b = new FriendService(server, Path.Combine(dir, "B.ini")) { RunOnUi = act => act() };
            a.MyName = "Коля";
            b.MyName = "Вася";
            a.MyHat = () => HatStyle.TopHat;
            a.MyColors = () => new[] { "#ffffff", "#ff8800", "#cccccc" };
            check("each goose got its own code, saved to its ini", a.MyCode != b.MyCode && File.ReadAllText(Path.Combine(dir, "A.ini")).Contains(GooseCode.Display(a.MyCode)), null);
            FriendService a2 = new FriendService(server, Path.Combine(dir, "A.ini"));
            check("the code survives a restart", a2.MyCode == a.MyCode, null);

            a.Start();
            b.Start();
            check("both geese connected", WaitFor(() => a.Connected && b.Connected, 10), null);

            check("A sends a note", Send(done => a.SendNote(b.MyCode, "Привет, гусь! ёЁ\nвторая строка", done)), null);
            Incoming note = Take(b, x => x.Kind == IncomingKind.Note, 5);
            check("B receives it intact", note != null && note.Text == "Привет, гусь! ёЁ\nвторая строка", note != null ? note.Text : "nothing");
            check("with A's code, name and look (a visitor will come)", note != null && note.FromCode == a.MyCode && note.FromName == "Коля"
                  && note.Guest != null && note.Guest.Hat == HatStyle.TopHat && note.Guest.Orange == "#ff8800", null);

            check("B answers with a honk", Send(done => b.SendCommand(a.MyCode, GooseCommand.Honk, done)), null);
            Incoming honk = Take(a, x => x.Kind == IncomingKind.Command, 5);
            check("A gets the honk command from B", honk != null && honk.Command == GooseCommand.Honk && honk.FromCode == b.MyCode && honk.Guest != null, null);

            check("A sends a picture", Send(done => a.SendImage(b.MyCode, Png, "мем.png", done)), null);
            Incoming pic = Take(b, x => x.Kind == IncomingKind.Image, 8);
            check("B downloads the same bytes", pic != null && pic.ImageBytes != null && pic.ImageBytes.SequenceEqual(Png) && pic.FileName == "мем.png", null);

            using (HttpClient http = new HttpClient())
            {
                // someone with the phone link types "мем" in the ntfy app: no tags, no title
                http.PostAsync(server + "/" + GooseCode.Topic(b.MyCode), new StringContent("мем", Encoding.UTF8, "text/plain")).Wait();
                Incoming phone = Take(b, x => x.Kind == IncomingKind.Command, 5);
                check("a phone message is a command for B's own goose", phone != null && phone.Command == GooseCommand.Meme && phone.Guest == null && phone.FromCode == null, null);

                byte[] webp = Encoding.ASCII.GetBytes("RIFF\x10\x00\x00\x00WEBPVP8 ");
                http.PutAsync(server + "/" + GooseCode.Topic(b.MyCode) + "?filename=x.webp", new ByteArrayContent(webp)).Wait();
                Incoming unsupported = Take(b, x => x.Kind == IncomingKind.UnsupportedFile, 5);
                check("a WebP arrives as 'can't carry this'", unsupported != null && unsupported.FileName == "x.webp" && unsupported.ImageBytes == null, null);

                string evil = "{\"message\":\"You received a file: x.png\",\"attachment\":{\"name\":\"x.png\",\"type\":\"image/png\",\"size\":10,\"url\":\"http://evil.example/file/x.png\"},\"tags\":[\"gd\",\"from-" + a.MyCode.ToLowerInvariant() + "\"],\"title\":\"Коля\"}";
                http.PostAsync(server + "/_inject/" + GooseCode.Topic(b.MyCode), new StringContent(evil, Encoding.UTF8, "application/json")).Wait();
                Incoming foreign = Take(b, x => x.Kind == IncomingKind.UnsupportedFile && x.FileName == "x.png", 5);
                check("an attachment on another host is not downloaded", foreign != null && foreign.ImageBytes == null && foreign.AttachmentUrl == null, null);

                // network hiccup: every stream is cut, a note is sent meanwhile, nothing may be lost
                http.PostAsync(server + "/_drop", new StringContent("")).Wait();
                check("the cut is noticed", WaitFor(() => !b.Connected, 5), null);
                check("A sends while B is disconnected", Send(done => a.SendNote(b.MyCode, "пока тебя не было", done)), null);
                Incoming late = Take(b, x => x.Kind == IncomingKind.Note && x.Text == "пока тебя не было", 15);
                check("B reconnects and still gets it", late != null && b.Connected, null);
                check("nothing arrives twice after the reconnect", WaitFor(() => false, 1.5) || b.Inbox.Count == 0, "inbox " + b.Inbox.Count);
            }

            FriendService dead = new FriendService("http://127.0.0.1:9", Path.Combine(dir, "C.ini")) { RunOnUi = act => act() };
            check("sending to an unreachable server reports failure", !Send(done => dead.SendNote(b.MyCode, "x", done), 30), null);
            a.Stop();
            b.Stop();
        }
    }
}
