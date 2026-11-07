import { randomBytes } from "node:crypto";
import { execFileSync, spawn } from "node:child_process";
import { closeSync, existsSync, mkdirSync, mkdtempSync, openSync, readFileSync, writeFileSync } from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { verifyServedModule, verifyUiBuild } from "./ui-build-provenance.mjs";

const repo = resolve(import.meta.dirname, "../../..");
const git = (...args) => execFileSync("git", args, { cwd: repo, encoding: "utf8" }).trim();
const sourceSha = git("rev-parse", "HEAD");
if (git("status", "--porcelain")) throw new Error("Commit source changes before full Studio qualification; a dirty run cannot certify a revision.");
const buildProvenance = await verifyUiBuild();
const profile = process.argv[2] ?? null;
const profiles = {
  empty: { emptyCatalog: true },
  configuration: { databaseManagement: false },
  "provider-reader": { secretsAvailable: false, administratorPermissions: ["identity/providers:read", "identity/users:read", "api-capabilities:read"] },
  denied: { administratorPermissions: ["identity/users:read", "api-capabilities:read"] },
  "external-owned": { ownershipMode: "ExternalOwned" }, hybrid: { ownershipMode: "Hybrid" },
  "missing-api": { managedApi: false }, "missing-sessions": { sessionStore: false }
};
if (profile && !Object.hasOwn(profiles, profile)) throw new Error("Unknown owned Studio qualification profile.");
const root = process.env.ELSA_EXTERNAL_AUTH_PROBE_ROOT ?? mkdtempSync(join(tmpdir(), `elsa-3579-full-studio-${profile ? profile + "-" : ""}`));
console.log(`Owned full Studio qualification: ${root}`);
if (process.env.ELSA_EXTERNAL_AUTH_PROBE_ROOT && (!existsSync(join(root, ".elsa-3579-owned-probe")) ||
  readFileSync(join(root, ".elsa-3579-owned-probe"), "utf8") !== "elsa-external-authentication-probe/v1" || existsSync(join(root, "identity.db"))))
  throw new Error("A parent-owned fixture needs its ownership marker and must not contain an existing IAM store.");
writeFileSync(join(root, ".elsa-3579-owned-probe"), "elsa-external-authentication-probe/v1");
const previous = existsSync(join(root, "probe-settings.json")) ? JSON.parse(readFileSync(join(root, "probe-settings.json"), "utf8")) : {};
async function port() {
  const listener = createServer();
  await new Promise((done, reject) => { listener.once("error", reject); listener.listen(0, "127.0.0.1", done); });
  const result = listener.address().port; await new Promise(done => listener.close(done)); return result;
}
const backend = (previous.listen ?? `http://127.0.0.1:${await port()}`) + "/backend";
const studioUrl = `http://127.0.0.1:${await port()}`;
const uiRecovery = !profile && previous.provider !== "Keycloak";
if (uiRecovery) writeFileSync(join(root, "ui-control-key.txt"), randomBytes(32).toString("hex"), { mode: 0o600 });
writeFileSync(join(root, "probe-settings.json"), JSON.stringify({ ...previous, ...profiles[profile], emptyCatalog: profile === "empty", uiRecovery, uiProfile: profile, listen: new URL(backend).origin, studioUrl }), { mode: 0o600 });
const content = join(root, "studio"); mkdirSync(content); mkdirSync(join(content, "packages")); mkdirSync(join(content, "wwwroot"));
writeFileSync(join(content, "appsettings.json"), JSON.stringify({
  Logging: { LogLevel: { Default: "Warning", "Microsoft.Hosting.Lifetime": "Information" } },
  Studio: { HostId: "elsa-3579-owned-studio", BackendBaseUrl: backend, Auth: { Enabled: true },
    Backends: { DefaultId: "owned", AllowPersonalProfiles: false,
      Profiles: [{ Id: "owned", Name: "Owned authentication backend", BaseUrl: backend, Environment: "Test" }] } }
}));
writeFileSync(join(content, "shells.json"), JSON.stringify({ CShells: { Shells: { Default: {
  Features: { StudioApi: {}, ExternalAuthenticationStudio: {}, BackendsStudio: {}, ThemeManagement: {}, ThemePicker: {}, ThemeStoreCore: {} },
  Configuration: { WebRouting: { Path: "" } }
} } } }));
writeFileSync(join(content, "nuplane-management.json"), JSON.stringify({ Nuplane: {
  Setup: { AutomaticReconciliation: false, Feeds: [{ Name: "studio-local-packages", DirectoryPath: "packages", IncludePatterns: ["*"], Directory: { Watch: false } }] },
  Loading: { Enabled: false }
} }));
const log = openSync(join(root, "studio.log"), "w", 0o600);
const environment = { ...process.env, ASPNETCORE_ENVIRONMENT: "Development", DOTNET_ENVIRONMENT: "Development" };
// Prevent ambient Studio/shell selections from retargeting this owned host.
for (const key of Object.keys(environment)) if (/^(Studio__|CShells__|ASPNETCORE_URLS)/i.test(key)) delete environment[key];
const host = spawn(buildProvenance.toolchain.dotnet.launcher.path, [join(repo, "Studio/src/apps/Elsa.Studio.Web/bin/Debug/net10.0/Elsa.Studio.Web.dll"),
  "--contentRoot", content, "--urls", studioUrl], { cwd: content, env: environment, stdio: ["ignore", log, log] });
