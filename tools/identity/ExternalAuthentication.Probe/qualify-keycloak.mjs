import { execFileSync, spawn } from "node:child_process";
import { randomUUID, randomBytes } from "node:crypto";
import { closeSync, mkdirSync, mkdtempSync, openSync, readFileSync, writeFileSync } from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

// Development-only interoperability proof. Every account, credential, port, store and container is owned here.
const repo = resolve(import.meta.dirname, "../../..");
const root = mkdtempSync(join(tmpdir(), "elsa-3579-keycloak-"));
const owner = randomUUID();
const image = "quay.io/keycloak/keycloak@sha256:3f3c5f8e03c614774b4bd9173b88391fefa4153bfc010d8012d59ad825377332";
const marker = "elsa-external-authentication-probe/v1";
writeFileSync(join(root, ".elsa-3579-owned-probe"), marker);
console.log(`Owned Keycloak qualification: ${root}`);
const realms = join(root, "realms"); mkdirSync(realms);
const password = randomBytes(24).toString("base64url");
const secret = randomBytes(32).toString("base64url");
const clientId = "elsa-owned-probe";
const username = "owned-reader";
const listener = createServer();
await new Promise((done, reject) => { listener.once("error", reject); listener.listen(0, "127.0.0.1", done); });
const backendPort = listener.address().port;
await new Promise(done => listener.close(done));
const backend = `http://127.0.0.1:${backendPort}/backend`;
for (const realm of ["company", "partner"]) {
  mkdirSync(join(realms, realm));
  const callbacks = [realm, "studio-" + realm].flatMap(key => ["signin", "preview"].map(purpose => `${backend}/identity/external/${purpose}/${key}/callback`));
  writeFileSync(join(realms, realm, `${realm}-realm.json`), JSON.stringify({
    realm, enabled: true, sslRequired: "none", registrationAllowed: false,
    roles: { realm: [{ name: "upstream-administrator" }] },
    clients: [{ clientId, enabled: true, protocol: "openid-connect", publicClient: false, secret,
      standardFlowEnabled: true, directAccessGrantsEnabled: false, redirectUris: callbacks,
      defaultClientScopes: ["profile", "email", "roles"], attributes: { "pkce.code.challenge.method": "S256" } }],
    users: [{ id: "57c019b9-b2b1-4699-bc01-000000003579", username, enabled: true,
      firstName: "Ada", lastName: "Owned", email: "owned-reader@example.test", emailVerified: true,
      realmRoles: ["upstream-administrator"], credentials: [{ type: "password", value: password, temporary: false }] }]
  }), { mode: 0o644 });
}
writeFileSync(join(root, "client-secret.txt"), secret, { mode: 0o600 });
writeFileSync(join(root, "browser-credentials.json"), JSON.stringify({ username, password }), { mode: 0o600 });
const docker = (args, options = {}) => execFileSync("docker", args, { encoding: "utf8", timeout: 60_000, ...options })?.trim() ?? "";
const containers = [];
try {
  docker(["image", "inspect", image], { stdio: "ignore" });
  const issuers = {};
  for (const realm of ["company", "partner"]) {
    const container = docker(["run", "--detach", "--name", `elsa-3579-keycloak-${realm}-${owner}`, "--label", `elsa.qualification.owner=${owner}`,
      "--cpus", "1", "--memory", "1g", "--publish", "127.0.0.1::8080", "--mount", `type=bind,source=${join(realms, realm)},target=/opt/keycloak/data/import,readonly`,
      image, "start-dev", "--import-realm", "--hostname-strict=false"]);
    containers.push({ container, realm });
    const bindings = JSON.parse(docker(["inspect", "--format", "{{json .NetworkSettings.Ports}}", container]));
    const provider = `http://127.0.0.1:${bindings["8080/tcp"][0].HostPort}`;
    issuers[realm] = `${provider}/realms/${realm}`;
    const deadline = Date.now() + 240_000;
    while (true) {
      try {
        const response = await fetch(`${provider}/realms/${realm}/.well-known/openid-configuration`, { signal: AbortSignal.timeout(2000) });
        if (response.ok && (await response.json()).issuer === `${provider}/realms/${realm}`) break;
      } catch { /* bounded startup observation; no alternate instance or production service */ }
      if (Date.now() >= deadline || docker(["inspect", "--format", "{{.State.Running}}", container]) !== "true")
        throw new Error(`Owned Keycloak did not become ready; inspect the retained keycloak-${realm}.log.`);
      await new Promise(done => setTimeout(done, 500));
    }
  }
  writeFileSync(join(root, "probe-settings.json"), JSON.stringify({ provider: "Keycloak", databaseProvider: "Sqlite", listen: `http://127.0.0.1:${backendPort}`,
    companyIssuer: issuers.company, partnerIssuer: issuers.partner, clientId, clientSecretFile: "client-secret.txt" }), { mode: 0o600 });
  const log = openSync(join(root, "keycloak-journey.log"), "w", 0o600);
  let code;
  try {
    const entry = process.argv.includes("--studio") ? "tools/identity/ExternalAuthentication.Probe/qualify-studio.mjs"
      : "Extensions/Identity/studio/Elsa.Studio.Identity.ExternalAuthentication/Client/scripts/browser.mjs";
    const child = spawn(process.execPath, [join(repo, entry)],
      { cwd: repo, env: { ...process.env, ELSA_EXTERNAL_AUTH_PROBE_ROOT: root }, stdio: ["ignore", log, log] });
    code = await new Promise((done, reject) => { child.once("error", reject); child.once("exit", done); });
  } finally { closeSync(log); }
  if (code !== 0) throw new Error(`Owned browser journey failed (${code}); inspect ${join(root, "keycloak-journey.log")}.`);
  const receipt = JSON.parse(readFileSync(join(root, "browser-receipt.json"), "utf8"));
  const result = { ...receipt, image, twoIndependentProvidersWithEqualSubjects: true, resourceLimitsPerProvider: { cpus: 1, memory: "1g" } };
  writeFileSync(join(root, "keycloak-receipt.json"), JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result));
} finally {
  for (const { container, realm } of containers.reverse()) {
    if (docker(["inspect", "--format", '{{index .Config.Labels "elsa.qualification.owner"}}', container]) !== owner)
      throw new Error("Refusing to stop a container without this invocation's ownership label.");
    const log = openSync(join(root, `keycloak-${realm}.log`), "w", 0o600);
    try { docker(["logs", container], { stdio: ["ignore", log, log] }); } finally { closeSync(log); }
    docker(["stop", "--time", "10", container]); docker(["rm", container]);
  }
}
