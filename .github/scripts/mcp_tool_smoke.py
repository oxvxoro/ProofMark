#!/usr/bin/env python3
"""Smoke-test installed proof-mcp / distill-mcp / codemap mcp tool packages over stdio JSON-RPC.

Usage:
  mcp_tool_smoke.py proof <proof-mcp executable> <workspace>
  mcp_tool_smoke.py distill <distill-mcp executable> <workspace>
  mcp_tool_smoke.py codemap <codemap executable> <workspace>

The proof check proves the packaged server spawns the proof CLI beside itself
(proof_plan runs `dotnet exec proof.dll`), rejects a root outside the pinned
workspace, and keeps proof_verify off the read-only surface. The workspace
must already carry a CodeMap index. A successful tool call returns plain text;
a failing one ends with an `exit: N` line.
"""

import json
import os
import queue
import subprocess
import sys
import threading


class McpClient:
    def __init__(self, argv, cwd):
        self._process = subprocess.Popen(
            argv,
            cwd=cwd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
        )
        self._lines = queue.Queue()
        self._stderr = []
        threading.Thread(target=self._pump, args=(self._process.stdout, self._lines), daemon=True).start()
        threading.Thread(target=self._drain, daemon=True).start()
        self._next_id = 1

    @staticmethod
    def _pump(stream, sink):
        for line in stream:
            sink.put(line)
        sink.put(None)

    def _drain(self):
        for line in self._process.stderr:
            self._stderr.append(line)

    def _send(self, message):
        self._process.stdin.write(json.dumps(message) + "\n")
        self._process.stdin.flush()

    def request(self, method, params, timeout=120):
        request_id = self._next_id
        self._next_id += 1
        self._send({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params})
        while True:
            try:
                line = self._lines.get(timeout=timeout)
            except queue.Empty:
                raise SystemExit(f"timeout waiting for {method}; stderr:\n{''.join(self._stderr)}")
            if line is None:
                raise SystemExit(f"server exited during {method}; stderr:\n{''.join(self._stderr)}")
            try:
                message = json.loads(line)
            except json.JSONDecodeError:
                continue
            if message.get("id") == request_id:
                if "error" in message:
                    raise SystemExit(f"{method} error: {message['error']}")
                return message["result"]

    def initialize(self):
        self.request("initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "mcp-tool-smoke", "version": "1.0"},
        })
        self._send({"jsonrpc": "2.0", "method": "notifications/initialized"})

    def tool_names(self):
        return [tool["name"] for tool in self.request("tools/list", {})["tools"]]

    def call_text(self, name, arguments, timeout=300):
        result = self.request("tools/call", {"name": name, "arguments": arguments}, timeout)
        return "".join(item.get("text", "") for item in result.get("content", []))

    def close(self):
        try:
            self._process.stdin.close()
            self._process.wait(timeout=10)
        except Exception:
            self._process.kill()


def exit_code(text):
    lines = text.rstrip().splitlines()
    if lines and lines[-1].startswith("exit: "):
        return int(lines[-1][len("exit: "):])
    return 0


def require(condition, message):
    if not condition:
        raise SystemExit(message)


def outside_directory(workspace):
    return os.path.dirname(os.path.abspath(workspace).rstrip(os.sep))


def smoke_proof(executable, workspace):
    client = McpClient([executable, "--root", workspace], workspace)
    try:
        client.initialize()
        names = client.tool_names()
        print("proof-mcp tools:", ", ".join(names))
        require("proof_config_validate" in names, "proof_config_validate missing")
        require("proof_verify" not in names, "proof_verify exposed without --allow-exec")

        text = client.call_text("proof_config_validate", {"root": workspace})
        print(text)
        require(exit_code(text) == 0, "proof_config_validate failed")

        plan = client.call_text("proof_plan", {"root": workspace})
        print(plan[:2000])
        require("proof CLI not found" not in plan, "proof-mcp cannot find the proof CLI beside itself")
        require(exit_code(plan) == 0, "proof_plan through the packaged proof CLI failed")

        rejected = client.call_text("proof_config_validate", {"root": outside_directory(workspace)})
        print(rejected)
        require(rejected.startswith("Rejected root"), "root outside the pinned workspace was not rejected")
    finally:
        client.close()


def smoke_distill(executable, workspace):
    client = McpClient([executable, "--root", workspace], workspace)
    try:
        client.initialize()
        names = client.tool_names()
        print("distill-mcp tools:", ", ".join(names))
        for expected in ("distill_report", "distill_diagnostics", "distill_raw", "distill_doctor"):
            require(expected in names, f"{expected} missing")
        text = client.call_text("distill_doctor", {"root": workspace}, timeout=120)
        print(text)
        require("[ok] dotnet SDK" in text, "distill_doctor did not run its checks")

        rejected = client.call_text("distill_doctor", {"root": outside_directory(workspace)}, timeout=120)
        print(rejected)
        require(rejected.startswith("Rejected root"), "root outside the pinned workspace was not rejected")
    finally:
        client.close()


def smoke_codemap(executable, workspace):
    client = McpClient([executable, "mcp", "--root", workspace], workspace)
    try:
        client.initialize()
        names = client.tool_names()
        print("codemap mcp tools:", ", ".join(names))
        require("find_symbol" in names, "find_symbol missing")
        require("get_status" in names, "get_status missing")

        status = client.call_text("get_status", {"root": workspace, "checkFreshness": False}, timeout=120)
        print(status[:500])
        require("indexState" in status or "error" in status, "get_status returned unexpected payload")

        rejected = client.call_text("get_status", {"root": outside_directory(workspace)}, timeout=120)
        print(rejected)
        require(rejected.startswith("Rejected root"), "root outside the pinned workspace was not rejected")
    finally:
        client.close()


def main():
    if len(sys.argv) != 4 or sys.argv[1] not in ("proof", "distill", "codemap"):
        raise SystemExit(__doc__)
    kind, executable, workspace = sys.argv[1], os.path.abspath(sys.argv[2]), os.path.abspath(sys.argv[3])
    if kind == "proof":
        smoke_proof(executable, workspace)
    elif kind == "distill":
        smoke_distill(executable, workspace)
    else:
        smoke_codemap(executable, workspace)
    print(f"{kind} mcp smoke passed")


if __name__ == "__main__":
    main()
