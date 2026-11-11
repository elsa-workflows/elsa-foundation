import { tabbedSecretsJourney } from "./tabbedSecretsJourney";
import { templateToastJourney } from "./templateToastJourney";
import { executableDesignerJourney } from "./executableDesignerJourney";
import { writeFileSync } from "node:fs";
import { expect, test, type APIRequestContext, type Page, type TestInfo } from "@playwright/test";
import { userTaskAuthoringJourney } from "./userTaskAuthoringJourney";
import { userTasksJourney } from "./userTasksJourney";
import { decisionsJourney } from "./decisionsJourney";
import { chooseDataJourney } from "./chooseDataJourney";

async function capture(page: Page, info: TestInfo, name: string) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const screenshot = info.outputPath(`${name}.png`);
  await page.screenshot({ path: screenshot, fullPage: true, animations: "disabled" });
  await info.attach(`${name}.png`, { path: screenshot, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`);
  writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}

async function signIn(page: Page) {
  await page.goto("/workflows/definitions");
  const form = page.locator('form[action="/_elsa/identity/login"]');
  await form.getByLabel(/username/i).fill("admin");
  await form.getByLabel(/password/i).fill("Password123!");
  await form.getByRole("button", { name: /sign in/i }).click();
  await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible({ timeout: 60_000 });
}

const browserErrors = new WeakMap<Page, string[]>();
type CreatedWorkflow = { url: string; authorization?: string; id: string };
const createdWorkflows = new WeakMap<Page, CreatedWorkflow[]>();
const ownedCleanups = new WeakMap<Page, Array<() => Promise<void>>>();

test("user tasks inbox and response", async ({ page }, info) => userTasksJourney(page, info, capture));
test("user task authoring and workflow result", async ({ page }, info) => {
  const creations: CreatedWorkflow[] = [];
  createdWorkflows.set(page, creations);
  await userTaskAuthoringJourney(page, info, capture, id => {
    creations.push({ url: `${process.env.UX_BACKEND_URL}/design/workflows/definitions`, id });
  }, cleanup => { ownedCleanups.get(page)!.push(cleanup); });
});
test("business decisions authoring and explanations", async ({ page }, info) => decisionsJourney(page, info, capture));

test.afterEach(async ({ page, playwright }, info) => {
  const creations = createdWorkflows.get(page) ?? [];
  const failures: Error[] = [];
  const outcomes: Array<{ id: string; url: string; status?: number; error?: string }> = [];
  let cleanup: APIRequestContext | undefined;
  // Journeys that create their own records register a bounded cleanup; each one runs even if an earlier one fails.
  // They run first, so an open task is closed while its workflow definition still exists.
  for (const ownedCleanup of ownedCleanups.get(page) ?? []) {
    try { await ownedCleanup(); }
    catch (error) { failures.push(error instanceof Error ? error : new Error(String(error))); }
  }
  // This hook and request context outlive a timed-out body. Delete only registered, case-owned IDs.
  try {
    if (creations.length) {
      cleanup = await playwright.request.newContext({ storageState: await page.context().storageState(), timeout: 5_000 });
      for (const creation of creations) {
        const url = `${creation.url}/${encodeURIComponent(creation.id)}`;
        try {
          const removed = await cleanup.delete(url, {
            timeout: 5_000,
            headers: creation.authorization ? { Authorization: creation.authorization } : undefined
          });
          outcomes.push({ id: creation.id, url, status: removed.status() });
          if (!removed.ok()) failures.push(new Error(`Cleanup for owned workflow ${creation.id} returned HTTP ${removed.status()}`));
        } catch (error) {
          const details = String(error);
          const message = creation.authorization ? details.replaceAll(creation.authorization, "[redacted]") : details;
          outcomes.push({ id: creation.id, url, error: message });
          failures.push(new Error(`Cleanup for owned workflow ${creation.id} failed: ${message}`, { cause: error }));
        }
      }
    }
  } catch (error) {
    failures.push(new Error(`Could not initialize cleanup for owned workflows ${creations.map(item => item.id).join(", ")}`, { cause: error }));
  } finally {
    if (cleanup) {
      try { await cleanup.dispose(); }
      catch (error) { failures.push(new Error("Could not dispose the workflow cleanup request context", { cause: error })); }
    }
  }
  if (creations.length) {
    try {
      await info.attach("workflow-cleanup.json", { body: JSON.stringify({ requestTimeoutMs: 5_000, ownedIds: creations.map(item => item.id), primaryErrors: info.errors, outcomes }, null, 2), contentType: "application/json" });
    } catch (error) { failures.push(new Error("Could not retain the workflow cleanup results", { cause: error })); }
  }
  try { expect(browserErrors.get(page), "Uncaught browser errors").toEqual([]); }
  catch (error) { failures.push(error instanceof Error ? error : new Error(String(error))); }
  // Playwright retains the body failure separately; cleanup failures must also fail the case.
  if (failures.length) throw new AggregateError(failures, failures.map(error => error.message).join("; "));
});

test.beforeEach(async ({ page }, info) => {
  const reportedAppearance = info.title === "feature filtering and settings dialogs";
  await page.addInitScript(({ mode, reportedAppearance }) => {
    if (reportedAppearance) localStorage.setItem("elsa-studio-theme", "classic");
    else localStorage.removeItem("elsa-studio-theme");
    localStorage.setItem("elsa-studio-workspace-style", reportedAppearance ? "minimal" : "linen");
    localStorage.setItem("elsa-studio-theme-mode", mode);
  }, { mode: info.project.use.colorScheme ?? "light", reportedAppearance });
  const errors: string[] = [];
  browserErrors.set(page, errors);
  ownedCleanups.set(page, []);
  page.on("pageerror", error => errors.push(error.message));
  // Keep browser runtime errors actionable without accepting an assertion-only green result.
  await info.attach("source", { body: process.env.UX_SOURCE_SHA ?? "unknown", contentType: "text/plain" });
  await signIn(page);
  if (reportedAppearance) {
    await expect(page.locator("html")).toHaveAttribute("data-theme", "classic");
    await expect(page.locator("html")).toHaveAttribute("data-color-scheme", "classic");
    await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "minimal");
  }
  await expect(page.getByRole("heading", { name: /^No active workflow definitions in All workflows$/ })).toBeVisible();
  await capture(page, info, "reference");
});

test("feature filtering and settings dialogs", async ({ page }, info) => {
  await page.goto("/features");
  const list = page.locator(".feature-management-list");
  const filter = page.getByRole("searchbox", { name: "Filter features", exact: true });
  for (const host of ["Server", "Studio"]) {
    await page.getByRole("tablist", { name: "Hosts" }).getByRole("tab", { name: new RegExp(`^${host}`) }).click();
    await expect(list.locator(".feature-management-card").first()).toBeVisible();
    await filter.fill("no-such-feature-acceptance");
    const message = list.getByText("No features match this filter.", { exact: true });
    await expect(message).toBeVisible();
    await message.scrollIntoViewIfNeeded();
    const alignment = await message.evaluate(element => {
      const heading = document.querySelector(".feature-management-list-heading strong");
      if (!heading) throw new Error("Feature list heading is missing.");
      const text = document.createRange();
      text.selectNodeContents(element);
      return {
        messageLeft: text.getBoundingClientRect().left,
        headingLeft: heading.getBoundingClientRect().left
      };
    });
    expect(Math.abs(alignment.messageLeft - alignment.headingLeft)).toBeLessThanOrEqual(1);
    await capture(page, info, `features-${host.toLowerCase()}-no-matches`);
    await filter.fill("");
    await expect(message).toBeHidden();
    await expect(list.locator(".feature-management-card").first()).toBeVisible();
    await list.locator(".feature-management-card").first().scrollIntoViewIfNeeded();
    await capture(page, info, `features-${host.toLowerCase()}-populated`);
  }

  await page.getByRole("tablist", { name: "Hosts" }).getByRole("tab", { name: /^Server/ }).click();
  await filter.fill("Activities Dispatch Workflow (Runtime)");
  await list.getByRole("button", { name: /^Activities Dispatch Workflow \(Runtime\)/ }).click();
  const edit = page.getByRole("button", { name: "Edit settings", exact: true });
  await edit.click();
  const settings = page.getByRole("dialog", { name: "Activities Dispatch Workflow (Runtime)", exact: true });
  await expect(settings).toBeVisible();
  await capture(page, info, "feature-settings-dialog");
  await settings.getByRole("button", { name: "Close", exact: true }).click();
  await expect(settings).toBeHidden();
  await edit.click();
  await settings.getByLabel(/^Child-start maximum delivery attempts/).fill("5");
  await settings.getByRole("button", { name: "Save draft", exact: true }).click();
  await expect(settings).toBeHidden();
  await edit.click();
  await expect(settings.getByLabel(/^Child-start maximum delivery attempts/)).toHaveValue("5");
  await settings.getByRole("button", { name: "Close", exact: true }).click();
  await page.getByRole("button", { name: "Review changes", exact: true }).click();
  const review = page.getByRole("dialog", { name: "Review changes", exact: true });
  await expect(review).toBeVisible();
  await capture(page, info, "feature-impact-dialog");
  await review.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(review).toBeHidden();
});

test("collections and connection creation", async ({ page }, info) => {
  const stamp = `${info.project.name}-${Date.now()}`;
  await page.goto("/security/connections");
  await expect(page.getByRole("region", { name: "Connections management", exact: true }).getByRole("heading", { name: "Connections", exact: true })).toBeVisible();
  // A fresh run starts with no connections; later projects use the same disposable store and exercise search recovery.
  const search = page.getByRole("searchbox", { name: /search connections/i });
  await expect(page.getByRole("button", { name: "Search", exact: true })).toBeEnabled();
  await search.fill(`absent-${stamp}`);
  await search.press("Enter");
  await expect(page.getByRole("button", { name: /clear search/i })).toBeVisible();
  await capture(page, info, "no-matches");
  await page.getByRole("button", { name: /clear search/i }).press("Enter");
  await expect(search).toHaveValue("");
  await expect(page.getByRole("table", { name: "Connections", exact: true })).not.toHaveAttribute("aria-busy", "true");
  await capture(page, info, "connections-list");
  if (info.project.name === "desktop-light") {
    await expect(page.getByRole("table", { name: "Connections", exact: true }).getByRole("button", { name: "Create connection", exact: true })).toBeVisible();
  }

  await page.goto("/security/secrets");
  const secretName = `DbConnectionString-${stamp}`;
  await page.getByRole("button", { name: "Create secret", exact: true }).first().click();
  const secret = page.getByRole("region", { name: "Create secret", exact: true });
  const technicalName = secret.getByLabel("Technical name", { exact: true });
  await technicalName.fill(` ${secretName} `);
  await technicalName.press("Tab");
  await expect(technicalName).toHaveValue(secretName);
  await capture(page, info, "secret-create");
  await secret.getByLabel("Value", { exact: true }).fill("disposable-acceptance-value");
  await secret.getByRole("button", { name: "Create", exact: true }).click();
  await expect(secret).toBeHidden();
  await page.reload();
  const secretRow = page.getByRole("row").filter({ hasText: secretName });
  await expect(secretRow).toBeVisible();
  await capture(page, info, "secret-list");
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"], { origin: new URL(page.url()).origin });
  await secretRow.getByRole("button", { name: `Copy technical name ${secretName}`, exact: true }).click();
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(secretName);
  await secretRow.getByRole("button", { name: `Edit ${secretName}`, exact: true }).click();
  const editor = page.getByRole("region", { name: "Edit secret", exact: true });
  await expect(editor.locator("code")).toHaveText(secretName);
  await capture(page, info, "secret-detail");
  await editor.getByRole("button", { name: "Back to secrets", exact: true }).click();
  await page.getByRole("button", { name: "Create secret", exact: true }).first().click();
  await technicalName.fill(secretName.toUpperCase());
  await secret.getByLabel("Value", { exact: true }).fill("disposable-duplicate-value");
  await secret.getByRole("button", { name: "Create", exact: true }).click();
  await expect(secret.getByRole("alert")).toHaveText("A secret with the same name already exists.");
  await expect(technicalName).toHaveValue(secretName.toUpperCase());
  await capture(page, info, "secret-duplicate");
  await secret.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(secret).toBeHidden();
  await expect(page.getByRole("button", { name: "Create secret", exact: true }).first()).toBeFocused();

  await page.goto("/security/connections");
  const openConnection = page.getByRole("button", { name: "Create connection", exact: true }).first();
  await expect(openConnection).toBeEnabled();
  await openConnection.press("Enter");
  const dialog = page.getByRole("dialog", { name: "Create connection", exact: true });
  const name = dialog.getByLabel("Name", { exact: true });
  await expect(name).toBeFocused();
  await name.fill("invalid name");
  await dialog.getByLabel("Auth scheme", { exact: true }).selectOption("api-key");
  await dialog.getByLabel("Existing Secret for API key", { exact: true }).selectOption(secretName);
  await dialog.getByLabel("Base URL", { exact: true }).fill("https://example.invalid");
  await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeDisabled();
  await expect(name).toHaveAttribute("aria-invalid", "true");
  await name.scrollIntoViewIfNeeded();
  await capture(page, info, "invalid-name");
  await name.fill(`qa-${stamp}`);
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("status").filter({ hasText: /^Connection / })).toContainText(`Connection 'qa-${stamp}' created.`);
  const createdRow = page.getByRole("row").filter({ hasText: `qa-${stamp}` });
  await expect(createdRow).toBeVisible();
  const nameLink = createdRow.getByRole("button", { name: `qa-${stamp}`, exact: true });
  // A table can stay inside the viewport while its flexible name column collapses to one character.
  expect((await nameLink.boundingBox())!.width).toBeGreaterThan(100);
  expect((await createdRow.boundingBox())!.height).toBeLessThan(160);
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Create connection", exact: true }).first()).toBeFocused();
  await capture(page, info, "completed");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("agent authoring and navigation", async ({ page }, info) => {
  await page.goto("/agents");
  await expect(page.getByTestId("agents-page").getByRole("heading", { name: "Agents", exact: true })).toBeVisible();
  await expect(page.getByRole("status").filter({ hasText: /^\d[\d,]* agents?$/ })).toBeVisible();
  await capture(page, info, "agents-list");
  const create = page.getByRole("button", { name: "New agent", exact: true });
  await create.press("Enter");
  await expect(page.getByRole("searchbox", { name: "Search agents", exact: true })).toHaveCount(0);
  await expect(page.getByRole("textbox", { name: "Name", exact: true })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(create).toBeFocused();
  await create.press("Enter");
  await page.getByRole("textbox", { name: "Name", exact: true }).fill(`Acceptance ${info.project.name} ${Date.now()}`);
  await page.getByRole("textbox", { name: /^Purpose/ }).fill("Proofread a supplied text.");
  await capture(page, info, "agent-create");
  await page.getByRole("button", { name: "Create and edit", exact: true }).click();
  const editor = page.getByTestId("agent-editor");
  const instructions = editor.getByRole("textbox", { name: /^Instructions/ });
  await expect(instructions).toBeVisible();
  await instructions.fill("Proofread the supplied text and return concise suggestions.");
  await instructions.press("Tab");
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved", { timeout: 30_000 });
  await page.reload();
  await expect(instructions).toHaveValue("Proofread the supplied text and return concise suggestions.");
  const advanced = editor.locator("details.agent-advanced");
  await expect(advanced).not.toHaveAttribute("open");
  await page.getByRole("tab", { name: "Designer", exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "agent-designer");
  await editor.getByRole("combobox", { name: /^Model connection/ }).scrollIntoViewIfNeeded();
  await capture(page, info, "agent-model");
  await advanced.locator("summary").press("Enter");
  await advanced.getByRole("textbox", { name: /^Maximum model calls/ }).scrollIntoViewIfNeeded();
  await capture(page, info, "advanced-settings");
  await advanced.locator("summary").press("Enter");
  const tabs = page.getByRole("tablist", { name: "Activity Definition authoring views" });
  await expect(tabs.getByRole("tab")).toHaveText(["Designer", "Inputs (0)", "Outputs (0)", "Outcomes (1)"]);
  for (const kind of ["input", "output"] as const) {
    await tabs.getByRole("tab", { name: kind === "input" ? "Inputs (0)" : "Outputs (0)", exact: true }).click();
    await expect(page.getByText(`No ${kind}s yet`, { exact: true })).toBeVisible();
    await capture(page, info, `${kind}s`);
    const view = page.locator(".ad-public-interface-view");
    await view.getByRole("button", { name: `Add ${kind}`, exact: true }).click();
    await expect(view.getByRole("textbox", { name: `${kind === "input" ? "Input" : "Output"} name`, exact: true })).toBeFocused();
    await view.getByRole("button", { name: "Cancel", exact: true }).click();
    await view.getByRole("button", { name: `Add ${kind}`, exact: true }).click();
    await view.getByRole("textbox", { name: `${kind === "input" ? "Input" : "Output"} name`, exact: true }).fill(kind === "input" ? "Topic" : "Result");
    await view.getByRole("button", { name: `Add ${kind}`, exact: true }).last().click();
    const member = view.locator(`[data-contract-kind="${kind}"][data-contract-selected="true"]`);
    const settings = member.getByRole("tablist", { name: "Member settings", exact: true });
    await expect(member.getByRole("textbox", { name: "Name", exact: true })).toBeFocused();
    await expect(page.locator(".ad-save-chip")).toHaveText("Saved", { timeout: 30_000 });
    if (kind === "input") {
      await member.getByRole("combobox", { name: "Default", exact: true }).selectOption("literal");
      await member.getByRole("textbox", { name: "Value", exact: true }).fill("A retained topic");
    }
    await capture(page, info, `${kind}-general`);
    await settings.getByRole("tab", { name: "General", exact: true }).focus();
    await page.keyboard.press("ArrowRight");
    await expect(settings.getByRole("tab", { name: "Presentation", exact: true })).toBeFocused();
    await member.getByRole("textbox", { name: /^Display name/ }).fill(kind === "input" ? "Writing topic" : "Written result");
    await member.getByRole("combobox", { name: /^Category/ }).fill("Writing");
    await member.getByRole("spinbutton", { name: "Order", exact: true }).fill("2");
    await capture(page, info, `${kind}-presentation`);
    await settings.getByRole("tab", { name: "Advanced", exact: true }).click();
    await expect(member.getByRole("combobox", { name: "Durable storage driver", exact: true })).toBeVisible();
    await expect(member.getByText("Type alias", { exact: true })).toHaveCount(0);
    const key = member.getByRole("textbox", { name: "Stable reference key", exact: true });
    await key.fill(`${kind}-writing`);
    await member.getByRole("button", { name: "Apply key", exact: true }).click();
    await expect(member.getByRole("button", { name: "Apply key", exact: true })).toBeDisabled();
    await capture(page, info, `${kind}-advanced`);
    await settings.getByRole("tab", { name: "General", exact: true }).click();
    if (kind === "input") {
      await expect(member.getByRole("textbox", { name: "Value", exact: true })).toHaveValue("A retained topic");
      await member.getByRole("button", { name: "Apply default", exact: true }).click();
    }
    await expect(page.locator(".ad-save-chip")).toHaveText("Saved", { timeout: 30_000 });
    await page.reload();
    await page.getByRole("tablist", { name: "Activity Definition authoring views" }).getByRole("tab", { name: kind === "input" ? "Inputs (1)" : "Outputs (1)", exact: true }).click();
    const saved = page.locator(`[data-contract-kind="${kind}"][data-contract-selected="true"]`);
    if (kind === "input") await expect(saved.getByRole("textbox", { name: "Value", exact: true })).toHaveValue("A retained topic");
    await saved.getByRole("tab", { name: "Presentation", exact: true }).click();
    await expect(saved.getByRole("combobox", { name: /^Category/ })).toHaveValue("Writing");
    await expect(saved.getByRole("spinbutton", { name: "Order", exact: true })).toHaveValue("2");
    await saved.getByRole("tab", { name: "Advanced", exact: true }).click();
    await expect(saved.getByRole("textbox", { name: "Stable reference key", exact: true })).toHaveValue(`${kind}-writing`);
  }
  await tabs.getByRole("tab", { name: "Outcomes (1)", exact: true }).click();
  const outcome = page.getByRole("region", { name: "Done", exact: true });
  await expect(outcome.getByText("Emitted", { exact: true })).toBeVisible();
  await expect(outcome.getByRole("checkbox")).toHaveCount(0);
  await expect(outcome.getByRole("textbox", { name: "Name", exact: true })).toHaveCount(0);
  if ((info.project.use.viewport?.width ?? 0) >= 1440) {
    for (const [label, icon] of [["Agents", "bot"], ["Secrets", "key-round"], ["Connections", "cable"]]) {
      const entry = page.getByRole("link", { name: label, exact: true });
      if (!await entry.isVisible()) await page.getByRole("button", { name: /More tools/ }).click();
      await expect(entry.locator(`svg.lucide-${icon}`)).toBeVisible();
    }
  }
  await outcome.getByText("Emitted", { exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "completed");
});

test("publication blockers explain how to correct agent settings", async ({ page }, info) => {
  await page.goto("/agents");
  await page.getByRole("button", { name: "New agent", exact: true }).click();
  await page.getByRole("textbox", { name: "Name", exact: true }).fill(`Publication guidance ${info.project.name} ${Date.now()}`);
  await page.getByRole("textbox", { name: /^Purpose/ }).fill("Verify publication guidance without calling a model.");
  await page.getByRole("button", { name: "Create and edit", exact: true }).click();
  const editor = page.getByTestId("agent-editor");
  const instructions = editor.getByRole("textbox", { name: /^Instructions/ });
  await instructions.fill("");
  await instructions.press("Tab");
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved", { timeout: 30_000 });
  const publish = page.locator(".ad-publication-toolbar-trigger");
  await publish.click();
  const dialog = page.getByRole("dialog", { name: "Review & publish", exact: true });
  const blockers = dialog.locator(".ad-publication-blockers");
  await expect(blockers.getByRole("heading", { name: "Resolve these issues before publishing", exact: true })).toBeVisible();
  await expect(blockers.getByText("Agent instructions are required.", { exact: true })).toBeVisible();
  await expect(blockers.getByText("A complete model connection has not been selected.", { exact: true })).toBeVisible();
  await expect(blockers.getByRole("button", { name: "Edit instructions", exact: true })).toHaveCount(1);
  await expect(blockers.getByRole("button", { name: "Choose model connection", exact: true })).toHaveCount(1);
  await expect(dialog.getByRole("button", { name: "Publish 1.0.0", exact: true })).toBeDisabled();
  await expect(dialog.getByText("Review the provider documentation for this diagnostic code.", { exact: true })).toHaveCount(0);
  await capture(page, info, "blocked");
  await blockers.getByRole("button", { name: "Edit instructions", exact: true }).press("Enter");
  await expect(dialog).toBeHidden();
  await expect(instructions).toBeFocused();
  await capture(page, info, "instructions-focused");
  await instructions.fill("Proofread the supplied text and return concise suggestions.");
  await instructions.press("Tab");
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved", { timeout: 30_000 });
  await publish.click();
  await expect(blockers.getByText("A complete model connection has not been selected.", { exact: true })).toBeVisible();
  await expect(blockers.getByRole("button", { name: "Edit instructions", exact: true })).toHaveCount(0);
  await capture(page, info, "model-blocked");
  await blockers.getByRole("button", { name: "Choose model connection", exact: true }).press("Enter");
  await expect(dialog).toBeHidden();
  await expect(editor.getByRole("combobox", { name: /^Model connection/ })).toBeFocused();
  await expect(editor.getByText(/There are no model connections you can use yet/)).toBeVisible();
  await capture(page, info, "model-focused");
  await publish.click();
  await blockers.getByRole("button", { name: "Check again", exact: true }).click();
  await expect(blockers.getByRole("button", { name: "Choose model connection", exact: true })).toBeVisible();
  await blockers.getByRole("button", { name: "Return to editor", exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(publish).toBeFocused();
  await capture(page, info, "completed");
});

test("save template toast", async ({ page }, info) => {
  await templateToastJourney(page, info, capture, cleanup => { ownedCleanups.get(page)!.push(cleanup); });
});

test("workflow template metadata defaults", async ({ page }, info) => {
  const stamp = `Metadata ${info.project.name} ${Date.now()}`;
  const templateNames = [`${stamp} greeting`, `${stamp} order`];
  const descriptions = ["Sends a greeting.", "Processes an order."];
  const creations: Array<{ url: string; authorization: string; id: string }> = [];
  createdWorkflows.set(page, creations);
  // Persist real templates using the same composition and controls as a workflow author.
  for (let index = 0; index < templateNames.length; index++) {
    await page.goto("/workflows/templates");
    await page.getByRole("button", { name: "New template", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Create template", exact: true });
    await dialog.getByRole("textbox", { name: "Display name", exact: true }).fill(templateNames[index]);
    await dialog.getByRole("textbox", { name: "Description", exact: true }).fill(descriptions[index]);
    const sequence = dialog.getByRole("radio", { name: "Sequence", exact: true });
    await sequence.focus();
    await sequence.press("Space");
    await expect(sequence).toBeChecked();
    await dialog.getByRole("button", { name: "Create template", exact: true }).click();
    await expect(page.getByRole("button", { name: "Templates", exact: true })).toBeVisible();
  }

  const submitAndInspect = async (expectedName: string, expectedDescription: string) => {
    const response = page.waitForResponse(value => value.request().method() === "POST" && /\/definitions$/.test(new URL(value.url()).pathname));
    await page.getByRole("dialog").getByRole("button", { name: "Create", exact: true }).click();
    const saved = await response;
    expect(saved.ok()).toBe(true);
    const body = await saved.json();
    creations.push({ url: saved.url(), authorization: saved.request().headers().authorization, id: body.definition.id });
    expect(saved.request().postDataJSON()).toMatchObject({ name: expectedName });
    expect(saved.request().postDataJSON().description || "").toBe(expectedDescription);
    await expect(page.getByRole("button", { name: "More workflow actions", exact: true })).toBeVisible();
    await page.reload();
    const properties = page.getByRole("tab", { name: "Properties", exact: true });
    await expect(properties).toBeVisible();
    // Narrow layouts retain the user's inspector state; collapse it only when it is still open.
    const collapseInspector = page.getByRole("button", { name: "Collapse inspector panel", exact: true });
    if ((page.viewportSize()?.width ?? 1440) < 700 && await collapseInspector.isVisible()) {
      await collapseInspector.click();
    }
    await properties.click();
    if ((page.viewportSize()?.width ?? 1440) < 700) {
      const collapseActivities = page.getByRole("button", { name: "Collapse activities panel", exact: true });
      if (await collapseActivities.isVisible()) await collapseActivities.click();
    }
    const savedName = page.getByRole("textbox", { name: "Workflow name", exact: true });
    const savedDescription = page.getByRole("textbox", { name: "Workflow description", exact: true });
    await expect(savedName).toHaveValue(expectedName);
    await expect(savedDescription).toHaveValue(expectedDescription);
    await expect(savedName).toBeInViewport();
    await expect(savedDescription).toBeInViewport();
    // Being onscreen alone can still leave an unusable one-character input beside a fixed-width label.
    expect((await savedName.boundingBox())!.width).toBeGreaterThanOrEqual(120);
    expect((await savedDescription.boundingBox())!.width).toBeGreaterThanOrEqual(120);
  };

  await page.goto("/workflows/definitions");
  const open = page.getByRole("button", { name: "Create workflow", exact: true }).first();
  await open.click();
  const dialog = page.getByRole("dialog", { name: "Create Workflow", exact: true });
  const startingPoint = dialog.getByRole("combobox", { name: "Starting point", exact: true });
  const name = dialog.getByRole("textbox", { name: "Display name", exact: true });
  const description = dialog.getByRole("textbox", { name: "Description", exact: true });
  await expect(startingPoint).toBeFocused();
  // The host replaces the native arrow; module field surfaces must retain its visible chevron.
  await expect(startingPoint).toHaveCSS("appearance", "none");
  await expect(startingPoint).not.toHaveCSS("background-image", "none");
  await startingPoint.press("Tab");
  await expect(name).toBeFocused();
  await name.press("Tab");
  await expect(description).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(open).toBeFocused();
  await open.click();
  await expect(startingPoint.locator("option").filter({ hasText: templateNames[0] })).toHaveCount(1);
  await startingPoint.selectOption({ label: templateNames[0] });
  await expect(name).toHaveValue(templateNames[0]);
  await expect(description).toHaveValue(descriptions[0]);
  await capture(page, info, "template-defaults");
  await startingPoint.selectOption({ label: templateNames[1] });
  await expect(name).toHaveValue(templateNames[1]);
  await expect(description).toHaveValue(descriptions[1]);
  await name.fill(`${stamp} custom`);
  await startingPoint.selectOption({ label: templateNames[0] });
  await expect(name).toHaveValue(`${stamp} custom`);
  await expect(description).toHaveValue(descriptions[0]);
  await description.fill("");
  await startingPoint.selectOption({ label: templateNames[1] });
  await expect(name).toHaveValue(`${stamp} custom`);
  await expect(description).toHaveValue("");
  await capture(page, info, "template-edits");
  await submitAndInspect(`${stamp} custom`, "");

  await page.goto("/workflows/templates");
  const card = page.locator("article").filter({ has: page.getByRole("button", { name: templateNames[0], exact: true }) });
  await card.getByRole("button", { name: "Use template", exact: true }).click();
  await expect(name).toHaveValue(templateNames[0]);
  await expect(description).toHaveValue(descriptions[0]);
  await startingPoint.selectOption({ label: templateNames[1] });
  await expect(name).toHaveValue(templateNames[1]);
  await expect(description).toHaveValue(descriptions[1]);
  await capture(page, info, "template-catalog-entry");
  await submitAndInspect(templateNames[1], descriptions[1]);
  await capture(page, info, "template-persisted");
});

test("executable designer parity", async ({ page }, info) => {
  await executableDesignerJourney(page, info, capture);
});

test("choose data for activity outputs", async ({ page }, info) => {
  await chooseDataJourney(page, info, capture, cleanup => { ownedCleanups.get(page)!.push(cleanup); });
});

test("tabbed Secrets authoring and metadata", async ({ page }, info) => {
  await tabbedSecretsJourney(page, info, capture);
});
