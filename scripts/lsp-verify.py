#!/usr/bin/env python3
"""Quick health check for the language servers used by this project.

Usage:
  python3 scripts/lsp-verify.py "<server command>" [server args...]

Sends an LSP initialize handshake over stdio and prints the advertised
capabilities. Exit code 0 means the server responded correctly.
"""
import json, subprocess, sys, time, os

server_cmd = sys.argv[1:]
root = os.getcwd()
proc = subprocess.Popen(server_cmd, stdin=subprocess.PIPE,
                        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
buf = b""

def read_message(deadline):
    global buf
    while True:
        if time.time() > deadline:
            return None
        idx = buf.find(b"Content-Length:")
        if idx > 0:
            buf = buf[idx:]
            idx = 0
        if idx == -1:
            chunk = proc.stdout.read1(65536)
            if not chunk:
                return None
            buf += chunk
            continue
        hdr_end = buf.find(b"\r\n\r\n")
        if hdr_end == -1:
            chunk = proc.stdout.read1(65536)
            if not chunk:
                return None
            buf += chunk
            continue
        headers = dict(
            l.split(": ", 1) for l in buf[:hdr_end].decode().split("\r\n") if ": " in l
        )
        length = int(headers["Content-Length"])
        while len(buf) < hdr_end + 4 + length:
            chunk = proc.stdout.read1(hdr_end + 4 + length - len(buf))
            if not chunk:
                return None
            buf += chunk
        body = buf[hdr_end + 4 : hdr_end + 4 + length]
        buf = buf[hdr_end + 4 + length :]
        return json.loads(body)

def send(method, params=None, msg_id=None):
    msg = {"jsonrpc": "2.0", "method": method}
    if msg_id is not None:
        msg["id"] = msg_id
    if params is not None:
        msg["params"] = params
    body = json.dumps(msg).encode()
    proc.stdin.write(b"Content-Length: %d\r\n\r\n" % len(body) + body)
    proc.stdin.flush()

send("initialize", {
    "processId": os.getpid(),
    "rootUri": "file://" + root,
    "capabilities": {},
}, msg_id=1)

deadline = time.time() + int(os.environ.get("PROBE_TIMEOUT", "240"))
while time.time() < deadline:
    m = read_message(deadline)
    if m is None:
        print("TIMEOUT/EOF waiting for initialize response", flush=True)
        sys.exit(2)
    if m.get("id") == 1:
        caps = m["result"]["capabilities"]
        print("INIT-OK capabilities:", ", ".join(sorted(caps.keys())), flush=True)
        break

send("initialized", {})
time.sleep(3)
proc.terminate()
print("DONE", flush=True)
