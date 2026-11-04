import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { createWriteStream, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const repo = resolve(import.meta.dirname, "../../../../../..");
const clientRoot = resolve(import.meta.dirname, "..");
const { createServer } = await import(pathToFileURL(join(repo, "Studio/node_modules/vite/dist/node/index.js")));
const { chromium, expect } = await import(pathToFileURL(join(repo, "Studio/node_modules/@playwright/test/index.mjs")));
const ownedRoot = process.env.ELSA_EXTERNAL_AUTH_PROBE_ROOT ?? mkdtempSync(join(tmpdir(), "elsa-3579-browser-"));
const marker = join(ownedRoot, ".elsa-3579-owned-probe");
console.log(`Owned external-authentication qualification: ${ownedRoot}`);
if (existsSync(ownedRoot) && readdirSync(ownedRoot).length && !existsSync(marker)) throw new Error("Refusing a nonempty root without the probe ownership marker.");
if (existsSync(marker) && readFileSync(marker, "utf8") !== "elsa-external-authentication-probe/v1") throw new Error("Unsupported probe ownership marker.");
mkdirSync(ownedRoot, { recursive: true }); writeFileSync(marker, "elsa-external-authentication-probe/v1");
const server = await createServer({ configFile: false, root: clientRoot, cacheDir: join(ownedRoot, "vite-cache"),
  server: { host: "127.0.0.1", port: 0, hmr: false, fs: { allow: [repo] } },
  resolve: { dedupe: ["react", "react-dom"], alias: [
    { find: /^@elsa-workflows\/studio-sdk$/, replacement: join(repo, "Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/index.ts") },
    { find: /^@elsa-workflows\/studio-ui$/, replacement: join(repo, "Studio/src/essentials/Elsa.Studio.Hosting/StudioUi/src/index.ts") }
  ] }
});
await server.listen();
const ui = server.resolvedUrls.local[0];
const log = createWriteStream(join(ownedRoot, "host.log"));
const instance = randomUUID();
const host = spawn("dotnet", ["run", "--no-build", "--project", "tools/identity/ExternalAuthentication.Probe/ExternalAuthentication.Probe.csproj", "--", "--owned-root", ownedRoot, "--ui", ui, "--instance", instance], { cwd: repo, stdio: ["ignore", "pipe", "pipe"] });
host.stdout.pipe(log); host.stderr.pipe(log);
let browser;
try {
  const metadataFile = join(ownedRoot, "probe.json");
  const deadline = Date.now() + 60_000;
  while (!existsSync(metadataFile) || JSON.parse(readFileSync(metadataFile, "utf8")).Instance !== instance) {
    if (host.exitCode !== null || host.signalCode !== null) throw new Error(`Owned backend stopped before startup; inspect ${join(ownedRoot, "host.log")}.`);
    if (Date.now() > deadline) throw new Error(`Owned backend startup exceeded its observation window; inspect ${join(ownedRoot, "host.log")}.`);
    await new Promise(done => setTimeout(done, 100));
  }
  const metadata = JSON.parse(readFileSync(metadataFile, "utf8"));
  const backend = metadata.Backend;
  browser = await chromium.launch({ headless: true });
  const admin = await browser.newContext({ viewport: { width: 1440, height: 1100 } });
  const page = await admin.newPage();
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("dialog", dialog => dialog.accept());
  await page.goto(backend + "/_elsa/identity/login?returnUrl=/backend/__probe");
  await page.locator("input[name=username]").fill("admin"); await page.locator("input[name=password]").fill("Password123!");
  await Promise.all([page.waitForURL(url => url.pathname === "/backend/__probe"), page.getByRole("button", { name: "Sign in", exact: true }).click()]);
  const adminSession = await (await admin.request.get(backend + "/_elsa/identity/session")).json();
  await page.goto(backend + "/__tests__/live.html?backend=owned");
  await expect(page.getByRole("heading", { name: "External sign-in", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Add connection", exact: true }).click();
  await page.getByLabel("Connection key", { exact: true }).fill("studio-company"); await page.getByLabel("Display name", { exact: true }).fill("Studio company");
  await page.getByLabel("Issuer URL", { exact: true }).fill(metadata.CompanyIssuer); await page.getByLabel("Client ID", { exact: true }).fill(metadata.ClientId);
  await page.getByLabel("Client secret reference", { exact: true }).fill(metadata.CompanySecret); await page.getByLabel("Secret scope (optional)", { exact: true }).fill("identity/external");
  await page.getByLabel("Unknown identities", { exact: true }).selectOption("jit"); await page.getByLabel("Reader", { exact: true }).check();
  await page.getByRole("button", { name: "Save draft", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Studio company", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Enable sign-in", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Test configuration", exact: true }).click();
  await expect(page.getByText("Configuration test passed", { exact: true })).toBeVisible();
  const beforePreview = await (await admin.request.get(backend + "/__probe/state")).json();
  await page.getByRole("button", { name: "Preview sign-in", exact: true }).click();
  await expect(page.getByText("Preview complete. No user, role assignment or normal session was changed.", { exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText("Would be created", { exact: true })).toBeVisible();
  const afterPreview = await (await admin.request.get(backend + "/__probe/state")).json();
  expect(afterPreview.users).toBe(beforePreview.users); expect(afterPreview.links).toBe(beforePreview.links); expect(afterPreview.tokens).toBe(beforePreview.tokens);
  expect(beforePreview.sessions).toBe(1); expect(afterPreview.sessions).toBe(beforePreview.sessions);
  expect((await (await admin.request.get(backend + "/_elsa/identity/session")).json()).subject).toBe(adminSession.subject);
  expect(new URL(page.url()).searchParams.has("externalPreviewHandle")).toBe(false);
  await expect(page.getByLabel("Reader", { exact: true })).toBeVisible();
  await page.addScriptTag({ path: join(repo, "Studio/node_modules/axe-core/axe.min.js") });
  const accessibility = await page.evaluate(async () => {
    const result = await globalThis.axe.run(globalThis.document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] } });
    return result.violations.map(violation => ({ id: violation.id, impact: violation.impact,
      nodes: violation.nodes.map(node => ({ target: node.target, explanation: node.failureSummary })) }));
  });
  expect(accessibility).toEqual([]);
  await page.screenshot({ path: join(ownedRoot, "preview-light.png"), fullPage: true });
  await page.getByRole("button", { name: "Enable sign-in", exact: true }).click();
  await expect(page.getByRole("button", { name: "Disable sign-in", exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => globalThis.document.documentElement.scrollWidth <= globalThis.window.innerWidth)).toBe(true);
  await page.screenshot({ path: join(ownedRoot, "connection-narrow.png"), fullPage: true });
  const user = await browser.newContext(); const signIn = await user.newPage();
  await signIn.goto(backend + "/_elsa/identity/login?returnUrl=/backend/resource");
  await signIn.getByRole("button", { name: /Studio company/ }).click();
  await expect(signIn).toHaveURL(backend + "/resource");
  const session = await (await user.request.get(backend + "/_elsa/identity/session")).json();
  expect(session.status).toBe("authenticated"); expect(session.roles).toContain("reader"); expect(session.permissions).not.toContain("*");
  expect(session.subject).not.toBe(adminSession.subject);
  expect((await (await admin.request.get(backend + "/_elsa/identity/session")).json()).subject).toBe(adminSession.subject);
  // The real user-access component uses only its public bounded API and protected commands.
  await page.setViewportSize({ width: 1440, height: 1100 });
  await page.goto(backend + "/__tests__/live.html?backend=owned&view=access");
  await expect(page.getByRole("heading", { name: "User access", exact: true })).toBeVisible();
  const directory = await (await admin.request.get(backend + "/_elsa/identity/external-connections/users?take=25")).json();
  const target = directory.items.find(item => item.id === session.subject); expect(target).toBeTruthy();
  await page.getByLabel("Exact username or email").fill(target.userName);
  await page.getByRole("button", { name: "Find users", exact: true }).click();
  const row = page.locator(".external-auth-row").filter({ hasText: target.userName });
  await expect(row).toHaveCount(1); await row.click();
  await expect(page.getByRole("heading", { name: "Identity links", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Remove link", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Remove link", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("Keep a usable sign-in method for this user and an administrator");
  await expect(page.getByRole("button", { name: "Remove link", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Refresh user access", exact: true }).click();
  await expect(page.getByRole("button", { name: "Remove link", exact: true })).toBeEnabled();
  const currentLinks = await (await admin.request.get(backend + `/_elsa/identity/external-connections/users/${encodeURIComponent(session.subject)}/links`)).json();
  const subject = currentLinks.items[0].subject;
  await page.getByLabel("Connection", { exact: true }).selectOption("deployment:partner");
  await page.getByLabel("External subject", { exact: true }).fill(subject);
  await page.getByRole("button", { name: "Attach identity", exact: true }).click();
  await expect(page.locator(".external-auth-notice")).toContainText("Identity attached. Saved Elsa roles are unchanged.");
  // Establish an actual normal sign-in through the newly attached provider, without a synthetic witness.
  const csrf = await (await user.request.get(backend + "/identity/external/antiforgery")).json();
  await signIn.evaluate(({ action, field, token }) => {
    const form = document.createElement("form"); form.method = "post"; form.action = action;
    for (const [name, value] of Object.entries({ [field]: token, returnUrl: "/backend/resource" })) {
      const input = document.createElement("input"); input.type = "hidden"; input.name = name; input.value = value; form.appendChild(input);
    }
    document.body.appendChild(form); form.submit();
  }, { action: backend + "/identity/external/signin/partner", field: csrf.formFieldName, token: csrf.requestToken });
  await expect.poll(async () => (await (await user.request.get(backend + "/_elsa/identity/session")).json()).provider).toBe("external:partner");
  await expect(signIn).toHaveURL(backend + "/resource");
  const partnerSession = await (await user.request.get(backend + "/_elsa/identity/session")).json(); expect(partnerSession.subject).toBe(session.subject);
  await page.getByRole("button", { name: "Refresh user access", exact: true }).click();
  const companyCard = page.getByRole("region", { name: "Identity links", exact: true }).locator("article").filter({ has: page.getByRole("heading", { name: "Studio company", exact: true }) });
  await expect(companyCard.getByRole("button", { name: "Remove link", exact: true })).toBeEnabled();
  await companyCard.getByRole("button", { name: "Remove link", exact: true }).click();
  await expect(page.locator(".external-auth-notice")).toContainText("Identity link removed.");
  expect((await (await user.request.get(backend + "/_elsa/identity/session")).json()).subject).toBe(session.subject);
  // A reader can use the directory, while link/session inspection remains explicitly denied.
  const readerPage = await user.newPage(); await readerPage.goto(backend + "/__tests__/live.html?backend=owned&view=access");
  await expect(readerPage.getByRole("heading", { name: "User access", exact: true })).toBeVisible();
  await readerPage.getByLabel("Exact username or email").fill(target.userName); await readerPage.getByRole("button", { name: "Find users", exact: true }).click();
  await readerPage.locator(".external-auth-row").filter({ hasText: target.userName }).click();
  await expect(readerPage.getByText("You do not have permission to read identity links.", { exact: true })).toBeVisible();
  await expect(readerPage.getByText("You do not have permission to read sessions.", { exact: true })).toBeVisible();
  await expect(readerPage.getByRole("button", { name: "Attach identity", exact: true })).toHaveCount(0);
  const pair = await (await user.request.get(backend + "/_elsa/identity/token")).json();
  expect((await user.request.get(backend + "/resource", { headers: { Authorization: `Bearer ${pair.accessToken}` } })).status()).toBe(200);
  const partnerCard = page.getByRole("region", { name: "Sessions", exact: true }).locator("article").filter({ has: page.getByRole("heading", { name: "partner", exact: true }) });
  await expect(partnerCard.getByRole("button", { name: "Revoke session", exact: true })).toBeEnabled();
  await partnerCard.getByRole("button", { name: "Revoke session", exact: true }).click();
  await expect(page.locator(".external-auth-notice")).toContainText("Session revoked. Other sessions are unchanged.");
  await expect(partnerCard.getByText("revoked", { exact: true })).toBeVisible();
  await expect(partnerCard.getByRole("button", { name: "Revoke session", exact: true })).toBeDisabled();
  await expect(page.getByText("Loading user access…", { exact: true })).toHaveCount(0);
  const cookieChallenge = await user.request.get(backend + "/resource", { maxRedirects: 0 });
  expect(cookieChallenge.status()).toBe(302);
  expect(new URL(cookieChallenge.headers().location).pathname).toBe("/backend/_elsa/identity/login");
  expect((await (await user.request.get(backend + "/_elsa/identity/session")).json()).status).toBe("anonymous");
  expect((await user.request.get(backend + "/resource", { headers: { Authorization: `Bearer ${pair.accessToken}` } })).status()).toBe(401);
  expect((await user.request.post(backend + "/_elsa/identity/refresh", { data: { refreshToken: pair.refreshToken } })).status()).toBe(401);
  await page.addScriptTag({ path: join(repo, "Studio/node_modules/axe-core/axe.min.js") });
  const accessAccessibility = await page.evaluate(async () => {
    const result = await globalThis.axe.run(globalThis.document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] } });
    return result.violations.map(item => ({ id: item.id, impact: item.impact, nodes: item.nodes.map(node => ({ target: node.target, explanation: node.failureSummary })) }));
  });
  expect(accessAccessibility).toEqual([]);
  await page.screenshot({ path: join(ownedRoot, "user-access-light.png"), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => globalThis.document.documentElement.scrollWidth <= globalThis.window.innerWidth)).toBe(true);
  await page.screenshot({ path: join(ownedRoot, "user-access-narrow.png"), fullPage: true });
  const access = { targetRolesPreserved: partnerSession.roles.includes("reader"), finalPathRefused: true, attachedExistingUser: true,
    provedSiblingThenRemoved: true, readerPanelsDenied: true, selectedRevocation: { cookie: true, access: true, refresh: true }, accessibility: accessAccessibility };
  expect(errors).toEqual([]);
  const result = { passed: true, ownedRoot, beforePreview, afterPreview, ordinaryUser: { roles: session.roles, permissions: session.permissions },
    accessibility, access, provider: "controlled owned HTTP OIDC fixture", store: "native SQLite", browser: "Chromium", limitations: ["not an external IdP product", "not a full Studio host", "not server-database or separate-node qualification"] };
  writeFileSync(join(ownedRoot, "browser-receipt.json"), JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result));
} catch (error) {
  if (browser) {
    for (const [index, page] of browser.contexts().flatMap(context => context.pages()).entries()) {
      await page.screenshot({ path: join(ownedRoot, `failure-${index}.png`), fullPage: true }).catch(() => {});
      writeFileSync(join(ownedRoot, `failure-${index}.json`), JSON.stringify({ path: new URL(page.url()).pathname,
        text: (await page.locator("body").innerText().catch(() => "")).slice(0, 2000) }));
    }
  }
  throw error;
} finally {
  if (browser) await browser.close(); await server.close();
  if (host.exitCode === null && host.signalCode === null) {
    const stopped = new Promise(done => host.once("exit", done)); host.kill("SIGTERM");
    await Promise.race([stopped, new Promise(done => setTimeout(done, 5000))]);
    if (host.exitCode === null && host.signalCode === null) { host.kill("SIGKILL"); await stopped; }
  }
  log.end();
}
