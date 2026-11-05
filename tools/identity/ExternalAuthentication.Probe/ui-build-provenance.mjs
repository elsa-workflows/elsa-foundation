import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { closeSync, existsSync, lstatSync, mkdirSync, openSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { isAbsolute, join, relative, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { runOwnedCommand } from "./ui-owned-command.mjs";
import { absoluteToolPath, assertToolchain, captureToolchain, isExecutableIdentity } from "./ui-toolchain.mjs";

const repo = resolve(import.meta.dirname, "../../..");
const studioBin = "Studio/src/apps/Elsa.Studio.Web/bin/Debug/net10.0";
const probeProject = "tools/identity/ExternalAuthentication.Probe/ExternalAuthentication.Probe.csproj";
const moduleRoot = "Extensions/Identity/studio/Elsa.Studio.Identity.ExternalAuthentication/wwwroot";
const marker = "elsa-external-auth-ui-build/v1";
const digest = value => createHash("sha256").update(value).digest("hex");
function phaseExecutable(phase, tools) {
  if (phase === "controls") return tools.node;
  if (phase === "probe") return tools.dotnet.launcher;
  if (phase === "studio") return tools.python;
  throw new Error("Unknown owned qualification phase.");
}
function commandTimeoutCap(phase, index) {
  return (phase === "controls" ? 2 : phase === "probe" ? index === 0 ? 10 : 12 : 30) * 60_000;
}
function commandRecipe(phase, tools, standardOutput) {
  const executable = phaseExecutable(phase, tools);
  if (!isExecutableIdentity(executable) || (phase !== "studio" && standardOutput !== null)) throw new Error("Invalid phase executable or output binding.");
  if (phase === "controls") return [[executable.path, "--test", "--test-concurrency=1",
    "tools/identity/ExternalAuthentication.Probe/ui-evidence.test.mjs", "tools/identity/ExternalAuthentication.Probe/ui-build-provenance.test.mjs",
    "tools/identity/ExternalAuthentication.Probe/ui-owned-command.test.mjs"]];
  if (phase === "probe") return [
    [executable.path, "restore", probeProject, "--locked-mode", "--disable-parallel", "--disable-build-servers", "-m:2", "-p:BuildInParallel=false", "-nodeReuse:false"],
    [executable.path, "build", probeProject, "--no-restore", "--disable-build-servers", "-m:2", "-p:BuildInParallel=false", "-nodeReuse:false", "-p:UseSharedCompilation=false", "-p:WarningsNotAsErrors=NU1603"]];
  if (!absoluteToolPath(standardOutput) || !relative(repo, standardOutput).startsWith("..")) throw new Error("Invalid standard producer output binding.");
  return [[executable.path, "tools/ux-acceptance/run.py", "--output", standardOutput]];
}
function assertCommandExecutions(receipt, phase, tools) {
  const validCommand = command => Array.isArray(command) && command.length > 0 && command.every(argument =>
    typeof argument === "string" && argument.length > 0 && argument.length <= 8192 && !/[\0\r\n]/u.test(argument));
  const expected = commandRecipe(phase, tools, receipt.standardOutput);
  if (receipt.phase !== phase || !Array.isArray(receipt.commands) || !receipt.commands.every(validCommand) ||
      JSON.stringify(receipt.commands) !== JSON.stringify(expected) || !Array.isArray(receipt.executions) || receipt.executions.length !== expected.length ||
      receipt.executions.some((execution, index) => !execution || typeof execution !== "object" || Array.isArray(execution) || execution.exitCode !== 0 ||
        execution.cleanup !== "owned-group-absent" || !Number.isSafeInteger(execution.processGroup) || execution.processGroup <= 0 ||
        !Number.isSafeInteger(execution.timeoutMs) || execution.timeoutMs <= 0 || execution.timeoutMs > commandTimeoutCap(phase, index) ||
        !validCommand(execution.argv) || JSON.stringify(execution.argv) !== JSON.stringify(expected[index]) || !isExecutableIdentity(execution.executable) ||
        JSON.stringify(execution.executable) !== JSON.stringify(phaseExecutable(phase, tools))))
    throw new Error("Qualification command/executable records do not match the expected phase and frozen tool.");
}
export function sourceIdentity() {
  const git = (...args) => execFileSync("git", args, { cwd: repo, encoding: "utf8", timeout: 10_000 }).trim();
  if (git("status", "--porcelain")) throw new Error("UI build provenance requires clean committed source.");
  return { sourceSha: git("rev-parse", "HEAD"), tree: git("rev-parse", "HEAD^{tree}") };
}
function snapshot(root, label, include = () => true) {
  const entries = [];
  function walk(directory) {
    for (const name of readdirSync(directory).sort()) {
      const file = join(directory, name); const stat = lstatSync(file);
      if (stat.isSymbolicLink()) throw new Error("UI build output symlinks require explicit review.");
      if (stat.isDirectory()) walk(file);
      else if (stat.isFile() && include(name)) entries.push({ file: `${label}/${relative(root, file).split("\\").join("/")}`, sha256: digest(readFileSync(file)) });
    }
  }
  walk(root); return entries;
}
function outputs(phase) {
  const bin = resolve(repo, phase === "studio" ? studioBin : "tools/identity/ExternalAuthentication.Probe/bin/Debug/net10.0");
  const assembly = phase === "studio" ? "Elsa.Studio.Web" : "ExternalAuthentication.Probe";
  for (const suffix of [".dll", ".deps.json", ".runtimeconfig.json"]) if (!existsSync(join(bin, assembly + suffix)))
    throw new Error("Required UI build output is missing.");
  const files = snapshot(bin, `${phase}/bin`, name => /(?:\.dll|\.so(?:\.\d+)*|\.dylib|\.deps\.json|\.runtimeconfig\.json|\.staticwebassets\.runtime\.json)$/u.test(name));
  if (phase === "studio") {
    const runtime = JSON.parse(readFileSync(join(bin, assembly + ".staticwebassets.runtime.json"), "utf8"));
    if (!Array.isArray(runtime.ContentRoots) || !runtime.ContentRoots.length) throw new Error("Studio static asset roots are missing.");
    runtime.ContentRoots.forEach((root, index) => files.push(...snapshot(root, `studio/static-${index}`)));
    const moduleFiles = snapshot(resolve(repo, moduleRoot), "external-module");
    if (!moduleFiles.some(file => file.file === "external-module/studio/modules/external-authentication/module.js"))
      throw new Error("Built external sign-in module is missing.");
    files.push(...moduleFiles);
  }
  return files.sort((left, right) => left.file.localeCompare(right.file, "en"));
}
export function assertBuildReceipt(receipt, phase, identity, actualOutputs, toolchain) {
  assertToolchain(toolchain, receipt?.toolchain);
  if (!["studio", "probe"].includes(phase) || receipt?.kind !== "successful-owned-ui-build" || receipt.phase !== phase || receipt.exitCode !== 0 ||
      receipt.sourceSha !== identity.sourceSha || receipt.tree !== identity.tree ||
      !Array.isArray(receipt.outputs) || !receipt.outputs.length || JSON.stringify(receipt.outputs) !== JSON.stringify(actualOutputs))
    throw new Error("UI build receipt does not match current source and actual outputs.");
  assertCommandExecutions(receipt, phase, toolchain);
}
export async function verifyUiBuild(root = process.env.ELSA_EXTERNAL_AUTH_UI_BUILD_ROOT) {
  if (!root || readFileSync(join(root, ".owned-ui-build"), "utf8") !== marker) throw new Error("An owned successful UI build receipt is required.");
  const identity = sourceIdentity(); const toolchain = JSON.parse(readFileSync(join(root, "toolchain-pin.json"), "utf8")).toolchain;
  assertToolchain(toolchain, await captureToolchain());
  const proof = { ...identity, toolchain, kind: "verified-owned-ui-build", builds: [] };
  for (const phase of ["studio", "probe"]) {
    const receipt = JSON.parse(readFileSync(join(root, `${phase}-build.json`), "utf8"));
    assertBuildReceipt(receipt, phase, identity, outputs(phase), toolchain); proof.builds.push(receipt);
  }
  return proof;
}
export async function verifyServedModule(studioUrl, proof) {
  const studio = proof.builds.find(build => build.phase === "studio"); const served = [];
  for (const file of studio.outputs.filter(file => file.file.startsWith("external-module/") && /\.(?:js|css)$/u.test(file.file))) {
    const path = "/_content/Elsa.Studio.Identity.ExternalAuthentication/" + file.file.slice("external-module/".length);
    const response = await fetch(new URL(path, studioUrl), { redirect: "error", signal: AbortSignal.timeout(10_000) });
    if (!response.ok || digest(Buffer.from(await response.arrayBuffer())) !== file.sha256)
      throw new Error("Served external sign-in module differs from the successful build output.");
    served.push({ path, sha256: file.sha256 });
  }
  if (!served.some(file => file.path.endsWith("/module.js"))) throw new Error("No built module entry was verified from Studio.");
  return served;
}

async function recordBuild(phase, root, standardOutput, toolchainPath) {
  // Queue time is part of the deadline. This is an operational ceiling, not a timing test or retry policy.
  const deadline = Date.now() + (phase === "controls" ? 2 : phase === "probe" ? 20 : 30) * 60_000;
  if (!["studio", "probe", "controls"].includes(phase) || !root || !isAbsolute(root) || !relative(repo, root).startsWith(".."))
    throw new Error("Select studio, probe or controls and an absolute owned root outside the checkout.");
  if (phase === "studio" && standardOutput && (!isAbsolute(standardOutput) || !relative(repo, standardOutput).startsWith("..") || existsSync(standardOutput)))
    throw new Error("Standard composed evidence requires a new absolute output outside the checkout.");
  const identity = sourceIdentity();
  if (!existsSync(root)) { mkdirSync(root, { mode: 0o700 }); writeFileSync(join(root, ".owned-ui-build"), marker, { mode: 0o600 }); }
  if (readFileSync(join(root, ".owned-ui-build"), "utf8") !== marker || existsSync(join(root, phase === "controls" ? "controls-run.json" : `${phase}-build.json`)))
    throw new Error("Use the owned build root without replacing an existing receipt.");
  const pin = JSON.parse(readFileSync(toolchainPath ?? join(root, "toolchain-pin.json"), "utf8"));
  if (pin.kind !== "owned-ui-toolchain-pin/v1") throw new Error("An explicit actual-tool pin is required.");
  assertToolchain(pin.toolchain, await captureToolchain(deadline));
  const pinFile = join(root, "toolchain-pin.json");
  if (existsSync(pinFile)) assertToolchain(pin.toolchain, JSON.parse(readFileSync(pinFile, "utf8")).toolchain);
  else writeFileSync(pinFile, JSON.stringify(pin, null, 2), { mode: 0o600, flag: "wx" });
  for (const producer of ["studio", "probe", "controls"]) {
    const file = join(root, producer === "controls" ? "controls-run.json" : `${producer}-build.json`);
    if (existsSync(file)) {
      const existing = JSON.parse(readFileSync(file, "utf8"));
      if (existing.sourceSha !== identity.sourceSha || existing.tree !== identity.tree)
        throw new Error("An earlier source owns this build root; create a fresh root.");
    }
  }
  const commandExecutable = phaseExecutable(phase, pin.toolchain);
  const producerOutput = phase === "studio" ? standardOutput ?? join(root, "composed-standard") : null;
  const commands = commandRecipe(phase, pin.toolchain, producerOutput);
  const startedAt = new Date().toISOString(); const executions = [];
  for (const [index, command] of commands.entries()) {
    const log = openSync(join(root, `${phase}-build-${index}.log`), "wx", 0o600);
    try {
      assertToolchain(pin.toolchain, await captureToolchain(deadline));
      const result = await runOwnedCommand(command[0], command.slice(1), { cwd: repo, log,
        timeoutMs: Math.min(commandTimeoutCap(phase, index), deadline - Date.now()),
        graceMs: phase === "studio" ? 60_000 : phase === "controls" ? 20_000 : 5000,
        env: { ...process.env, DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER: "1", MSBUILDDISABLENODEREUSE: "1" } });
      assertToolchain(pin.toolchain, await captureToolchain(deadline));
      executions.push({ argv: command, executable: commandExecutable, exitCode: result.exitCode, processGroup: result.processGroup,
        timeoutMs: result.timeoutMs, cleanup: result.cleanup });
    } finally { closeSync(log); }
  }
  if (JSON.stringify(sourceIdentity()) !== JSON.stringify(identity)) throw new Error("Source changed during the UI build.");
  const receipt = { ...identity, kind: phase === "controls" ? "successful-owned-ui-controls" : "successful-owned-ui-build", phase, standardOutput: producerOutput, commands, toolchain: pin.toolchain, executions, operationalDeadline: new Date(deadline).toISOString(), startedAt, completedAt: new Date().toISOString(), exitCode: 0, outputs: phase === "controls" ? [] : outputs(phase) };
  assertCommandExecutions(receipt, phase, pin.toolchain);
  writeFileSync(join(root, phase === "controls" ? "controls-run.json" : `${phase}-build.json`), JSON.stringify(receipt, null, 2), { mode: 0o600, flag: "wx" });
  console.log(JSON.stringify({ phase, sourceSha: identity.sourceSha, tree: identity.tree, outputCount: receipt.outputs.length, receiptSha256: digest(JSON.stringify(receipt, null, 2)) }));
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const [phase, ...args] = process.argv.slice(2); const options = new Map();
  if (args.length % 2) throw new Error("Qualification options require named values.");
  for (let index = 0; index < args.length; index += 2) {
    if (!["--root", "--standard-output", "--toolchain", "--output"].includes(args[index]) || options.has(args[index]))
      throw new Error("Unknown or duplicate qualification option.");
    options.set(args[index], args[index + 1]);
  }
  if (phase === "pin") {
    const file = options.get("--output");
    if (options.size !== 1 || !file || !isAbsolute(file) || !relative(repo, file).startsWith("..")) throw new Error("Pin tools to a new explicit file outside the checkout.");
    const pin = { kind: "owned-ui-toolchain-pin/v1", capturedAt: new Date().toISOString(), toolchain: await captureToolchain() };
    writeFileSync(file, JSON.stringify(pin, null, 2), { mode: 0o600, flag: "wx" });
    console.log(JSON.stringify({ file, node: pin.toolchain.node.version, dotnetHost: pin.toolchain.dotnet.host.version, sdk: pin.toolchain.dotnet.sdk.version, python: pin.toolchain.python.version }));
  } else {
    if (options.has("--output") || phase !== "studio" && options.has("--standard-output")) throw new Error("Invalid build options.");
    await recordBuild(phase, options.get("--root"), options.get("--standard-output"), options.get("--toolchain"));
  }
}
