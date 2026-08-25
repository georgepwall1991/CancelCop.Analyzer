#!/usr/bin/env python3
"""Quick health check for the language servers used by this project.

Usage:
  python3 scripts/lsp-verify.py "<server command>" [server args...]

Sends an LSP initialize handshake over stdio and prints the advertised
capabilities. Exit code 0 means the server responded correctly.
"""

import json
import os
import select
import subprocess
import sys
import time


def read_message(proc, buf, deadline):
    """Read one LSP frame from proc.stdout, honoring the deadline even when
    the server is silent. Returns (message, buffer)."""
    while True:
        remaining = deadline - time.time()
        if remaining <= 0:
            return None, buf

        hdr_end = buf.find(b"\r\n\r\n")
        if hdr_end != -1:
            headers = {}
            for line in buf[:hdr_end].decode(errors="replace").split("\r\n"):
                if ": " in line:
                    k, v = line.split(": ", 1)
                    headers[k.lower()] = v.strip()
            length = int(headers["content-length"])
            body_start = hdr_end + 4
            needed = body_start + length
            if len(buf) >= needed:
                message = json.loads(buf[body_start:needed])
                return message, buf[needed:]

        rlist, _, _ = select.select([proc.stdout], [], [], min(remaining, 0.5))
        if not rlist:
            continue
        chunk = proc.stdout.read1(65536)
        if not chunk and proc.poll() is not None:
            return None, buf
        buf += chunk


def main():
    import shlex

    server_cmd = shlex.split(" ".join(sys.argv[1:]))
    root = os.getcwd()

    proc = subprocess.Popen(
        server_cmd,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
    )
    buf = b""

    def send(method, params=None, msg_id=None):
        message = {"jsonrpc": "2.0", "method": method}
        if msg_id is not None:
            message["id"] = msg_id
        if params is not None:
            message["params"] = params
        body = json.dumps(message).encode()
        proc.stdin.write(b"Content-Length: %d\r\n\r\n" % len(body) + body)
        proc.stdin.flush()

    send(
        "initialize",
        {
            "processId": os.getpid(),
            "rootUri": "file://" + root,
            "capabilities": {},
        },
        msg_id=1,
    )

    deadline = time.time() + float(os.environ.get("LSP_VERIFY_TIMEOUT", "60"))
    while True:
        message, buf = read_message(proc, buf, deadline)
        if message is None:
            print("FAIL: no initialize response within timeout")
            return 2

        if message.get("id") == 1:
            capabilities = sorted(message["result"]["capabilities"].keys())
            print("INIT-OK capabilities:", ", ".join(capabilities))
            break

    send("initialized", {})
    time.sleep(0.2)
    proc.terminate()
    return 0


if __name__ == "__main__":
    sys.exit(main())