try {
  const deadline = Date.now() + 90_000;
  while (true) {
    try {
      const response = await fetch(studioUrl + "/studio/index.html", { signal: AbortSignal.timeout(2000) });
      if (response.ok) break;
    } catch { /* bounded observation of this process only */ }
    if (host.exitCode !== null || host.signalCode !== null || Date.now() >= deadline)
      throw new Error(`Owned Studio did not become ready; inspect ${join(root, "studio.log")}.`);
    await new Promise(done => setTimeout(done, 250));
  }
  const servedModule = await verifyServedModule(studioUrl, buildProvenance);
  writeFileSync(join(root, "build-provenance.json"), JSON.stringify({ ...buildProvenance, servedModule }, null, 2), { mode: 0o600 });
  const browserLog = openSync(join(root, "browser.log"), "w", 0o600);
  let code;
  try {
    const entry = profile ? "tools/identity/ExternalAuthentication.Probe/ui-modes-browser.mjs"
      : "Extensions/Identity/studio/Elsa.Studio.Identity.ExternalAuthentication/Client/scripts/browser.mjs";
    const child = spawn(process.execPath, [join(repo, entry)],
      { cwd: repo, env: { ...environment, ELSA_EXTERNAL_AUTH_PROBE_ROOT: root }, stdio: ["ignore", browserLog, browserLog] });
    code = await new Promise((done, reject) => { child.once("error", reject); child.once("exit", done); });
  } finally { closeSync(browserLog); }
  if (code !== 0) throw new Error(`Owned full Studio browser journey failed (${code}); inspect ${join(root, "browser.log")}.`);
  if (git("rev-parse", "HEAD") !== sourceSha || git("status", "--porcelain")) throw new Error("Source changed during Studio qualification; rerun the committed revision.");
  await verifyUiBuild(); await verifyServedModule(studioUrl, buildProvenance);
  const receipt = { ...JSON.parse(readFileSync(join(root, "browser-receipt.json"), "utf8")),
    sourceSha, headSha: process.env.UX_PR_HEAD_SHA ?? sourceSha, dirty: false, kind: "composed-source", buildProvenance: { ...buildProvenance, servedModule },
    visualApproval: "requires independent review" };
  if (!receipt.fullStudioHost) throw new Error("Browser receipt did not prove the full Studio host.");
  writeFileSync(join(root, "studio-receipt.json"), JSON.stringify(receipt, null, 2));
  console.log(JSON.stringify(receipt));
} finally {
  if (host.exitCode === null && host.signalCode === null) {
    const stopped = new Promise(done => host.once("exit", done)); host.kill("SIGTERM");
    await Promise.race([stopped, new Promise(done => setTimeout(done, 5000))]);
    if (host.exitCode === null && host.signalCode === null) { host.kill("SIGKILL"); await stopped; }
  }
  closeSync(log);
}
