#!/usr/bin/env python3
"""A small stand-in for ntfy (https://ntfy.sh) used by the headless tests.

Implements the parts GooseDeluxe relies on, following ntfy's documented behaviour:
  POST/PUT /<topic>            publish; title/tags/message/filename as query params
                               (a PUT with ?filename=, a non-UTF-8 body or >4 KB becomes an attachment)
  GET  /<topic>/json           newline-delimited JSON stream, chunked: "open" event, then messages,
                               "keepalive" events; ?since=<id>|<unix time> replays cached messages
  GET  /file/<name>            attachment download
Test-only helpers:
  POST /_drop                  abruptly closes every open stream (simulates a network hiccup)
  POST /_inject/<topic>        publishes the JSON body as-is (to fake odd attachments)

Usage: mock_ntfy.py <port-file>   (binds to a free port on 127.0.0.1 and writes it to <port-file>)
"""
import http.server
import json
import os
import secrets
import socket
import sys
import threading
import time
import urllib.parse

cond = threading.Condition()
messages = []          # every published message, all topics, in order
files = {}             # attachment name -> bytes
drop_generation = [0]  # bumped by /_drop; streams opened before the bump close themselves
port = [0]


def new_id():
    return secrets.token_hex(6)


def sniff(body):
    if body.startswith(b"\x89PNG\r\n\x1a\n"):
        return "image/png"
    if body.startswith(b"GIF8"):
        return "image/gif"
    if body.startswith(b"\xff\xd8"):
        return "image/jpeg"
    if body.startswith(b"RIFF") and body[8:12] == b"WEBP":
        return "image/webp"
    return "application/octet-stream"


class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *args):
        pass

    def _param(self, query, *names):
        for n in names:
            if n in query:
                return query[n][0]
            h = self.headers.get("X-" + n) or self.headers.get(n)
            if h:
                return h
        return None

    def _reply_json(self, obj, code=200):
        data = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_PUT(self):
        self._publish()

    def do_POST(self):
        self._publish()

    def _publish(self):
        url = urllib.parse.urlparse(self.path)
        query = urllib.parse.parse_qs(url.query)
        parts = [p for p in url.path.split("/") if p]
        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length) if length else b""

        if parts == ["_drop"]:
            with cond:
                drop_generation[0] += 1
                cond.notify_all()
            return self._reply_json({"dropped": True})
        if len(parts) == 2 and parts[0] == "_inject":
            msg = json.loads(body.decode("utf-8"))
            msg.setdefault("id", new_id())
            msg.setdefault("time", int(time.time()))
            msg.setdefault("event", "message")
            msg["topic"] = parts[1]
            with cond:
                messages.append(msg)
                cond.notify_all()
            return self._reply_json(msg)
        if len(parts) != 1:
            return self._reply_json({"error": "bad path"}, 404)

        topic = parts[0]
        msg = {"id": new_id(), "time": int(time.time()), "event": "message", "topic": topic}
        title = self._param(query, "title", "t")
        tags = self._param(query, "tags", "ta", "tag")
        filename = self._param(query, "filename", "file", "f")
        caption = self._param(query, "message", "m")
        if title:
            msg["title"] = title
        if tags:
            msg["tags"] = [t for t in tags.split(",") if t]
        try:
            text = body.decode("utf-8")
            is_text = True
        except UnicodeDecodeError:
            text, is_text = None, False
        if filename is not None or not is_text or len(body) > 4096:
            name = filename or "attachment"
            ext = os.path.splitext(name)[1] or ".bin"
            fid = new_id() + ext
            files[fid] = body
            msg["attachment"] = {
                "name": name,
                "type": sniff(body),
                "size": len(body),
                "expires": int(time.time()) + 3 * 3600,
                "url": "http://127.0.0.1:%d/file/%s" % (port[0], fid),
            }
            msg["message"] = caption or ("You received a file: %s" % name)
        else:
            msg["message"] = text if text else (caption or "")
        with cond:
            messages.append(msg)
            cond.notify_all()
        self._reply_json(msg)

    def do_GET(self):
        url = urllib.parse.urlparse(self.path)
        query = urllib.parse.parse_qs(url.query)
        parts = [p for p in url.path.split("/") if p]
        if len(parts) == 2 and parts[0] == "file":
            data = files.get(parts[1])
            if data is None:
                return self._reply_json({"error": "not found"}, 404)
            self.send_response(200)
            self.send_header("Content-Type", sniff(data))
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)
            return
        if len(parts) == 2 and parts[1] == "json":
            return self._stream(parts[0], query)
        self._reply_json({"error": "not found"}, 404)

    def _chunk(self, obj):
        data = (json.dumps(obj, ensure_ascii=False) + "\n").encode("utf-8")
        self.wfile.write(b"%x\r\n" % len(data) + data + b"\r\n")
        self.wfile.flush()

    def _stream(self, topic, query):
        since = query.get("since", [None])[0]
        with cond:
            generation = drop_generation[0]
            if since is None:
                index = len(messages)
            elif since.isdigit():
                t = int(since)
                index = next((i for i, m in enumerate(messages) if m["time"] >= t), len(messages))
            else:
                ids = [m["id"] for m in messages]
                index = ids.index(since) + 1 if since in ids else 0
        self.send_response(200)
        self.send_header("Content-Type", "application/x-ndjson; charset=utf-8")
        self.send_header("Transfer-Encoding", "chunked")
        self.end_headers()
        try:
            self._chunk({"id": new_id(), "time": int(time.time()), "event": "open", "topic": topic})
            last_keepalive = time.time()
            while True:
                with cond:
                    if drop_generation[0] != generation:
                        break
                    pending = [m for m in messages[index:] if m["topic"] == topic]
                    index = len(messages)
                    if not pending:
                        cond.wait(timeout=0.5)
                for m in pending:
                    self._chunk(m)
                if time.time() - last_keepalive > 5:
                    self._chunk({"id": new_id(), "time": int(time.time()), "event": "keepalive", "topic": topic})
                    last_keepalive = time.time()
        except (BrokenPipeError, ConnectionResetError):
            return
        # abrupt close, no terminating chunk: the client sees a broken stream
        try:
            self.connection.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass
        self.close_connection = True


def main():
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    server.daemon_threads = True
    port[0] = server.server_address[1]
    with open(sys.argv[1], "w") as f:
        f.write(str(port[0]))
    server.serve_forever()


if __name__ == "__main__":
    main()
