import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, existsSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { runOwnedCommand } from "./ui-owned-command.mjs";

// Tiny owned idle children exercise cleanup without dotnet, servers, browsers or product tests.
const fixture = String.raw`
const { spawn } = require("node:child_process"); const { writeFileSync } = require("node:fs");
const ignore = process.argv[2] === "ignore";
if (ignore) process.on("SIGTERM", () => {});
const child = spawn(process.execPath, ["-e", (ignore ? 'process.on("SIGTERM", () => {});' : '') + 'setInterval(() => {}, 1000)'], { stdio: "ignore" });
writeFileSync(process.argv[1], JSON.stringify({ leader: process.pid, child: child.pid }));
setInterval(() => {}, 1000);
`;
for (const mode of ["deadline", "cancellation"]) test(`owned command ${mode} drains its child group`, { timeout: 30_000 }, async () => {
  const root = mkdtempSync(join(tmpdir(), "elsa-3579-owned-command-")); const file = join(root, "owned-pids.json");
  const controller = new AbortController(); let pids; let passed = false; let rejected;
  try {
    const command = runOwnedCommand(process.execPath, ["-e", fixture, file, mode === "deadline" ? "ignore" : "term"],
      { timeoutMs: mode === "deadline" ? 5000 : 15_000, signal: controller.signal });
    rejected = assert.rejects(command, error => error.message.includes(mode === "deadline" ? "deadline" : "cancelled"));
    const ready = Date.now() + 4000;
    while (!existsSync(file) && Date.now() < ready) await new Promise(done => setTimeout(done, 50));
    assert.equal(existsSync(file), true, "Owned fixture did not become ready; retain its root.");
    pids = JSON.parse(readFileSync(file, "utf8"));
    if (mode === "cancellation") controller.abort();
    await rejected;
    for (const pid of [pids.leader, pids.child]) assert.throws(() => process.kill(pid, 0), error => error.code === "ESRCH");
    passed = true;
  } finally {
    controller.abort();
    await rejected?.catch(() => {});
    if (!pids && existsSync(file)) pids = JSON.parse(readFileSync(file, "utf8"));
    if (pids) try { process.kill(-pids.leader, "SIGKILL"); } catch (error) { if (error.code !== "ESRCH") throw error; }
    if (passed) rmSync(root, { recursive: true });
  }
});
