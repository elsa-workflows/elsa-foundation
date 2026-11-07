import { execFileSync, spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { closeSync, openSync, readFileSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { verifyUiBuild } from "./ui-build-provenance.mjs";

const repo = resolve(import.meta.dirname, "../../..");
const { createUiEvidence } = await import(pathToFileURL(join(repo, "tools/identity/ExternalAuthentication.Probe/ui-evidence.mjs")));
const sourceSha = execFileSync("git", ["rev-parse", "HEAD"], { cwd: repo, encoding: "utf8" }).trim();
const sourceDirty = Boolean(execFileSync("git", ["status", "--porcelain"], { cwd: repo, encoding: "utf8" }).trim());
const root = process.env.ELSA_EXTERNAL_AUTH_PROBE_ROOT;
if (!root || readFileSync(join(root, ".elsa-3579-owned-probe"), "utf8") !== "elsa-external-authentication-probe/v1") throw new Error("An owned Studio runner root is required.");
const settings = JSON.parse(readFileSync(join(root, "probe-settings.json"), "utf8"));
const buildProvenance = await verifyUiBuild();
const { chromium, expect: baseExpect } = await import(pathToFileURL(join(repo, "Studio/node_modules/@playwright/test/index.mjs")));
const expect = baseExpect.configure({ timeout: 30_000 });
const instance = randomUUID(); const log = openSync(join(root, "host.log"), "w", 0o600);
const host = spawn(buildProvenance.toolchain.dotnet.launcher.path, [join(repo, "tools/identity/ExternalAuthentication.Probe/bin/Debug/net10.0/ExternalAuthentication.Probe.dll"),
  "--owned-root", root, "--instance", instance], { cwd: repo, stdio: ["ignore", log, log] });
let browser; let uiEvidence;
try {
  const deadline = Date.now() + 90_000;
  while (true) {
    try { if (JSON.parse(readFileSync(join(root, "probe.json"), "utf8")).Instance === instance) break; } catch { /* incomplete metadata */ }
    if (host.exitCode !== null || host.signalCode !== null || Date.now() >= deadline) throw new Error("Owned mode backend did not become ready; inspect host.log.");
    await new Promise(done => setTimeout(done, 100));
  }
  const backend = JSON.parse(readFileSync(join(root, "probe.json"), "utf8")).Backend;
  uiEvidence = createUiEvidence({ root, sourceSha, dirty: sourceDirty, origins: [new URL(settings.studioUrl).origin], forbidden: ["client secret +/:?&"] });

  browser = await chromium.launch({ headless: true }); const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage(); const errors = []; const writes = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("request", request => { if (request.url().startsWith(backend + "/_elsa/identity/external-connections") && request.method() !== "GET") writes.push(request.method()); });
  await page.goto(backend + "/_elsa/identity/login?returnUrl=/backend/resource");
  await page.getByLabel("Username", { exact: true }).fill("admin"); await page.getByLabel("Password", { exact: true }).fill("Password123!");
  await Promise.all([page.waitForURL(backend + "/resource"), page.getByRole("button", { name: "Sign in", exact: true }).click()]);
  await page.goto(settings.studioUrl + "/security/external-sign-in?backend=owned");
  const profile = settings.uiProfile;
  if (profile === "empty") {
    await expect(page.getByText("No external sign-in connections yet. Add one to prepare a disabled draft.", { exact: true })).toBeVisible();
    await uiEvidence.capture(page, "empty-managed-catalog", { action: "inspect-empty-connection-list" });
    await page.getByRole("button", { name: "Add connection", exact: true }).click();
    await page.getByLabel("Connection key", { exact: true }).fill("first-fixture"); await page.getByLabel("Display name", { exact: true }).fill("First fixture");
    const metadata = JSON.parse(readFileSync(join(root, "probe.json"), "utf8"));
    await page.getByLabel("Issuer URL", { exact: true }).fill(metadata.CompanyIssuer); await page.getByLabel("Client ID", { exact: true }).fill(metadata.ClientId);
    await page.getByLabel("Client secret reference", { exact: true }).fill(metadata.CompanySecret); await page.getByLabel("Secret scope (optional)", { exact: true }).fill("identity/external");
    await page.getByRole("button", { name: "Save draft", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Save draft", exact: true });
    await expect(dialog).toBeVisible();
    await uiEvidence.capture(page, "first-draft-confirmation", { region: '[role="dialog"]', action: "save-first-disabled-draft" });
    await dialog.getByRole("button", { name: "Save draft", exact: true }).click();
    const row = page.locator(".external-auth-row").filter({ hasText: "First fixture" }); await expect(row).toBeEnabled(); await row.focus(); await page.keyboard.press("Enter");
    await expect(page.getByLabel("Connection key", { exact: true })).toHaveValue("first-fixture");
    await expect(page.getByRole("button", { name: "Enable sign-in", exact: true })).toBeDisabled();
    await uiEvidence.capture(page, "first-disabled-draft-reopened", { action: "reopen-first-disabled-draft" });
    await page.goto(settings.studioUrl + "/security/user-access?backend=owned");
    await page.getByLabel("Exact username or email").fill("pending-fixture-user"); await page.getByRole("button", { name: "Find users", exact: true }).click();
    await page.locator(".external-auth-row").filter({ hasText: "pending-fixture-user" }).click();
    await expect(page.getByText("No identities are linked to this user.", { exact: true })).toBeVisible();
    await expect(page.getByText("No retained sessions for this user.", { exact: true })).toBeVisible();
    await uiEvidence.capture(page, "empty-user-links-sessions", { action: "inspect-pending-user-without-access" });
  } else if (profile === "missing-api") {
    await expect(page.getByRole("status").filter({ hasText: "does not support elsa.api.identity.external-authentication" })).toBeVisible();
    await expect(page.locator(".external-auth-shell")).toHaveCount(0);
    expect((await context.request.get(backend + "/_elsa/identity/external-connections")).status()).toBe(404);
  } else {
    await expect(page.getByRole("heading", { name: "External sign-in", level: 2, exact: true })).toBeVisible();
    if (profile === "denied") {
      await expect(page.locator(".external-auth-shell:visible").getByRole("alert")).toContainText("permission");
      await expect(page.getByRole("button", { name: "Add connection", exact: true })).toBeDisabled();
    } else {
      await expect(page.locator(".external-auth-list .external-auth-row").filter({ has: page.getByText("Company", { exact: true }) })).toBeVisible();
      if (profile === "configuration") {
        await expect(page.getByText("This deployment manages connections in configuration. Studio changes are disabled.", { exact: true })).toBeVisible();
        await expect(page.getByRole("button", { name: "Add connection", exact: true })).toBeDisabled();
      }
      if (profile === "provider-reader") {
        await expect(page.getByText("You have read-only access to these connections.", { exact: true })).toBeVisible();
        await expect(page.getByRole("button", { name: "Add connection", exact: true })).toBeDisabled();
      }
      await page.locator(".external-auth-list .external-auth-row").filter({ has: page.getByText("Company", { exact: true }) }).click();
      await expect(page.getByLabel("Display name", { exact: true })).toBeDisabled();
      if (profile === "provider-reader") {
        await expect(page.getByRole("heading", { name: "Current configuration needs attention", exact: true })).toBeVisible();
        await expect(page.getByText("The referenced secret could not be resolved. Check its name, scope, type and status.", { exact: true })).toBeVisible();
        await expect(page.getByRole("button", { name: "Test configuration", exact: true })).toHaveCount(0);
      }
    }
    await page.screenshot({ path: join(root, "mode-connection.png"), fullPage: false });
    await page.goto(settings.studioUrl + "/security/user-access?backend=owned");
    await expect(page.getByRole("heading", { name: "User access", level: 2, exact: true })).toBeVisible();
    await page.locator(".external-auth-list .external-auth-row").filter({ hasText: "admin" }).click();
    const metadataResponse = await context.request.get(backend + "/_elsa/identity/external-connections/users?take=25"); expect(metadataResponse.status()).toBe(200);
    const metadata = await metadataResponse.json(); expect(metadata.ownershipMode).toBe(settings.ownershipMode ?? "FoundationOwned");
    if (profile === "provider-reader" || profile === "denied") {
      await expect(page.getByText("You do not have permission to read identity links.", { exact: true })).toBeVisible();
      await expect(page.getByText("You do not have permission to read sessions.", { exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: "Attach identity", exact: true })).toHaveCount(0);
    }
    if (profile === "external-owned" || profile === "hybrid") {
      expect(metadata.canManageUsers).toBe(false);
      await expect(page.getByText("The configured identity authority owns these users. Link changes are read-only.", { exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: "Attach identity", exact: true })).toHaveCount(0);
      const action = page.getByRole("button", { name: "Revoke session", exact: true });
      if (profile === "external-owned") { expect(metadata.canRevokeTokens).toBe(false); await expect(action).toBeDisabled(); }
      else { expect(metadata.canRevokeTokens).toBe(true); await expect(action).toBeEnabled(); }
    }
    if (profile === "missing-sessions") {
      expect(metadata.sessionAdministrationUnavailableReason).toBe("session-administration-unavailable");
      await expect(page.getByRole("region", { name: "Sessions", exact: true })).toContainText("Session administration is unavailable");
      await expect(page.getByRole("button", { name: "Revoke session", exact: true })).toHaveCount(0);
    }
    await expect(page.getByText("Loading user access…", { exact: true })).toHaveCount(0);
  }
  await page.addScriptTag({ path: join(repo, "Studio/node_modules/axe-core/axe.min.js") });
  async function accessibility() {
    return page.evaluate(async () => (await globalThis.axe.run(globalThis.document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] } })).violations.map(item => ({ id: item.id, impact: item.impact, targets: item.nodes.map(node => node.target) })));
  }
  const light = await accessibility(); expect(light).toEqual([]);
  await page.screenshot({ path: join(root, "mode-light.png"), fullPage: false });
  const modes = page.getByRole("radiogroup", { name: "Colour mode", exact: true });
  await modes.getByRole("radio", { name: "Light", exact: true }).focus(); await page.keyboard.press("ArrowRight");
  await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "dark");
  await page.evaluate(() => Promise.all(globalThis.document.getAnimations().filter(animation => Number.isFinite(animation.effect?.getComputedTiming().iterations)).map(animation => animation.finished.catch(() => {}))));
  const dark = await accessibility(); expect(dark).toEqual([]);
  await page.screenshot({ path: join(root, "mode-dark.png"), fullPage: false });
  await page.setViewportSize({ width: 360, height: 844 });
  expect(await page.evaluate(() => globalThis.document.documentElement.scrollWidth <= globalThis.innerWidth)).toBe(true);
  await page.screenshot({ path: join(root, "mode-narrow.png"), fullPage: false });
  await uiEvidence.capture(page, "profile-narrow-dark", { region: "body", action: "inspect-profile-at-360" });
  expect(writes).toEqual(profile === "empty" ? ["POST", "POST"] : []); expect(errors).toEqual([]);
  await verifyUiBuild();
  const result = { passed: true, ownedRoot: root, profile, fullStudioHost: true, browser: "Chromium", store: "native SQLite", buildProvenance,
    noExternalManagementWrites: profile !== "empty", uiEvidence: uiEvidence.result(profile === "empty" ? ["empty-managed-catalog", "first-draft-confirmation", "first-disabled-draft-reopened", "empty-user-links-sessions", "profile-narrow-dark"] : ["profile-narrow-dark"]), accessibility: { light, dark }, narrowNoOverflow: true,
    limitations: ["representative ownership/permission/installation UI profile; full provider and process/store journeys are recorded separately"] };
  writeFileSync(join(root, "browser-receipt.json"), JSON.stringify(result, null, 2)); console.log(JSON.stringify(result));
} catch (error) {
  if (browser) for (const page of browser.contexts().flatMap(context => context.pages())) {
    await uiEvidence?.capture(page, "mode-failure", { region: "body", action: "failure-checkpoint" }).catch(() => {
      writeFileSync(join(root, "mode-failure.json"), JSON.stringify({ capture: "withheld-unsafe-or-unavailable" }));
    });
  }
  throw error;
} finally {
  if (browser) await browser.close();
  if (host.exitCode === null && host.signalCode === null) {
    const stopped = new Promise(done => host.once("exit", done)); host.kill("SIGTERM");
    await Promise.race([stopped, new Promise(done => setTimeout(done, 5000))]);
    if (host.exitCode === null && host.signalCode === null) { host.kill("SIGKILL"); await stopped; }
  }
  closeSync(log);
}
