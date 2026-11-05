import { execFileSync, spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { closeSync, existsSync, lstatSync, mkdirSync, openSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { isAbsolute, join, relative, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const repo = resolve(import.meta.dirname, "../../..");
const studioBin = "Studio/src/apps/Elsa.Studio.Web/bin/Debug/net10.0";
const probeProject = "tools/identity/ExternalAuthentication.Probe/ExternalAuthentication.Probe.csproj";
const moduleRoot = "Extensions/Identity/studio/Elsa.Studio.Identity.ExternalAuthentication/wwwroot";
const marker = "elsa-external-auth-ui-build/v1";
const digest = value => createHash("sha256").update(value).digest("hex");
export function sourceIdentity() {
  const git = (...args) => execFileSync("git", args, { cwd: repo, encoding: "utf8" }).trim();
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
export function assertBuildReceipt(receipt, phase, identity, actualOutputs) {
  if (receipt?.kind !== "successful-owned-ui-build" || receipt.phase !== phase || receipt.exitCode !== 0 ||
      receipt.sourceSha !== identity.sourceSha || receipt.tree !== identity.tree || !Array.isArray(receipt.commands) || !receipt.commands.length ||
      !Array.isArray(receipt.outputs) || !receipt.outputs.length || JSON.stringify(receipt.outputs) !== JSON.stringify(actualOutputs))
    throw new Error("UI build receipt does not match current source and actual outputs.");
}
export function verifyUiBuild(root = process.env.ELSA_EXTERNAL_AUTH_UI_BUILD_ROOT) {
  if (!root || readFileSync(join(root, ".owned-ui-build"), "utf8") !== marker) throw new Error("An owned successful UI build receipt is required.");
  const identity = sourceIdentity(); const proof = { ...identity, kind: "verified-owned-ui-build", builds: [] };
  for (const phase of ["studio", "probe"]) {
    const receipt = JSON.parse(readFileSync(join(root, `${phase}-build.json`), "utf8"));
    assertBuildReceipt(receipt, phase, identity, outputs(phase)); proof.builds.push(receipt);
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

async function recordBuild(phase, root, standardOutput) {
  if (!["studio", "probe"].includes(phase) || !root || !isAbsolute(root) || !relative(repo, root).startsWith(".."))
    throw new Error("Select studio or probe and an absolute owned build root outside the checkout.");
  if (phase === "studio" && standardOutput && (!isAbsolute(standardOutput) || !relative(repo, standardOutput).startsWith("..") || existsSync(standardOutput)))
    throw new Error("Standard composed evidence requires a new absolute output outside the checkout.");
  const identity = sourceIdentity();
  if (!existsSync(root)) { mkdirSync(root, { mode: 0o700 }); writeFileSync(join(root, ".owned-ui-build"), marker, { mode: 0o600 }); }
  if (readFileSync(join(root, ".owned-ui-build"), "utf8") !== marker || existsSync(join(root, `${phase}-build.json`)))
    throw new Error("Use the owned build root without replacing an existing receipt.");
  for (const producer of ["studio", "probe"]) {
    const file = join(root, `${producer}-build.json`);
    if (existsSync(file)) {
      const existing = JSON.parse(readFileSync(file, "utf8"));
      if (existing.sourceSha !== identity.sourceSha || existing.tree !== identity.tree)
        throw new Error("An earlier source owns this build root; create a fresh root.");
    }
  }
  const commands = phase === "studio" ? [["python3", "tools/ux-acceptance/run.py", "--output", standardOutput ?? join(root, "composed-standard")]] : [
    ["dotnet", "restore", probeProject, "--locked-mode", "--disable-parallel", "-m:2", "-p:BuildInParallel=false"],
    ["dotnet", "build", probeProject, "--no-restore", "-m:2", "-p:BuildInParallel=false", "-p:WarningsNotAsErrors=NU1603"]];
  const startedAt = new Date().toISOString();
  for (const [index, command] of commands.entries()) {
    const log = openSync(join(root, `${phase}-build-${index}.log`), "wx", 0o600);
    let exitCode;
    try {
      const child = spawn(command[0], command.slice(1), { cwd: repo, stdio: ["ignore", log, log] });
      exitCode = await new Promise((done, reject) => { child.once("error", reject); child.once("exit", done); });
    } finally { closeSync(log); }
    if (exitCode !== 0) throw new Error("Owned UI build failed; no successful receipt emitted. Inspect its private build log.");
  }
  if (JSON.stringify(sourceIdentity()) !== JSON.stringify(identity)) throw new Error("Source changed during the UI build.");
  const receipt = { ...identity, kind: "successful-owned-ui-build", phase, commands, startedAt, completedAt: new Date().toISOString(), exitCode: 0, outputs: outputs(phase) };
  writeFileSync(join(root, `${phase}-build.json`), JSON.stringify(receipt, null, 2), { mode: 0o600, flag: "wx" });
  console.log(JSON.stringify({ phase, sourceSha: identity.sourceSha, tree: identity.tree, outputCount: receipt.outputs.length, receiptSha256: digest(JSON.stringify(receipt, null, 2)) }));
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const args = process.argv.slice(2); const [phase, flag, root, outputFlag, standardOutput] = args;
  if (flag !== "--root" || ![3, 5].includes(args.length) || outputFlag && (phase !== "studio" || outputFlag !== "--standard-output" || !standardOutput))
    throw new Error("Usage: ui-build-provenance.mjs studio|probe --root <owned-root> [--standard-output <new-output>]");
  await recordBuild(phase, root, standardOutput);
}
