import { spawn } from "node:child_process";
import { generateKeyPairSync, randomBytes } from "node:crypto";
import { closeSync, mkdirSync, mkdtempSync, openSync, writeFileSync } from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const repo = resolve(import.meta.dirname, "../../..");
const { chromium, expect } = await import(pathToFileURL(join(repo, "Studio/node_modules/@playwright/test/index.mjs")));
const root = mkdtempSync(join(tmpdir(), "elsa-3579-workbench-"));
console.log(`Owned Workbench qualification: ${root}`);
mkdirSync(join(root, "packages"));
writeFileSync(join(root, ".elsa-3579-owned-probe"), "elsa-external-authentication-probe/v1");
const listener = createServer();
await new Promise((done, reject) => { listener.once("error", reject); listener.listen(0, "127.0.0.1", done); });
const origin = `http://127.0.0.1:${listener.address().port}`;
await new Promise(done => listener.close(done));
const backend = origin + "/default";
function fixturePassword() {
  for (let attempt = 0; attempt < 32; attempt++) {
    const value = randomBytes(24).toString("base64url");
    if (/[A-Z]/.test(value) && /[a-z]/.test(value) && /[0-9]/.test(value) && /[^A-Za-z0-9]/.test(value)) return value;
  }
  throw new Error("Could not generate an owned fixture password satisfying the standard policy.");
}
const password = fixturePassword();
const key = generateKeyPairSync("rsa", { modulusLength: 2048, privateKeyEncoding: { type: "pkcs8", format: "der" }, publicKeyEncoding: { type: "spki", format: "der" } });
const connection = `Data Source=${join(root, "identity.db")};Pooling=False`;
const features = {
  ApiCapabilities: {}, FoundationIdentityAbstractions: {}, FoundationIdentityApi: {},
  FoundationIdentityAspNetCoreIdentity: { IsDefault: true },
  FoundationIdentityAspNetCoreIdentityEntityFrameworkCore: { ConnectionString: connection, IsDevelopmentOrDemo: true,
    SeedAdminUserName: "admin", SeedAdminPassword: password, SeedAdminRoleName: "administrator" },
  IdentityIamEntityFrameworkCore: { ConnectionString: connection }, IdentityProviderConfigurationEntityFrameworkCore: { ConnectionString: connection },
  FoundationIdentityOpenIddict: { IsDevelopmentOrDemo: false, SigningKey: key.privateKey.toString("base64"),
    ConnectionString: `Data Source=${join(root, "tokens.db")};Pooling=False`, AutoMigrate: true },
  FoundationIdentityExternalAuthentication: { PublicBaseUrl: backend, AllowDatabaseManagement: true, AllowDeploymentOverrides: true },
  FoundationIdentityExternalAuthenticationApi: {}, FoundationIdentityExternalOidc: { AllowLoopbackHttpForDevelopment: true },
  FoundationIdentityExternalAuthenticationEntityFrameworkCore: { ConnectionString: connection },
  Secrets: { EncryptionKey: randomBytes(32).toString("base64") }, SecretsEntityFrameworkCore: { ConnectionString: connection }
};
writeFileSync(join(root, "shells.json"), JSON.stringify({ CShells: { Shells: { default: {
  Name: "default", Features: features, Configuration: { WebRouting: { Path: "default" } }
} } } }), { mode: 0o600 });
writeFileSync(join(root, "appsettings.json"), JSON.stringify({
  Logging: { LogLevel: { Default: "Warning", "Microsoft.Hosting.Lifetime": "Information" } },
  ConnectionStrings: { Elsa: connection },
  Elsa: { DataProtection: { ApplicationName: "elsa-3579-owned-workbench", EntityFrameworkCore: { Enabled: true,
    Provider: "Sqlite", ConnectionString: `Data Source=${join(root, "keys.db")};Pooling=False` } } },
  Nuplane: { Setup: { AutomaticReconciliation: false, Feeds: [{ Name: "local-packages", DirectoryPath: "packages", IncludePatterns: ["*"], Directory: { Watch: false } }] } }
}), { mode: 0o600 });
const log = openSync(join(root, "workbench.log"), "w", 0o600);
const environment = { ...process.env, ASPNETCORE_ENVIRONMENT: "Development", DOTNET_ENVIRONMENT: "Development" };
for (const name of Object.keys(environment)) if (/^(Elsa__|CShells__|ConnectionStrings__|ASPNETCORE_URLS)/i.test(name)) delete environment[name];
const host = spawn("dotnet", [join(repo, "Essentials/apps/Elsa.Workbench/bin/Debug/net10.0/Elsa.Workbench.dll"),
  "--contentRoot", root, "--urls", origin], { cwd: root, env: environment, stdio: ["ignore", log, log] });
