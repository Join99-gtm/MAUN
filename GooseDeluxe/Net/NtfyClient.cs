using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace GooseDeluxe
{
    /// <summary>
    /// Minimal client for ntfy (https://ntfy.sh), a free publish/subscribe service with no accounts:
    /// whoever knows a topic name can post to it and listen to it. Subscribing is one long-lived
    /// streaming GET (JSON per line, keepalives every ~45 s); on reconnect we ask for everything
    /// "since" the last message we saw, so nothing sent while the connection was down is lost.
    /// </summary>
    internal sealed class NtfyClient
    {
        private const string UserAgent = "GooseDeluxe/0.2";

        public readonly string Server;
        public Action<string> Log = s => { };
        public int ReadTimeoutMs = 150000;   // > 2 keepalives, then we assume the connection is dead
        public int MaxBackoffMs = 60000;

        private volatile bool connected;
        private volatile bool stopped;
        private volatile HttpWebRequest current;
        private readonly Queue<string> seenOrder = new Queue<string>();
        private readonly HashSet<string> seen = new HashSet<string>();

        public bool Connected { get { return connected; } }

        static NtfyClient()
        {
            // .NET Framework apps built for 4.5 still default to SSL3/TLS 1.0, which ntfy.sh refuses
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            // one connection is permanently held by the subscription; the default limit (2) would stall uploads
            try { if (ServicePointManager.DefaultConnectionLimit < 8) ServicePointManager.DefaultConnectionLimit = 8; } catch { }
            try { ServicePointManager.Expect100Continue = false; } catch { }
        }

        public NtfyClient(string server)
        {
            Server = (server ?? "https://ntfy.sh").Trim().TrimEnd('/');
        }

        public void Subscribe(string topic, Action<NtfyMessage> onMessage)
        {
            Thread t = new Thread(() => Loop(topic, onMessage));
            t.IsBackground = true;
            t.Name = "GooseDeluxe ntfy " + topic;
            t.Start();
        }

        public void Stop()
        {
            stopped = true;
            HttpWebRequest r = current;
            if (r != null) { try { r.Abort(); } catch { } }
        }

        /// <summary>For tests: drop the current stream as if the network hiccupped.</summary>
        public void DropConnection()
        {
            HttpWebRequest r = current;
            if (r != null) { try { r.Abort(); } catch { } }
        }

        private void Loop(string topic, Action<NtfyMessage> onMessage)
        {
            string since = null;
            long firstConnectUnix = -1;
            int backoff = 2000;
            while (!stopped)
            {
                try
                {
                    if (firstConnectUnix < 0) firstConnectUnix = UnixNow();
                    string url = Server + "/" + topic + "/json" + (since != null ? "?since=" + Uri.EscapeDataString(since) : "");
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.UserAgent = UserAgent;
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = ReadTimeoutMs;
                    req.KeepAlive = true;
                    req.AllowReadStreamBuffering = false;
                    current = req;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader reader = new StreamReader(resp.GetResponseStream(), new UTF8Encoding(false)))
                    {
                        connected = true;
                        backoff = 2000;
                        string line;
                        while (!stopped && (line = reader.ReadLine()) != null)
                        {
                            NtfyMessage m = NtfyMessage.Parse(line);
                            if (m == null || m.@event != "message") continue; // open / keepalive / poll_request
                            if (!string.IsNullOrEmpty(m.id))
                            {
                                if (!Remember(m.id)) continue; // already seen before a reconnect
                                since = m.id;
                            }
                            try { onMessage(m); }
                            catch (Exception ex) { Log("ntfy handler failed: " + ex); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!stopped) Log("ntfy stream: " + ex.Message);
                }
                connected = false;
                current = null;
                if (stopped) break;
                // don't lose what arrives while we're away, even if nothing has arrived yet
                if (since == null) since = firstConnectUnix.ToString();
                for (int waited = 0; waited < backoff && !stopped; waited += 250) Thread.Sleep(250);
                backoff = Math.Min(backoff * 2, MaxBackoffMs);
            }
        }

        private bool Remember(string id)
        {
            if (seen.Contains(id)) return false;
            seen.Add(id);
            seenOrder.Enqueue(id);
            while (seenOrder.Count > 200) seen.Remove(seenOrder.Dequeue());
            return true;
        }

        public bool PublishText(string topic, string text, string title, string tags)
        {
            string url = Server + "/" + topic + Query("title", title, "tags", tags, null, null, null, null);
            return Send("POST", url, Encoding.UTF8.GetBytes(text ?? ""), "text/plain; charset=utf-8");
        }

        /// <summary>Uploads a file as an attachment (ntfy.sh keeps it for a few hours, up to 15 MB).</summary>
        public bool PublishFile(string topic, byte[] data, string fileName, string caption, string title, string tags)
        {
            string url = Server + "/" + topic + Query("filename", fileName, "message", caption, "title", title, "tags", tags);
            return Send("PUT", url, data, "application/octet-stream");
        }

        /// <summary>Downloads at most <paramref name="maxBytes"/>; null when bigger or on any error.</summary>
        public byte[] Download(string url, int maxBytes)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = UserAgent;
                req.Timeout = 20000;
                req.ReadWriteTimeout = 30000;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[16384];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        ms.Write(buf, 0, n);
                        if (ms.Length > maxBytes) return null;
                    }
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Log("ntfy download " + url + ": " + ex.Message);
                return null;
            }
        }

        private bool Send(string method, string url, byte[] body, string contentType)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                req.UserAgent = UserAgent;
                req.ContentType = contentType;
                req.Timeout = 20000;
                req.ReadWriteTimeout = 60000;
                req.ContentLength = body.Length;
                using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    return (int)resp.StatusCode / 100 == 2;
            }
            catch (Exception ex)
            {
                Log("ntfy " + method + " failed: " + ex.Message);
                return false;
            }
        }

        private static string Query(params string[] pairs)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                if (string.IsNullOrEmpty(pairs[i]) || string.IsNullOrEmpty(pairs[i + 1])) continue;
                sb.Append(sb.Length == 0 ? '?' : '&').Append(pairs[i]).Append('=').Append(Uri.EscapeDataString(pairs[i + 1]));
            }
            return sb.ToString();
        }

        private static long UnixNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
