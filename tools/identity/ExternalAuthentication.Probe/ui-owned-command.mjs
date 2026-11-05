import { spawn } from "node:child_process";

// POSIX qualification processes only. Never signal a port owner, shared server or unowned process group.
export async function runOwnedCommand(executable, args, { cwd, log, timeoutMs, env = process.env, signal, graceMs = 5000 } = {}) {
  if (process.platform === "win32" || !Number.isFinite(timeoutMs) || timeoutMs <= 0 || !Number.isFinite(graceMs) || graceMs < 0 || graceMs > 60_000)
    throw new Error("Owned UI commands require POSIX process groups and a positive deadline.");
  if (signal?.aborted) throw new Error("Owned UI command cancelled before launch.");
  const child = spawn(executable, args, { cwd, env, detached: true, stdio: ["ignore", log ?? "pipe", log ?? "pipe"] });
  let reason = null; let escalation; let hardStop; let rejectStopped; let output = ""; let outputBytes = 0; let cleanupError;
  const stopped = new Promise((_, reject) => { rejectStopped = reject; });
  function groupSignal(value) {
    if (!child.pid) return;
    try { process.kill(-child.pid, value); } catch (error) { if (error.code !== "ESRCH") cleanupError = error; }
  }
  function stop(value) {
    reason ??= value; groupSignal("SIGTERM");
    escalation ??= setTimeout(() => groupSignal("SIGKILL"), graceMs);
    hardStop ??= setTimeout(() => rejectStopped(new Error("Owned UI leader did not exit after bounded termination.")), graceMs + 5000);
  }
  const interrupted = value => stop(value);
  const onTerm = () => interrupted("SIGTERM"); const onInt = () => interrupted("SIGINT"); const onAbort = () => interrupted("cancelled");
  process.on("SIGTERM", onTerm); process.on("SIGINT", onInt); signal?.addEventListener("abort", onAbort, { once: true });
  const timer = setTimeout(() => stop("deadline"), timeoutMs);
  const collect = bytes => {
    outputBytes += bytes.length;
    if (outputBytes > 65_536) stop("metadata-output-bound"); else output += bytes.toString("utf8");
  };
  child.stdout?.on("data", collect); child.stderr?.on("data", collect);
  let result;
  try {
    result = await Promise.race([stopped, new Promise((done, reject) => {
      child.once("error", reject); child.once("close", (exitCode, exitSignal) => done({ exitCode, exitSignal }));
    })]);
  } finally {
    clearTimeout(timer); clearTimeout(escalation); clearTimeout(hardStop);
    groupSignal("SIGTERM");
    // The leader can exit while a compiler/worker remains. Drain the group in all outcomes.
    const grace = Date.now() + graceMs;
    const alive = () => {
      if (!child.pid) return false;
      try { process.kill(-child.pid, 0); return true; } catch (error) { if (error.code === "ESRCH") return false; cleanupError = error; return true; }
    };
    while (alive() && Date.now() < grace) await new Promise(done => setTimeout(done, 100));
    if (alive()) groupSignal("SIGKILL");
    const reaped = Date.now() + 5000;
    while (alive() && Date.now() < reaped) await new Promise(done => setTimeout(done, 100));
    const remains = alive();
    clearTimeout(timer); clearTimeout(escalation); clearTimeout(hardStop);
    process.off("SIGTERM", onTerm); process.off("SIGINT", onInt); signal?.removeEventListener("abort", onAbort);
    if (cleanupError || remains) throw new Error("Owned UI process-group cleanup could not be confirmed; retain the allocation.");
  }
  if (reason || result.exitCode !== 0) throw new Error(`Owned UI command failed (${reason ?? result.exitSignal ?? result.exitCode}); no successful receipt emitted.`);
  return { ...result, output, processGroup: child.pid, cleanup: "owned-group-absent", timeoutMs };
}