let browser;
try {
  const deadline = Date.now() + 120_000;
  while (true) {
    try { if ((await fetch(origin, { signal: AbortSignal.timeout(2000) })).ok) break; } catch { /* owned startup only */ }
    if (host.exitCode !== null || host.signalCode !== null || Date.now() >= deadline) throw new Error(`Owned Workbench did not start; inspect ${join(root, "workbench.log")}.`);
    await new Promise(done => setTimeout(done, 250));
  }
  browser = await chromium.launch({ headless: true }); const context = await browser.newContext(); const page = await context.newPage();
  await page.goto(backend + "/_elsa/identity/login?returnUrl=/");
  await page.getByLabel("Username", { exact: true }).fill("admin"); await page.getByLabel("Password", { exact: true }).fill(password);
  await Promise.all([page.waitForURL(origin + "/"), page.getByRole("button", { name: "Sign in", exact: true }).click()]);
  const session = await (await context.request.get(backend + "/_elsa/identity/session")).json(); expect(session.status).toBe("authenticated");
  const capabilities = await (await context.request.get(backend + "/capabilities")).json();
  expect(JSON.stringify(capabilities)).toContain("elsa.api.identity.external-authentication");
  const catalogResponse = await context.request.get(backend + "/_elsa/identity/external-connections"); expect(catalogResponse.status()).toBe(200);
  const catalog = await catalogResponse.json(); expect(catalog.canManageConnections).toBe(true); expect(catalog.connections).toHaveLength(0);
  const users = await context.request.get(backend + "/_elsa/identity/external-connections/users?take=25"); expect(users.status()).toBe(200);
  const directory = await users.json(); expect(directory.canReadLinks).toBe(true); expect(directory.canReadSessions).toBe(true);
  expect(directory.linkManagementUnavailableReason).toBe(null); expect(directory.sessionAdministrationUnavailableReason).toBe(null);
  const sessions = await context.request.get(`${backend}/_elsa/identity/external-connections/users/${encodeURIComponent(session.subject)}/sessions`);
  expect(sessions.status()).toBe(200); expect((await sessions.json()).items).toHaveLength(1);
  const result = { passed: true, ownedRoot: root, host: "actual source-built Workbench", store: "native SQLite",
    optionalFeaturesActivated: true, capabilityAdvertised: true, localSignIn: true, managedCatalog: true, durableSessionInspection: true,
    limitations: ["Workbench composition smoke; provider/browser journeys are qualified separately"] };
  writeFileSync(join(root, "workbench-receipt.json"), JSON.stringify(result, null, 2)); console.log(JSON.stringify(result));
} finally {
  if (browser) await browser.close();
  if (host.exitCode === null && host.signalCode === null) {
    const stopped = new Promise(done => host.once("exit", done)); host.kill("SIGTERM");
    await Promise.race([stopped, new Promise(done => setTimeout(done, 5000))]);
    if (host.exitCode === null && host.signalCode === null) { host.kill("SIGKILL"); await stopped; }
  }
  closeSync(log);
}
