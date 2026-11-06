import { execFileSync, spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { createWriteStream, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const repo = resolve(import.meta.dirname, "../../../../../..");
const clientRoot = resolve(import.meta.dirname, "..");
const { createUiEvidence } = await import(pathToFileURL(join(repo, "tools/identity/ExternalAuthentication.Probe/ui-evidence.mjs")));
const { verifyUiBuild } = await import(pathToFileURL(join(repo, "tools/identity/ExternalAuthentication.Probe/ui-build-provenance.mjs")));
const recovery = await import(pathToFileURL(join(repo, "tools/identity/ExternalAuthentication.Probe/ui-recovery.mjs")));
const sourceSha = execFileSync("git", ["rev-parse", "HEAD"], { cwd: repo, encoding: "utf8" }).trim();
const sourceDirty = Boolean(execFileSync("git", ["status", "--porcelain"], { cwd: repo, encoding: "utf8" }).trim());
let uiEvidence;
const { createServer } = await import(pathToFileURL(join(repo, "Studio/node_modules/vite/dist/node/index.js")));
const { chromium, expect: baseExpect } = await import(pathToFileURL(join(repo, "Studio/node_modules/@playwright/test/index.mjs")));
const expect = baseExpect.configure({ timeout: 30_000 });
const ownedRoot = process.env.ELSA_EXTERNAL_AUTH_PROBE_ROOT ?? mkdtempSync(join(tmpdir(), "elsa-3579-browser-"));
const marker = join(ownedRoot, ".elsa-3579-owned-probe");
console.log(`Owned external-authentication qualification: ${ownedRoot}`);
if (existsSync(ownedRoot) && readdirSync(ownedRoot).length && !existsSync(marker)) throw new Error("Refusing a nonempty root without the probe ownership marker.");
if (existsSync(marker) && readFileSync(marker, "utf8") !== "elsa-external-authentication-probe/v1") throw new Error("Unsupported probe ownership marker.");
mkdirSync(ownedRoot, { recursive: true }); writeFileSync(marker, "elsa-external-authentication-probe/v1");
const settingsPath = join(ownedRoot, "probe-settings.json");
const settings = existsSync(settingsPath) ? JSON.parse(readFileSync(settingsPath, "utf8")) : {};
const buildProvenance = settings.studioUrl ? await verifyUiBuild() : null;
async function completeProviderLogin(page) {
  if (settings.provider !== "Keycloak") return;
  const credentials = JSON.parse(readFileSync(join(ownedRoot, "browser-credentials.json"), "utf8"));
  await page.locator("#username").fill(credentials.username);
  await page.locator("#password").fill(credentials.password);
  await page.locator("#kc-login").click();
}
let confirmationNumber = 0;
async function confirmStudio(page, title, label = title) {
  if (settings.studioUrl) {
    const dialog = page.getByRole("dialog", { name: title, exact: true });
    await expect(dialog).toBeVisible();
    await dialog.evaluate(async element => {
      await element.ownerDocument.fonts.ready;
      await Promise.all(element.getAnimations({ subtree: true })
        .filter(animation => Number.isFinite(animation.effect?.getComputedTiming().endTime))
        .map(animation => animation.finished));
    });
    const geometry = await dialog.evaluate(element => {
      const bounds = element.getBoundingClientRect();
      return { width: element.clientWidth, scroll: element.scrollWidth, left: bounds.left, right: bounds.right,
        viewport: element.ownerDocument.documentElement.clientWidth,
        content: [...element.querySelectorAll(".studio-dialog-message, button")].map(child => {
          const box = child.getBoundingClientRect();
          return { left: box.left, right: box.right };
        }) };
    });
    expect(geometry.scroll).toBeLessThanOrEqual(geometry.width + 1);
    expect(geometry.left).toBeGreaterThanOrEqual(0);
    expect(geometry.right).toBeLessThanOrEqual(geometry.viewport);
    for (const box of geometry.content) {
      expect(box.left).toBeGreaterThanOrEqual(geometry.left);
      expect(box.right).toBeLessThanOrEqual(geometry.right);
    }
    confirmationNumber++;
    await uiEvidence.capture(page, `dialog-${confirmationNumber}-${title.toLowerCase().replace(/[^a-z0-9]+/g, "-").slice(0, 45)}`, { region: '[role="dialog"]', action: "inspect-confirmation" });
    await page.screenshot({ path: join(ownedRoot, `confirmation-${confirmationNumber}-${title.toLowerCase().replace(/[^a-z0-9]+/g, "-")}.png`), fullPage: false });
    await dialog.getByRole("button", { name: label, exact: true }).click();
  }
}
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
const host = spawn(buildProvenance?.toolchain.dotnet.launcher.path ?? "dotnet", ["run", "--no-build", "--project", "tools/identity/ExternalAuthentication.Probe/ExternalAuthentication.Probe.csproj", "--", "--owned-root", ownedRoot, "--ui", ui, "--instance", instance], { cwd: repo, stdio: ["ignore", "pipe", "pipe"] });
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
  const controlKey = settings.uiRecovery ? readFileSync(join(ownedRoot, "ui-control-key.txt"), "utf8") : null;
  uiEvidence = createUiEvidence({ root: ownedRoot, dirty: sourceDirty, origins: [new URL(backend).origin, ...(settings.studioUrl ? [new URL(settings.studioUrl).origin] : [])],
    forbidden: [controlKey, "client secret +/:?&", ...(settings.clientSecretFile ? [readFileSync(join(ownedRoot, settings.clientSecretFile), "utf8")] : [])], sourceSha });
  const capture = (...args) => uiEvidence.capture(...args);
  const componentUrl = view => settings.studioUrl ? `${settings.studioUrl}/security/${view === "access" ? "user-access" : "external-sign-in"}?backend=owned`
    : backend + `/__tests__/live.html?backend=owned${view === "access" ? "&view=access" : ""}`;
  browser = await chromium.launch({ headless: true });
  const admin = await browser.newContext({ viewport: { width: 1440, height: 1100 } });
  const page = await admin.newPage();
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("dialog", dialog => dialog.accept());
  await page.goto(backend + "/_elsa/identity/login?returnUrl=/backend/__probe");
  await expect(page.getByRole("button", { name: /^Continue with Company(?: Preferred)?$/ })).toBeVisible();
  await expect(page.getByRole("button", { name: /^Continue with Partner(?: Preferred)?$/ })).toBeVisible();
  await page.screenshot({ path: join(ownedRoot, "login-chooser.png"), fullPage: false });
  if (settings.uiRecovery) {
    await expect(page.getByRole("button", { name: /^Continue with Company/ })).toContainText("Preferred");
    await page.setViewportSize({ width: 360, height: 844 });
    await expect(page.getByRole("button", { name: /^Continue with Company/ })).toBeInViewport();
    await expect(page.getByRole("button", { name: /^Continue with Partner/ })).toBeInViewport();
    await capture(page, "preferred-chooser-narrow", { region: ".login-methods", blankLogin: true, action: "inspect-all-choices-before-login" });
    await page.setViewportSize({ width: 1440, height: 1100 });
  }
  await page.locator("input[name=username]").fill("admin"); await page.locator("input[name=password]").fill("Password123!");
  await Promise.all([page.waitForURL(url => url.pathname === "/backend/__probe"), page.getByRole("button", { name: "Sign in", exact: true }).click()]);
  const adminSession = await (await admin.request.get(backend + "/_elsa/identity/session")).json();
  async function control(mode, userId = null, sessionId = null) {
    const result = await admin.request.post(backend + "/__probe/ui-control", { headers: { "X-Owned-Probe-Control": controlKey }, data: { mode, userId, sessionId } });
    expect(result.status()).toBe(200); expect((await result.json()).armed).toBe(true);
  }
  const administratorExchanges = [];
  page.on("response", response => {
    if (new URL(response.url()).pathname === "/backend/_elsa/identity/token" && response.status() === 200) {
      administratorExchanges.push(response.json().then(pair => {
        const claims = JSON.parse(Buffer.from(pair.accessToken.split(".")[1], "base64url").toString());
        return claims.sub === adminSession.subject;
      }).catch(() => false));
    }
  });
  async function exchangeCount() {
    await page.waitForLoadState("networkidle");
    expect((await Promise.all(administratorExchanges)).every(Boolean)).toBe(true);
    return administratorExchanges.length;
  }
  await page.goto(componentUrl("connections"));
  await expect(page.getByRole("heading", { name: "External sign-in", level: 2, exact: true })).toBeVisible();
  if (settings.uiRecovery) {
    await page.getByLabel("Find a connection", { exact: true }).fill("no-such-connection");
    await expect(page.getByText("No connections match this search.", { exact: true })).toBeVisible();
    await capture(page, "connection-no-match", { action: "filter-absent-connection" });
    await page.getByLabel("Find a connection", { exact: true }).fill("");
    await expect(page.locator(".external-auth-row").filter({ has: page.getByText("Company", { exact: true }) })).toBeVisible();
    await capture(page, "connection-filter-cleared", { action: "clear-connection-filter" });
  }
  await page.getByRole("button", { name: "Add connection", exact: true }).click();
  await page.getByLabel("Connection key", { exact: true }).focus(); await page.keyboard.press("Tab");
  await expect(page.getByLabel("Display name", { exact: true })).toBeFocused();
  await capture(page, "key-to-name-tab", { action: "tab-from-key-to-name" });
  await page.getByLabel("Connection key", { exact: true }).fill("studio-company"); await page.getByLabel("Display name", { exact: true }).fill("Studio company");
  await page.getByLabel("Issuer URL", { exact: true }).fill(metadata.CompanyIssuer); await page.getByLabel("Client ID", { exact: true }).fill(metadata.ClientId);
  await page.getByLabel("Client secret reference", { exact: true }).fill(metadata.CompanySecret); await page.getByLabel("Secret scope (optional)", { exact: true }).fill("identity/external");
  await page.getByLabel("Unknown identities", { exact: true }).selectOption("jit"); await page.getByLabel("Reader", { exact: true }).check();
  if (settings.uiRecovery) await recovery.prepareInvalidDraft({ page, expect, capture, setMode: connectionMode });
  await page.getByRole("button", { name: "Save draft", exact: true }).click();
  await confirmStudio(page, "Save draft");
  await expect(page.getByRole("heading", { name: "Studio company", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Enable sign-in", exact: true })).toBeDisabled();
  if (settings.uiRecovery) await recovery.recoverFailedTest({ page, expect, capture, metadata, confirm: confirmStudio });
  await page.getByRole("button", { name: "Test configuration", exact: true }).click();
  await expect(page.getByText("Configuration test passed", { exact: true })).toBeVisible();
  if (settings.uiRecovery) await recovery.exercisePreviewDenial({ page, admin, backend, expect, capture, confirm: confirmStudio, control, exchangeCount });
  const beforeExchanges = await exchangeCount();
  const beforePreview = await (await admin.request.get(backend + "/__probe/state")).json();
  await page.getByRole("button", { name: "Preview sign-in", exact: true }).click();
  await confirmStudio(page, "Preview provider sign-in", "Continue to provider");
  await completeProviderLogin(page);
  await expect(page.getByText("Preview complete. No user, role assignment or normal session was changed.", { exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText("Would be created", { exact: true })).toBeVisible();
  const previewAdministratorTokenExchanges = await exchangeCount() - beforeExchanges;
  const afterPreview = await (await admin.request.get(backend + "/__probe/state")).json();
  expect(afterPreview.users).toBe(beforePreview.users); expect(afterPreview.links).toBe(beforePreview.links);
  // The ordinary Studio redirect adapter exchanges the existing administrator cookie on API calls/reload.
  // Account for exactly those access/refresh pairs; the preview protocol may mint no additional credentials.
  expect(afterPreview.tokens).toBe(beforePreview.tokens + previewAdministratorTokenExchanges * 2);
  expect(beforePreview.sessions).toBe(1); expect(afterPreview.sessions).toBe(beforePreview.sessions);
  expect((await (await admin.request.get(backend + "/_elsa/identity/session")).json()).subject).toBe(adminSession.subject);
  expect(new URL(page.url()).searchParams.has("externalPreviewHandle")).toBe(false);
  await expect(page.getByLabel("Reader", { exact: true })).toBeVisible();
  if (settings.uiRecovery) await capture(page, "preview-recovery-succeeded", { action: "successful-preview-after-denial" });
  await page.addScriptTag({ path: join(repo, "Studio/node_modules/axe-core/axe.min.js") });
  const accessibility = await page.evaluate(async () => {
    const result = await globalThis.axe.run(globalThis.document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] } });
    return result.violations.map(violation => ({ id: violation.id, impact: violation.impact,
      nodes: violation.nodes.map(node => ({ target: node.target, explanation: node.failureSummary })) }));
  });
  expect(accessibility).toEqual([]);
  await page.screenshot({ path: join(ownedRoot, "preview-light.png"), fullPage: false });
  async function captureConnectionPanels(mode, size) {
    for (const [name, anchor] of [
      ["provisioning", page.getByLabel("Unknown identities", { exact: true })],
      ["callbacks", page.getByText("Preview callback URL", { exact: true })],
      ["preview", page.getByRole("heading", { name: "Preview result", exact: true })],
      ["actions", page.getByRole("button", { name: "Enable sign-in", exact: true })]
    ]) {
      await anchor.scrollIntoViewIfNeeded(); await expect(anchor).toBeVisible();
      await page.screenshot({ path: join(ownedRoot, `connection-${name}-${mode}-${size}.png`), fullPage: false });
    }
  }
  async function connectionMode(mode) {
    if (!settings.studioUrl) return;
    const label = mode === "dark" ? "Dark" : "Light";
    const quickMode = page.getByRole("radiogroup", { name: "Colour mode", exact: true }).getByRole("radio", { name: label, exact: true });
    if (await quickMode.isVisible()) await quickMode.click();
    else {
      // Narrow Studio hides its quick toggle; use the same public appearance menu as a person.
      const picker = page.getByRole("button", { name: "Select theme", exact: true });
      await picker.click();
      await page.getByRole("group", { name: "Appearance", exact: true }).getByRole("menuitemradio", { name: label, exact: true }).click();
      await page.keyboard.press("Escape");
      await expect(picker).toBeFocused();
    }
    await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", mode);
    await page.evaluate(() => Promise.all(globalThis.document.getAnimations().filter(animation => Number.isFinite(animation.effect?.getComputedTiming().iterations)).map(animation => animation.finished.catch(() => {}))));
  }
  await captureConnectionPanels("light", "desktop");
  let connectionDarkAccessibility = null;
  if (settings.studioUrl) {
    await connectionMode("dark"); await captureConnectionPanels("dark", "desktop");
    connectionDarkAccessibility = await page.evaluate(async () => (await globalThis.axe.run(globalThis.document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] } })).violations.map(item => ({ id: item.id, impact: item.impact, targets: item.nodes.map(node => node.target) })));
    expect(connectionDarkAccessibility).toEqual([]);
    await connectionMode("light");
  }
  await page.setViewportSize({ width: 360, height: 844 });
  expect(await page.evaluate(() => globalThis.document.documentElement.scrollWidth <= globalThis.innerWidth)).toBe(true);
  await captureConnectionPanels("light", "narrow");
  if (settings.studioUrl) {
    await page.setViewportSize({ width: 1440, height: 1100 });
    await connectionMode("dark");
    await page.setViewportSize({ width: 360, height: 844 });
    await captureConnectionPanels("dark", "narrow");
    await page.setViewportSize({ width: 1440, height: 1100 });
    await connectionMode("light");
  }
  await page.setViewportSize({ width: 1440, height: 1100 });
  if (settings.studioUrl) {
    const beforeCancel = await (await admin.request.get(backend + "/_elsa/identity/external-connections")).json();
    const enable = page.getByRole("button", { name: "Enable sign-in", exact: true });
    await enable.click(); await capture(page, "enable-before-escape", { region: '[role="dialog"]', action: "open-enable-confirmation" });
    await page.keyboard.press("Escape"); await expect(page.getByRole("dialog", { name: "Enable sign-in", exact: true })).toHaveCount(0);
    await expect(enable).toBeFocused(); await capture(page, "enable-escape-focus", { action: "escape-return-to-enable" });
    await enable.click();
    await page.getByRole("dialog", { name: "Enable sign-in", exact: true }).getByRole("button", { name: "Cancel", exact: true }).click();
    await expect(enable).toBeFocused(); await expect(page.getByLabel("Reader", { exact: true })).toBeChecked();
    const afterCancel = await (await admin.request.get(backend + "/_elsa/identity/external-connections")).json();
    expect(afterCancel.revision).toBe(beforeCancel.revision);
    expect(afterCancel.connections.map(item => item.document)).toEqual(beforeCancel.connections.map(item => item.document));
    await capture(page, "enable-cancel-focus", { action: "cancel-enable-preserve-form" });
  }
  await page.getByRole("button", { name: "Enable sign-in", exact: true }).click();
  await confirmStudio(page, "Enable sign-in");
  await expect(page.getByRole("button", { name: "Disable sign-in", exact: true })).toBeVisible();
  await page.setViewportSize({ width: 360, height: 844 });
  expect(await page.evaluate(() => globalThis.document.documentElement.scrollWidth <= globalThis.window.innerWidth)).toBe(true);
  await page.screenshot({ path: join(ownedRoot, "connection-narrow.png"), fullPage: false });
  const user = await browser.newContext(); const signIn = await user.newPage();
  await signIn.goto(backend + "/_elsa/identity/login?returnUrl=/backend/resource");
  await signIn.getByRole("button", { name: /Studio company/ }).click();
  await completeProviderLogin(signIn);
  await expect(signIn).toHaveURL(backend + "/resource");
  const session = await (await user.request.get(backend + "/_elsa/identity/session")).json();
  expect(session.status).toBe("authenticated"); expect(session.roles).toContain("reader"); expect(session.permissions).not.toContain("*");
  expect(session.subject).not.toBe(adminSession.subject);
  expect((await (await admin.request.get(backend + "/_elsa/identity/session")).json()).subject).toBe(adminSession.subject);
  // The real user-access component uses only its public bounded API and protected commands.
  await page.setViewportSize({ width: 1440, height: 1100 });
  await page.goto(componentUrl("access"));
  await expect(page.getByRole("heading", { name: "User access", level: 2, exact: true })).toBeVisible();
  const directory = await (await admin.request.get(backend + "/_elsa/identity/external-connections/users?take=25")).json();
  const target = directory.items.find(item => item.id === session.subject); expect(target).toBeTruthy();
  if (settings.uiRecovery) {
    await page.getByLabel("Exact username or email").fill("no-such-fixture-user"); await page.getByRole("button", { name: "Find users", exact: true }).click();
    await expect(page.getByText("No users match this search.", { exact: true })).toBeVisible();
    await capture(page, "user-no-match", { action: "search-absent-fixture-user" });
  }
  await page.getByLabel("Exact username or email").fill(target.userName);
  await page.getByRole("button", { name: "Find users", exact: true }).click();
  const row = page.locator(".external-auth-row").filter({ hasText: target.userName });
  await expect(row).toHaveCount(1); await row.click();
  await expect(page.getByRole("heading", { name: "Identity links", exact: true })).toBeVisible();
  if (settings.uiRecovery) await capture(page, "known-user-search-recovered", { action: "find-known-user-after-no-match" });
  await expect(page.getByRole("button", { name: "Remove link", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Remove link", exact: true }).click();
  await confirmStudio(page, "Remove link");
  await expect(page.locator(".external-auth-shell:visible").getByRole("alert")).toContainText("Keep a usable sign-in method for this user and an administrator");
  await capture(page, "last-login-path-refused", { action: "refuse-final-link-removal" });
  await expect(page.getByRole("button", { name: "Remove link", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Refresh user access", exact: true }).click();
  await expect(page.getByRole("button", { name: "Remove link", exact: true })).toBeEnabled();
  const currentLinks = await (await admin.request.get(backend + `/_elsa/identity/external-connections/users/${encodeURIComponent(session.subject)}/links`)).json();
  const subject = currentLinks.items[0].subject;
  await page.getByLabel("Connection", { exact: true }).selectOption("deployment:partner");
  await page.getByLabel("External subject", { exact: true }).fill(subject);
  await page.getByRole("button", { name: "Attach identity", exact: true }).click();
  await confirmStudio(page, "Attach identity");
  await expect(page.locator(".external-auth-notice")).toContainText("Identity attached. Saved Elsa roles are unchanged.");
  // Establish an actual normal sign-in through the newly attached provider, without a synthetic witness.
  const csrf = await (await user.request.get(backend + "/identity/external/antiforgery")).json();
  await signIn.evaluate(({ action, field, token }) => {
    const form = globalThis.document.createElement("form"); form.method = "post"; form.action = action;
    for (const [name, value] of Object.entries({ [field]: token, returnUrl: "/backend/resource" })) {
      const input = globalThis.document.createElement("input"); input.type = "hidden"; input.name = name; input.value = value; form.appendChild(input);
    }
    globalThis.document.body.appendChild(form); form.submit();
  }, { action: backend + "/identity/external/signin/partner", field: csrf.formFieldName, token: csrf.requestToken });
  await completeProviderLogin(signIn);
  await expect.poll(async () => (await (await user.request.get(backend + "/_elsa/identity/session")).json()).provider).toBe("external:partner");
  await expect(signIn).toHaveURL(backend + "/resource");
  const partnerSession = await (await user.request.get(backend + "/_elsa/identity/session")).json(); expect(partnerSession.subject).toBe(session.subject);
  await page.getByRole("button", { name: "Refresh user access", exact: true }).click();
  const companyCard = page.getByRole("region", { name: "Identity links", exact: true }).locator("article").filter({ has: page.getByRole("heading", { name: "Studio company", exact: true }) });
  await expect(companyCard.getByRole("button", { name: "Remove link", exact: true })).toBeEnabled();
  await companyCard.getByRole("button", { name: "Remove link", exact: true }).click();
  await confirmStudio(page, "Remove link");
  await expect(page.locator(".external-auth-notice")).toContainText("Identity link removed.");
  await expect(companyCard).toHaveCount(0);
  await capture(page, "proved-sibling-safe-removal", { action: "remove-link-after-proving-sibling" });
  expect((await (await user.request.get(backend + "/_elsa/identity/session")).json()).subject).toBe(session.subject);
  // A reader can use the directory, while link/session inspection remains explicitly denied.
  const readerPage = await user.newPage(); await readerPage.goto(componentUrl("access"));
  await expect(readerPage.getByRole("heading", { name: "User access", level: 2, exact: true })).toBeVisible();
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
  await confirmStudio(page, "Revoke session");
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
  let errorRecovery = null;
  if (settings.uiRecovery) {
    await recovery.exerciseAccessRecovery({ page, admin, browser, backend, componentUrl, target, partnerSession, subject, expect, capture, confirm: confirmStudio, control, setMode: connectionMode });
    errorRecovery = { ownershipConflict: true, beforeWriteFailureThenRetry: true, lostAcknowledgementRefreshAndIdempotence: true };
  }
  let retainedDraft = null;
  if (settings.studioUrl) {
    await page.getByLabel("External subject", { exact: true }).fill("unattached-retained-proof");
    await capture(page, "link-draft-before-tab", { action: "edit-fixture-link-draft" });
    await page.getByRole("tab", { name: "External sign-in", exact: true }).click();
    await expect(page.getByRole("heading", { name: "External sign-in", level: 2, exact: true })).toBeVisible();
    await page.getByRole("tab", { name: /^User access/ }).click();
    await expect(page.getByLabel("External subject", { exact: true })).toHaveValue("unattached-retained-proof");
    await capture(page, "link-draft-after-tab", { action: "return-to-retained-link-draft" });
    await page.getByRole("button", { name: "Discard link draft", exact: true }).click();
    await expect(page.getByText("Loading user access…", { exact: true })).toHaveCount(0);
    retainedDraft = true;
  }
  await page.addScriptTag({ path: join(repo, "Studio/node_modules/axe-core/axe.min.js") });
  const accessAccessibility = await page.evaluate(async () => {
    const result = await globalThis.axe.run(globalThis.document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] } });
    return result.violations.map(item => ({ id: item.id, impact: item.impact, nodes: item.nodes.map(node => ({ target: node.target, explanation: node.failureSummary })) }));
  });
  expect(accessAccessibility).toEqual([]);
  await page.getByRole("heading", { name: "User access", level: 2, exact: true }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: join(ownedRoot, "user-access-light.png"), fullPage: !settings.studioUrl });
  let darkAccessibility = null;
  if (settings.studioUrl) {
    const modes = page.getByRole("radiogroup", { name: "Colour mode", exact: true });
    await modes.getByRole("radio", { name: "Light", exact: true }).focus(); await page.keyboard.press("ArrowRight");
    await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "dark");
    await page.evaluate(() => Promise.all(globalThis.document.getAnimations()
      .filter(animation => Number.isFinite(animation.effect?.getComputedTiming().iterations))
      .map(animation => animation.finished.catch(() => {}))));
    darkAccessibility = await page.evaluate(async () => {
      const result = await globalThis.axe.run(globalThis.document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] } });
      return result.violations.map(item => ({ id: item.id, impact: item.impact, nodes: item.nodes.map(node => ({ target: node.target, explanation: node.failureSummary })) }));
    });
    expect(darkAccessibility).toEqual([]);
    await page.getByRole("heading", { name: "User access", level: 2, exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: join(ownedRoot, "user-access-dark.png"), fullPage: false });
    await page.keyboard.press("ArrowLeft"); await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "light");
    await page.evaluate(() => Promise.all(globalThis.document.getAnimations()
      .filter(animation => Number.isFinite(animation.effect?.getComputedTiming().iterations))
      .map(animation => animation.finished.catch(() => {}))));
  }
  await page.setViewportSize({ width: 360, height: 844 });
  expect(await page.evaluate(() => globalThis.document.documentElement.scrollWidth <= globalThis.window.innerWidth)).toBe(true);
  await page.getByRole("heading", { name: "User access", level: 2, exact: true }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: join(ownedRoot, "user-access-narrow.png"), fullPage: !settings.studioUrl });
  if (settings.studioUrl) {
    const lastAction = page.getByRole("region", { name: "Sessions", exact: true }).getByRole("button", { name: "Revoke session", exact: true }).last();
    await lastAction.scrollIntoViewIfNeeded(); await expect(lastAction).toBeInViewport();
    await page.screenshot({ path: join(ownedRoot, "user-access-narrow-sessions.png"), fullPage: false });
  }
  let connectionLifecycle = null;
  if (settings.studioUrl && settings.provider !== "Keycloak") {
    await page.setViewportSize({ width: 1440, height: 1100 });
    await page.getByRole("tab", { name: "External sign-in", exact: true }).click();
    await page.locator(".external-auth-list .external-auth-row").filter({ has: page.getByText("Studio company", { exact: true }) }).click();
    if (settings.uiRecovery) await recovery.exerciseConnectionConflict({ page, admin, componentUrl, expect, capture, confirm: confirmStudio });
    else {
      await page.getByLabel("Display name", { exact: true }).fill("Studio company edited");
      await page.getByRole("button", { name: "Save changes", exact: true }).click(); await confirmStudio(page, "Save changes");
    }
    await expect(page.getByRole("heading", { name: "Studio company edited", exact: true })).toBeVisible();
    for (const [label, title] of [["Disable sign-in", "Disable sign-in"], ["Archive", "Archive connection"], ["Restore", "Restore connection"], ["Remove", "Remove Studio connection"]]) {
      await page.getByRole("button", { name: label, exact: true }).click(); await confirmStudio(page, title);
      await expect(page.getByText(title + "…", { exact: true })).toHaveCount(0);
      if (label === "Archive") await expect(page.getByRole("button", { name: "Restore", exact: true })).toBeVisible();
      if (label === "Restore") await expect(page.getByRole("button", { name: "Archive", exact: true })).toBeVisible();
    }
    await expect(page.locator(".external-auth-list .external-auth-row").filter({ hasText: "Studio company edited" })).toHaveCount(0);
    await page.locator(".external-auth-list .external-auth-row").filter({ has: page.getByText("Company", { exact: true }) }).click();
    await page.getByRole("button", { name: "Prepare full override", exact: true }).click();
    await page.getByLabel("Unknown identities", { exact: true }).selectOption("jit"); await page.getByLabel("Reader", { exact: true }).check();
    await page.getByRole("button", { name: "Save draft", exact: true }).click(); await confirmStudio(page, "Save draft");
    await expect(page.getByRole("button", { name: "Test configuration", exact: true })).toBeEnabled();
    const beforePromotion = await (await admin.request.get(backend + "/_elsa/identity/external-connections")).json();
    expect(beforePromotion.connections.find(item => item.source === 0 && item.document.key === "company").effective).toBe(true);
    expect(beforePromotion.connections.find(item => item.source === 1 && item.document.key === "company").effective).toBe(false);
    await page.getByRole("button", { name: "Test configuration", exact: true }).click();
    await expect(page.getByText("Configuration test passed", { exact: true })).toBeVisible();
    const beforeOverrideExchanges = await exchangeCount(); const beforeOverridePreview = await (await admin.request.get(backend + "/__probe/state")).json();
    await page.getByRole("button", { name: "Preview sign-in", exact: true }).click(); await confirmStudio(page, "Preview provider sign-in", "Continue to provider");
    await expect(page.getByText("Would be created", { exact: true })).toBeVisible();
    const overrideAdministratorExchanges = await exchangeCount() - beforeOverrideExchanges;
    const afterOverridePreview = await (await admin.request.get(backend + "/__probe/state")).json();
    for (const field of ["users", "links", "sessions"]) expect(afterOverridePreview[field]).toBe(beforeOverridePreview[field]);
    expect(afterOverridePreview.tokens).toBe(beforeOverridePreview.tokens + overrideAdministratorExchanges * 2);
    await page.getByRole("button", { name: "Use as override", exact: true }).click(); await confirmStudio(page, "Use as override");
    await expect(page.getByRole("button", { name: "Disable sign-in", exact: true })).toBeVisible();
    const promoted = await (await admin.request.get(backend + "/_elsa/identity/external-connections")).json();
    const overrideId = promoted.connections.find(item => item.source === 1 && item.document.key === "company").document.id;
    expect(promoted.connections.find(item => item.source === 0 && item.document.key === "company").effective).toBe(false);
    for (const [label, title, baselineEffective] of [["Disable sign-in", "Disable sign-in", false], ["Archive", "Archive connection", true], ["Restore", "Restore connection", false], ["Remove", "Remove Studio connection", true]]) {
      await page.getByRole("button", { name: label, exact: true }).click(); await confirmStudio(page, title);
      await expect.poll(async () => {
        const state = await (await admin.request.get(backend + "/_elsa/identity/external-connections")).json();
        const stored = state.connections.find(item => item.document.id === overrideId);
        if (label === "Remove" && stored) return null;
        if (label === "Archive" && !stored?.document.archivedAt) return null;
        if (label === "Restore" && stored?.document.archivedAt) return null;
        if (label === "Disable sign-in" && stored?.document.enabled) return null;
        return state.connections.find(item => item.source === 0 && item.document.key === "company").effective;
      }).toBe(baselineEffective);
      await expect(page.getByRole("dialog")).toHaveCount(0);
      await expect(page.getByText("Connection updated. The sign-in chooser uses the current effective configuration.", { exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: "Refresh", exact: true })).toBeEnabled();
    }
    connectionLifecycle = { edited: true, retiredAndRestored: true, draftDoesNotShadow: true, explicitPromotion: true,
      disabledOverrideShadows: true, archiveRevealsBaseline: true, restoreShadowsAgain: true, removalRevealsBaseline: true,
      previewAdministratorTokenExchanges: overrideAdministratorExchanges };
    await expect(page.getByRole("heading", { name: "Choose a connection", exact: true })).toBeVisible();
    await expect(page.getByRole("region", { name: "Sign-in connections", exact: true }).locator("li")).toHaveCount(2);
    await page.screenshot({ path: join(ownedRoot, "connection-lifecycle.png"), fullPage: false });
  }
  const access = { targetRolesPreserved: partnerSession.roles.includes("reader"), finalPathRefused: true, attachedExistingUser: true,
    provedSiblingThenRemoved: true, readerPanelsDenied: true, selectedRevocation: { cookie: true, access: true, refresh: true }, retainedDraft,
    accessibility: accessAccessibility, darkAccessibility };
  if (settings.uiRecovery) await recovery.exerciseSelfRevocation({ page, admin, backend, componentUrl, adminSession, expect, capture, confirm: confirmStudio });
  expect(errors).toEqual([]);
  if (settings.studioUrl) await verifyUiBuild();
  const result = { passed: true, ownedRoot, beforePreview, afterPreview, previewAdministratorTokenExchanges, ordinaryUser: { roles: session.roles, permissions: session.permissions },
    accessibility, connectionDarkAccessibility, access, connectionLifecycle, errorRecovery, uiEvidence: uiEvidence.result(settings.uiRecovery ? [
      "preferred-chooser-narrow", "key-to-name-tab", "missing-name-narrow-dark", "invalid-key", "configuration-test-failed", "corrected-draft-enter-reopen",
      "preview-provider-denied", "preview-recovery-succeeded", "connection-no-match", "connection-filter-cleared", "enable-before-escape", "enable-escape-focus", "enable-cancel-focus",
      "user-no-match", "known-user-search-recovered", "last-login-path-refused", "proved-sibling-safe-removal", "pending-user-empty-access", "identity-ownership-conflict", "revocation-before-write-failure",
      "revocation-before-write-recovered", "revocation-after-commit-failure", "revocation-after-commit-recovered", "link-draft-before-tab", "link-draft-after-tab",
      "stale-draft-preserved", "discard-conflicting-draft", "discard-cancel-preserves-draft", "conflict-recovered-save-reopen", "self-session-revoked", "self-session-reauthenticated"
    ] : ["key-to-name-tab", "last-login-path-refused"]), provider: settings.provider === "Keycloak" ? "Keycloak 26.8.1, two independent owned providers" : "controlled owned HTTP OIDC fixture", store: "native SQLite", browser: "Chromium",
    fullStudioHost: Boolean(settings.studioUrl), buildProvenance,
    limitations: [...(settings.provider === "Keycloak" ? [] : ["not an external IdP product"]), ...(settings.studioUrl ? [] : ["not a full Studio host"]), "not server-database or separate-node qualification", "custom evidence is selected ARIA and named actions; native replay remains unverified"] };
  writeFileSync(join(ownedRoot, "browser-receipt.json"), JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result));
} catch (error) {
  if (browser) {
    for (const [index, page] of browser.contexts().flatMap(context => context.pages()).entries()) {
      await uiEvidence?.capture(page, `failure-${index}`, { action: "failure-checkpoint" }).catch(() => {
        writeFileSync(join(ownedRoot, `failure-${index}.json`), JSON.stringify({ capture: "withheld-unsafe-or-unavailable" }));
      });
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
