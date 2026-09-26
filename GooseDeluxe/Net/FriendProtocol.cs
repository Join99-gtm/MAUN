using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GooseDeluxe
{
    internal enum IncomingKind { Command, Note, Image, UnsupportedFile }

    internal enum GooseCommand { None, Honk, Meme, Note, Steal, Mud, Come, Phrase, Say }

    /// <summary>How a visiting goose looks: its owner's colours, hat and name.</summary>
    internal sealed class GuestLook
    {
        public string Name = "";
        public string White = "#ffffff", Orange = "#ffa500", Outline = "#d3d3d3";
        public HatStyle Hat = HatStyle.None;
    }

    internal sealed class Incoming
    {
        public string Id;
        public IncomingKind Kind;
        public GooseCommand Command;
        public string Text;
        public string FromName;
        /// <summary>Sender's goose code (set only when a goose sent it, so a reply can find its way back).</summary>
        public string FromCode;
        /// <summary>Non-null when another GooseDeluxe goose sent this: it will show up in person.</summary>
        public GuestLook Guest;
        public string AttachmentUrl;
        public string FileName;
        public byte[] ImageBytes;
    }

    /// <summary>
    /// What travels over ntfy. The body is plain text: a command word ("га", "мем", "кража"…) or a note.
    /// Goose-to-goose messages add tags: gd, from-&lt;code&gt;, c-&lt;white&gt;-&lt;orange&gt;-&lt;outline&gt;, hat-&lt;style&gt;
    /// and put the sender's name in the title. Plain messages (e.g. typed on a phone in the ntfy app)
    /// have no tags and are carried out by the receiver's own goose.
    /// </summary>
    internal static class FriendProtocol
    {
        public const int MaxTextLength = 500;
        public const int MaxSayLength = 300;
        public const int MaxImageBytes = 8 * 1024 * 1024;
        private const string DefaultFileMessagePrefix = "You received a file";

        private static readonly Dictionary<string, GooseCommand> words = new Dictionary<string, GooseCommand>
        {
            { "гудок", GooseCommand.Honk }, { "гудни", GooseCommand.Honk }, { "honk", GooseCommand.Honk }, { "хонк", GooseCommand.Honk },
            { "мем", GooseCommand.Meme }, { "мемас", GooseCommand.Meme }, { "meme", GooseCommand.Meme },
            { "записка", GooseCommand.Note }, { "записку", GooseCommand.Note }, { "блокнот", GooseCommand.Note }, { "note", GooseCommand.Note },
            { "кража", GooseCommand.Steal }, { "укради", GooseCommand.Steal }, { "мышь", GooseCommand.Steal }, { "курсор", GooseCommand.Steal }, { "steal", GooseCommand.Steal },
            { "грязь", GooseCommand.Mud }, { "следы", GooseCommand.Mud }, { "mud", GooseCommand.Mud },
            { "сюда", GooseCommand.Come }, { "ко мне", GooseCommand.Come }, { "иди сюда", GooseCommand.Come }, { "come", GooseCommand.Come },
            { "фраза", GooseCommand.Phrase }, { "фразу", GooseCommand.Phrase }, { "скажи", GooseCommand.Phrase }, { "скажи что-нибудь", GooseCommand.Phrase },
            { "говори", GooseCommand.Phrase }, { "phrase", GooseCommand.Phrase },
        };

        private static readonly Regex honkRx = new Regex("^(га[- ]?)+$|^(honk[- ]?)+$", RegexOptions.CultureInvariant);

        public static GooseCommand ParseCommand(string text)
        {
            string s = NormalizeWord(text);
            if (s.Length == 0 || s.Length > 20) return GooseCommand.None;
            if (honkRx.IsMatch(s)) return GooseCommand.Honk;
            GooseCommand c;
            return words.TryGetValue(s, out c) ? c : GooseCommand.None;
        }

        /// <summary>The word to send for a command (the receiver parses it back with <see cref="ParseCommand"/>).</summary>
        public static string CommandWord(GooseCommand c)
        {
            switch (c)
            {
                case GooseCommand.Honk: return "га";
                case GooseCommand.Meme: return "мем";
                case GooseCommand.Note: return "записка";
                case GooseCommand.Steal: return "кража";
                case GooseCommand.Mud: return "грязь";
                case GooseCommand.Come: return "сюда";
                case GooseCommand.Phrase: return "фраза";
                default: return "";
            }
        }

        private static readonly Regex sayRx = new Regex("^\\s*(скажи|say)(\\s*:\\s*|\\s+)(\\S.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

        /// <summary>«скажи Где акты?!» — what the goose should say, or null if it's not that kind of message.</summary>
        public static string ParseSay(string text)
        {
            if (text == null) return null;
            Match m = sayRx.Match(text);
            if (!m.Success) return null;
            string what = Regex.Replace(m.Groups[3].Value, "\\s+", " ").Trim(); // one line for the bubble
            if (what.Length == 0 || what.Equals("что-нибудь", StringComparison.OrdinalIgnoreCase)) return null;
            return what.Length > MaxSayLength ? what.Substring(0, MaxSayLength) : what;
        }

        private static string NormalizeWord(string text)
        {
            if (text == null) return "";
            string s = text.Trim().ToLowerInvariant().Replace('ё', 'е');
            s = s.Trim(' ', '!', '?', '.', ',', '…', ';', ':', '(', ')', '"', '\'', '«', '»', '\r', '\n', '\t');
            return Regex.Replace(s, "\\s+", " ");
        }

        public static string Tags(string myCode, string[] colors, HatStyle hat)
        {
            StringBuilder sb = new StringBuilder("gd,from-").Append(myCode.ToLowerInvariant());
            if (colors != null && colors.Length == 3)
            {
                string c0 = Hex(colors[0]), c1 = Hex(colors[1]), c2 = Hex(colors[2]);
                if (c0 != null && c1 != null && c2 != null) sb.Append(",c-").Append(c0).Append('-').Append(c1).Append('-').Append(c2);
            }
            sb.Append(",hat-").Append(hat.ToString().ToLowerInvariant());
            return sb.ToString();
        }

        /// <summary>Turns one ntfy message into something the goose can act on; null means "ignore it".</summary>
        public static Incoming Parse(NtfyMessage m, string server)
        {
            if (m == null || m.@event != "message") return null;
            Incoming x = new Incoming { Id = m.id, FromName = Clean(m.title, 40) };

            bool fromGoose = false;
            GuestLook look = new GuestLook();
            if (m.tags != null)
            {
                foreach (string raw in m.tags)
                {
                    if (string.IsNullOrEmpty(raw)) continue;
                    string tag = raw.Trim().ToLowerInvariant();
                    if (tag == "gd") fromGoose = true;
                    else if (tag.StartsWith("from-")) x.FromCode = GooseCode.Normalize(tag.Substring(5));
                    else if (tag.StartsWith("c-"))
                    {
                        string[] parts = tag.Substring(2).Split('-');
                        if (parts.Length == 3 && Hex(parts[0]) != null && Hex(parts[1]) != null && Hex(parts[2]) != null)
                        {
                            look.White = "#" + Hex(parts[0]);
                            look.Orange = "#" + Hex(parts[1]);
                            look.Outline = "#" + Hex(parts[2]);
                        }
                    }
                    else if (tag.StartsWith("hat-"))
                    {
                        try { look.Hat = (HatStyle)Enum.Parse(typeof(HatStyle), tag.Substring(4), true); } catch { }
                    }
                }
            }
            if (fromGoose && x.FromCode != null)
            {
                look.Name = x.FromName ?? "";
                x.Guest = look;
            }
            else
            {
                x.FromCode = null; // only a goose can be answered by goose mail
            }

            if (m.attachment != null && !string.IsNullOrEmpty(m.attachment.url))
            {
                x.FileName = Clean(m.attachment.name, 80) ?? "файл";
                string caption = Clean(m.message, MaxTextLength);
                if (caption != null && caption.StartsWith(DefaultFileMessagePrefix, StringComparison.Ordinal)) caption = null;
                x.Text = caption;
                if (IsSupportedImage(m.attachment.type) && m.attachment.size <= MaxImageBytes && IsOnServer(m.attachment.url, server))
                {
                    x.Kind = IncomingKind.Image;
                    x.AttachmentUrl = m.attachment.url;
                }
                else
                {
                    x.Kind = IncomingKind.UnsupportedFile;
                }
                return x;
            }

            string text = Clean(m.message, MaxTextLength);
            if (string.IsNullOrEmpty(text)) return null;
            GooseCommand cmd = ParseCommand(text);
            string say = cmd == GooseCommand.None && !fromGoose ? ParseSay(text) : null; // a friend's goose brings notes as notes
            if (say != null)
            {
                x.Kind = IncomingKind.Command;
                x.Command = GooseCommand.Say;
                x.Text = say;
                return x;
            }
            if (cmd != GooseCommand.None)
            {
                x.Kind = IncomingKind.Command;
                x.Command = cmd;
                return x;
            }
            x.Kind = IncomingKind.Note;
            x.Text = text;
            return x;
        }

        /// <summary>The goose (GDI+) can draw these; WebP and friends it can't.</summary>
        public static bool IsSupportedImage(string mime)
        {
            if (string.IsNullOrEmpty(mime)) return false;
            string t = mime.ToLowerInvariant();
            return t == "image/png" || t == "image/jpeg" || t == "image/jpg" || t == "image/gif" || t == "image/bmp";
        }

        /// <summary>Attachments are only fetched from the ntfy server itself, never from arbitrary links.</summary>
        public static bool IsOnServer(string url, string server)
        {
            Uri a, s;
            if (!Uri.TryCreate(url, UriKind.Absolute, out a) || !Uri.TryCreate(server, UriKind.Absolute, out s)) return false;
            return (a.Scheme == Uri.UriSchemeHttps || a.Scheme == Uri.UriSchemeHttp)
                && a.Scheme == s.Scheme
                && string.Equals(a.Host, s.Host, StringComparison.OrdinalIgnoreCase)
                && a.Port == s.Port
                && a.AbsolutePath.StartsWith("/file/", StringComparison.Ordinal);
        }

        private static string Clean(string s, int max)
        {
            if (s == null) return null;
            StringBuilder sb = new StringBuilder(Math.Min(s.Length, max));
            foreach (char ch in s)
            {
                if (sb.Length >= max) break;
                if (ch == '\n' || ch == '\t' || !char.IsControl(ch)) sb.Append(ch);
            }
            string r = sb.ToString().Trim();
            return r.Length == 0 ? null : r;
        }

        private static string Hex(string color)
        {
            if (color == null) return null;
            string s = color.Trim().TrimStart('#').ToLowerInvariant();
            if (s.Length != 6) return null;
            foreach (char ch in s) if (!Uri.IsHexDigit(ch)) return null;
            return s;
        }

        /// <summary>"#rrggbb" to a colour; fallback when malformed.</summary>
        public static System.Drawing.Color ToColor(string hex, System.Drawing.Color fallback)
        {
            string h = Hex(hex);
            if (h == null) return fallback;
            return System.Drawing.Color.FromArgb(255,
                int.Parse(h.Substring(0, 2), NumberStyles.HexNumber),
                int.Parse(h.Substring(2, 2), NumberStyles.HexNumber),
                int.Parse(h.Substring(4, 2), NumberStyles.HexNumber));
        }
    }
}
