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
  if ((info.project.use.viewport?.width ?? 0) <= 1024) {
    const overlaps = await page.locator(".sidebar .nav-section a, .sidebar .nav-group-toggle").evaluateAll(nodes => {
      const bounds = nodes.map(node => ({ label: node.textContent?.trim(), box: node.getBoundingClientRect() })).filter(item => item.box.width > 0 && item.box.height > 0).sort((a, b) => a.box.left - b.box.left);
      return bounds.slice(1).flatMap((item, index) => item.box.left < bounds[index].box.right - 1 ? [`${bounds[index].label} overlaps ${item.label}`] : []);
    });
    expect(overlaps, "Horizontal navigation destinations must not overlap").toEqual([]);
  }
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
const designerDefinitions = new WeakMap<Page, string[]>();

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
  const designerIds = designerDefinitions.get(page) ?? [];
  const base = process.env.ELSA_WORKBENCH_URL!;
  const owned: CreatedWorkflow[] = [
    ...creations,
    ...designerIds.map(id => ({ id, url: `${base}/design/workflows/definitions` }))
  ];
  const failures: string[] = [];
  // Close journey-owned records before deleting their workflow definitions.
  for (const cleanup of ownedCleanups.get(page) ?? []) {
    try { await cleanup(); } catch { failures.push("request-failed"); }
  }
  const receipts: Array<{ id: string; status?: number; error?: string }> = [];
  let context: APIRequestContext | undefined;
  try {
    if (owned.length) context = await playwright.request.newContext({ storageState: await page.context().storageState(), timeout: 5_000 });
    for (const item of owned) {
      try {
        const removed = await context!.delete(`${item.url}/${encodeURIComponent(item.id)}`, {
          timeout: 5_000,
          headers: item.authorization ? { Authorization: item.authorization } : undefined
        });
        receipts.push({ id: item.id, status: removed.status() });
      } catch {
        receipts.push({ id: item.id, error: "request-failed" });
      }
    }
  } catch {
    receipts.push(...owned.map(item => ({ id: item.id, error: "request-failed" })));
  } finally {
    if (context) {
      try { await context.dispose(); } catch { failures.push("request-failed"); }
    }
  }
  try {
    await info.attach("owned-definition-cleanup", { body: JSON.stringify({ ownedIds: owned.map(item => item.id), receipts }), contentType: "application/json" });
  } catch {
    failures.push("receipt-attachment-failed");
  }
  expect(receipts.filter(item => item.error || !item.status || item.status < 200 || item.status >= 300), "Every owned definition cleanup succeeds").toEqual([]);
  expect(failures, "Journey cleanup").toEqual([]);
  expect(browserErrors.get(page), "Uncaught browser errors").toEqual([]);
});

test.beforeEach(async ({ page }, info) => {
  const reportedAppearance = info.title === "feature filtering and settings dialogs";
  await page.addInitScript(({ mode, reportedAppearance }) => {
    if (reportedAppearance) localStorage.setItem("elsa-studio-theme", "classic");
    else localStorage.removeItem("elsa-studio-theme");
    localStorage.setItem("elsa-studio-workspace-style", reportedAppearance ? "minimal" : "linen");
    localStorage.setItem("elsa-studio-theme-mode", mode);
  }, { mode: info.project.use.colorScheme ?? "light", reportedAppearance });
  designerDefinitions.set(page, []);
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
  const main = page.getByRole("main");
  await expect(page).toHaveURL(url => url.pathname === "/workflows/definitions");
  const empty = main.getByRole("heading", { name: "No active workflow definitions in All workflows", exact: true });
  const populated = main.getByRole("table", { name: "Workflow definitions", exact: true });
  await expect(empty.or(populated)).toBeVisible();
  if (await populated.isVisible()) {
    await expect(populated).toHaveAttribute("aria-busy", "false");
    expect(await populated.getByRole("row").count(), "Populated readiness includes a real data row").toBeGreaterThan(1);
  }
  // This first journey in each serial project still owns the strict empty-store fixture proof.
  if (info.title === "user tasks inbox and response") await expect(empty).toBeVisible();
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
  await page.goto("/workflows/activity-definitions");
  await expect(page.getByRole("table", { name: "Activity Definitions", exact: true }).or(page.getByRole("heading", { name: "No designed activities yet", exact: true }))).toBeVisible();
  await capture(page, info, "library-initial");
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
  const secret = page.getByRole("dialog", { name: "Create secret", exact: true });
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
  const editor = page.getByRole("dialog", { name: "Edit secret", exact: true });
  await expect(editor.locator("code")).toHaveText(secretName);
  await capture(page, info, "secret-detail");
  await editor.getByRole("button", { name: "Close editor", exact: true }).click();
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
      if (!await entry.isVisible()) await page.getByRole("button", { name: label === "Agents" ? "Build" : "Integrations", exact: true }).click();
      await expect(entry.locator(`svg.lucide-${icon}`)).toBeVisible();
    }
  }
  await outcome.getByText("Emitted", { exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "completed");
});

test("grouped navigation and moved destinations", async ({ page }, info) => {
  const sidebar = page.locator(".sidebar");
  const navLink = (name: string) => sidebar.getByRole("link", { name, exact: true });
  const desktop = (info.project.use.viewport?.width ?? 0) >= 1025;
  for (const name of ["Build", "Operate", "Integrations"]) await expect(sidebar.getByRole("button", { name, exact: true })).toHaveAttribute("aria-expanded", "true");
  await expect(sidebar.getByRole("button", { name: "More tools" })).toHaveCount(0);
  await navLink("Connections").click();
  await expect(navLink("Connections")).toHaveAttribute("aria-current", "page");
  await expect(page.getByRole("table", { name: "Connections", exact: true })).not.toHaveAttribute("aria-busy", "true");
  await capture(page, info, "groups");
  await sidebar.getByRole("navigation", { name: "Build", exact: true }).getByRole("link", { name: "Workflows", exact: true }).click();
  await page.getByRole("navigation", { name: "Workflows views" }).getByRole("link", { name: "Executables", exact: true }).click();
  await expect(navLink("Workflows")).toHaveAttribute("aria-current", "page");
  await expect(page).toHaveURL(/\/workflows\/executables(?:\?|$)/);
  await expect(page.getByRole("navigation", { name: "Workflows views" }).getByRole("link", { name: "Executables", exact: true })).toHaveAttribute("aria-current", "page");
  await expect(page.getByRole("main").getByRole("heading", { name: "Executables", level: 2, exact: true })).toBeVisible();
  const executableList = page.getByRole("table", { name: "Workflow executables", exact: true });
  const emptyExecutables = page.getByRole("heading", { name: "No workflow executables", exact: true });
  await expect(executableList.or(emptyExecutables)).toBeVisible();
  await capture(page, info, "executables-list");
  await page.evaluate(() => localStorage.setItem("elsa-studio.ui.nav.groups", JSON.stringify({ build: false })));
  await page.goto("/workflows/tags");
  const build = sidebar.getByRole("button", { name: "Build", exact: true });
  await expect(build).toHaveAttribute("aria-expanded", "true");
  await expect(navLink("Workflows")).toHaveAttribute("aria-current", "page");
  await expect(page.getByRole("main").getByRole("heading", { name: "Workflow tags", exact: true })).toBeVisible();
  await capture(page, info, "tags-list");
  await page.getByRole("button", { name: "Create tag", exact: true }).click();
  await expect(page).toHaveURL(/\/workflows\/tags\/new(?:\?|$)/);
  await page.reload();
  await expect(build).toHaveAttribute("aria-expanded", "true");
  await expect(navLink("Workflows")).toHaveAttribute("aria-current", "page");
  await expect(page.getByRole("main").getByRole("heading", { name: "Create workflow tag", exact: true })).toBeVisible();
  await capture(page, info, "tags-create");
  await page.getByRole("textbox", { name: "Tag name", exact: true }).fill(`navigation-${info.project.name}-${Date.now()}`);
  await page.getByRole("button", { name: "Create tag", exact: true }).click();
  await expect(page.getByRole("main").getByRole("heading", { name: "Edit workflow tag", exact: true })).toBeVisible();
  // A real saved tag bookmark must also reveal the group after a fresh document load.
  await page.reload();
  await expect(build).toHaveAttribute("aria-expanded", "true");
  await expect(navLink("Workflows")).toHaveAttribute("aria-current", "page");
  await expect(page.getByRole("main").getByRole("heading", { name: "Edit workflow tag", exact: true })).toBeVisible();
  const tagUrl = page.url();
  const tagDocument = await page.evaluate(() => performance.timeOrigin);
  await build.click();
  await expect(build).toHaveAttribute("aria-expanded", "false");
  await navLink("Home").click();
  await expect(navLink("Home")).toHaveAttribute("aria-current", "page");
  await page.goBack();
  await expect(page).toHaveURL(tagUrl);
  await expect(build).toHaveAttribute("aria-expanded", "true");
  await expect(navLink("Workflows")).toHaveAttribute("aria-current", "page");
  const tagTabId = await page.getByRole("tablist", { name: "Open screens", exact: true }).getByRole("tab", { selected: true }).getAttribute("id");
  expect(tagTabId).toBeTruthy();
  await build.click();
  await expect(build).toHaveAttribute("aria-expanded", "false");
  await navLink("Home").click({ modifiers: ["Alt"] });
  await expect(navLink("Home")).toHaveAttribute("aria-current", "page");
  await page.locator(`[id="${tagTabId}"]`).click();
  await expect(page).toHaveURL(tagUrl);
  await expect(build).toHaveAttribute("aria-expanded", "true");
  await expect(navLink("Workflows")).toHaveAttribute("aria-current", "page");
  await expect(page.getByRole("main").getByRole("heading", { name: "Edit workflow tag", exact: true })).toBeVisible();
  expect(await page.evaluate(() => performance.timeOrigin)).toBe(tagDocument);
  // Restore the open preference through the UI before the later disclosure/rail exercise.
  await build.click();
  await expect(build).toHaveAttribute("aria-expanded", "false");
  await build.click();
  await expect(build).toHaveAttribute("aria-expanded", "true");
  await capture(page, info, "tags-edit");
  await navLink("Library").click();
  const libraryViews = page.getByRole("navigation", { name: "Library views" });
  await libraryViews.getByRole("link", { name: "Templates", exact: true }).click();
  await expect(navLink("Library")).toHaveAttribute("aria-current", "page");
  await libraryViews.getByRole("link", { name: "Activity definitions", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Library", exact: true })).toBeVisible();
  await expect(page.getByRole("table", { name: "Activity Definitions", exact: true }).or(page.getByRole("heading", { name: "No designed activities yet", exact: true }))).toBeVisible();
  await capture(page, info, "library");
  const screens = page.getByRole("tablist", { name: "Open screens", exact: true });
  const previousScreenCount = await screens.getByRole("tab").count();
  await page.locator(".studio-library-header").getByRole("link", { name: "Activity availability", exact: true }).click({ modifiers: ["Alt"] });
  await expect(screens.getByRole("tab")).toHaveCount(previousScreenCount + 1);
  await expect(page.locator(".settings-pane").getByRole("heading", { name: "Activity availability", exact: true })).toBeVisible();
  await expect(page.getByRole("navigation", { name: "Settings sections", exact: true })).toContainText("Workflow authoring");
  await expect(navLink("Settings")).toHaveAttribute("aria-current", "page");
  await expect(page.getByText("Loading availability…", { exact: true })).toHaveCount(0);
  await capture(page, info, "workflow-authoring-settings");
  const settingsSections = page.getByRole("navigation", { name: "Settings sections", exact: true });
  const sectionTabCount = await screens.getByRole("tab").count();
  const settingsDocument = await page.evaluate(() => performance.timeOrigin);
  await settingsSections.getByRole("link", { name: "Runtime evidence settings", exact: true }).click({ modifiers: ["Alt"] });
  await expect(screens.getByRole("tab")).toHaveCount(sectionTabCount + 1);
  await expect(page).toHaveURL(/\/settings\/workflows-runtime-diagnostics(?:\?|$)/);
  await expect(page.locator(".settings-pane").getByRole("heading", { name: "Runtime evidence settings", exact: true })).toBeVisible();
  expect(await page.evaluate(() => performance.timeOrigin)).toBe(settingsDocument);
  const originalSettingsUrl = page.url();
  const nativeSectionPage = page.context().waitForEvent("page");
  await settingsSections.getByRole("link", { name: "Activity availability", exact: true }).click({ modifiers: ["ControlOrMeta"] });
  const nativePage = await nativeSectionPage;
  try {
    await expect(nativePage).toHaveURL(/\/settings\/workflows-activity-availability(?:\?|$)/);
    await expect(nativePage.locator(".settings-pane").getByRole("heading", { name: "Activity availability", exact: true })).toBeVisible();
    await expect(page).toHaveURL(originalSettingsUrl);
    await expect(screens.getByRole("tab")).toHaveCount(sectionTabCount + 1);
    expect(await page.evaluate(() => performance.timeOrigin)).toBe(settingsDocument);
  } finally { await nativePage.close(); }
  await settingsSections.getByRole("link", { name: "Activity availability", exact: true }).click();
  await expect(page.locator(".settings-pane").getByRole("heading", { name: "Activity availability", exact: true })).toBeVisible();
  await expect(screens.getByRole("tab")).toHaveCount(sectionTabCount + 1);
  expect(await page.evaluate(() => performance.timeOrigin)).toBe(settingsDocument);
  // Existing bookmarked routes must resolve to their new Settings surface as well.
  await page.goto("/workflows/runtime-diagnostics");
  await expect(page.locator(".settings-pane").getByRole("heading", { name: "Runtime evidence settings", exact: true })).toBeVisible();
  await expect(page.getByRole("navigation", { name: "Settings sections", exact: true })).toContainText("Runtime");
  await expect(navLink("Settings")).toHaveAttribute("aria-current", "page");
  await capture(page, info, "runtime-settings");
  await navLink("Diagnostics").click();
  const diagnostics = page.getByRole("navigation", { name: "Diagnostics views" });
  for (const name of ["Console", "Structured logs", "OpenTelemetry"]) {
    await diagnostics.getByRole("link", { name, exact: true }).click();
    await expect(navLink("Diagnostics")).toHaveAttribute("aria-current", "page");
    await expect(diagnostics.getByRole("link", { name, exact: true })).toHaveAttribute("aria-current", "page");
    await expect(page.getByText("This route is unavailable.", { exact: true })).toHaveCount(0);
  }
  await capture(page, info, "diagnostic-views");
  const search = sidebar.getByRole("searchbox", { name: "Search Studio", exact: true });
  await search.fill("templates");
  await expect(navLink("Templates")).toBeVisible();
  await capture(page, info, "search");
  await search.press("Escape");
  // Search must enter host configuration through native SPA navigation, as integrations do.
  const documentEpoch = await page.evaluate(() => performance.timeOrigin);
  await search.fill("Features");
  await page.getByRole("navigation", { name: /^(Settings|Search results)$/ }).getByRole("link", { name: "Features", exact: true }).filter({ visible: true }).click();
  await expect(page).toHaveURL(/\/features(?:\?|$)/);
  await search.fill("");
  await page.getByRole("tab", { name: "Studio", exact: true }).click();
  await expect(page.getByRole("searchbox", { name: "Filter features", exact: true })).toBeVisible();
  await expect(page.locator(".feature-management-list")).toHaveAttribute("aria-busy", "false");
  await expect(page.getByRole("switch").first()).toBeVisible();
  expect(await page.evaluate(() => performance.timeOrigin)).toBe(documentEpoch);
  await capture(page, info, "features-navigation");
  if (desktop) {
    await sidebar.getByRole("button", { name: "Build", exact: true }).click();
    await expect(navLink("Agents")).toBeHidden();
    await page.reload();
    await expect(sidebar.getByRole("button", { name: "Build", exact: true })).toHaveAttribute("aria-expanded", "false");
    await sidebar.getByRole("button", { name: "Build", exact: true }).press("Enter");
    await sidebar.getByRole("button", { name: "Collapse navigation", exact: true }).click();
    await expect(navLink("Library")).toBeVisible();
    await sidebar.getByRole("button", { name: "Expand navigation", exact: true }).click();
  }
  await navLink("Connections").click();
  await expect(page.getByRole("table", { name: "Connections", exact: true })).not.toHaveAttribute("aria-busy", "true");
  await capture(page, info, "completed");
  // Verify the same real module composition through the alternate navigation and contrast axes.
  await page.goto("/settings/appearance");
  await page.getByRole("radiogroup", { name: "Navigation", exact: true }).getByRole("radio", { name: "Top", exact: true }).click();
  await expect(page.locator("html")).toHaveAttribute("data-nav-mode", "top");
  const topSearch = sidebar.getByRole("searchbox", { name: "Search Studio", exact: true });
  const topFrame = () => sidebar.evaluate(node => {
    const topbar = document.querySelector(".topbar"), content = document.querySelector(".content");
    if (!topbar || !content) throw new Error("The composed shell frame is missing");
    return { stripHeight: node.getBoundingClientRect().height, topbarTop: topbar.getBoundingClientRect().top, contentTop: content.getBoundingClientRect().top };
  });
  await expect(topSearch).toHaveValue("");
  const ordinaryTopFrame = await topFrame();
  const assertTopFrame = async () => {
    await expect.poll(async () => {
      const current = await topFrame();
      return Math.max(...Object.keys(ordinaryTopFrame).map(key => Math.abs(current[key as keyof typeof current] - ordinaryTopFrame[key as keyof typeof ordinaryTopFrame])));
    }, { message: "Search and dismissal must preserve the ordinary navigation height and composed frame position" }).toBeLessThanOrEqual(1);
  };
  await topSearch.fill("templates");
  const topResults = page.getByRole("navigation", { name: "Search results", exact: true });
  const templateResult = topResults.getByRole("link", { name: "Templates", exact: true });
  await expect(templateResult).toBeInViewport({ ratio: 1 });
  await expect(templateResult.locator(".nav-label")).toBeInViewport({ ratio: 1 });
  expect(await templateResult.evaluate(node => {
    const label = document.createRange(); label.selectNodeContents(node.querySelector(".nav-label")!);
    const box = label.getBoundingClientRect();
    return box.left >= 0 && box.right <= innerWidth && node.contains(document.elementFromPoint((box.left + box.right) / 2, (box.top + box.bottom) / 2));
  })).toBe(true);
  await assertTopFrame();
  await capture(page, info, "top-search");
  await topSearch.press("Escape");
  await expect(topResults).toBeHidden();
  await expect(topSearch).toBeFocused();
  await assertTopFrame();
  await topSearch.fill("");
  const headerHeight = await sidebar.evaluate(node => node.getBoundingClientRect().height);
  const absentDestination = "no-such-navigation-acceptance-long-unbroken-destination";
  await topSearch.fill(absentDestination);
  const topNoMatch = topResults.getByRole("status");
  const assertNoMatch = async () => {
    await expect(topNoMatch).toHaveText(`No destinations match "${absentDestination}".`);
    await expect(topResults).toBeVisible();
    await expect(topNoMatch).toBeInViewport({ ratio: 1 });
    expect(await topNoMatch.evaluate(node => {
      const range = document.createRange(); range.selectNodeContents(node);
      const surface = node.closest(".nav-search-results")!.getBoundingClientRect();
      return Array.from(range.getClientRects()).every(rect => rect.left >= 0 && rect.right <= innerWidth &&
        rect.top >= 0 && rect.bottom <= innerHeight && rect.left >= surface.left && rect.right <= surface.right &&
        rect.top >= surface.top && rect.bottom <= surface.bottom &&
        node.contains(document.elementFromPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2))) &&
        node.scrollWidth <= node.clientWidth + 1;
    }), "The complete no-match message must wrap inside the visible search surface").toBe(true);
  };
  await assertNoMatch();
  expect(Math.abs(await sidebar.evaluate(node => node.getBoundingClientRect().height) - headerHeight), "No-match feedback must not shift the navigation strip").toBeLessThanOrEqual(1);
  await assertTopFrame();
  await capture(page, info, "top-search-no-match");
  await topSearch.press("Escape");
  await expect(topResults).toBeHidden();
  await expect(topNoMatch).toBeHidden();
  await expect(topSearch).toBeFocused();
  await expect(topSearch).toHaveValue(absentDestination);
  await assertTopFrame();
  await capture(page, info, "top-search-no-match-dismissed");
  // An edit releases dismissal even when a later edit restores the previously dismissed query.
  await topSearch.fill("templates");
  await expect(templateResult).toBeInViewport({ ratio: 1 });
  await expect(topSearch).toBeFocused();
  await assertTopFrame();
  await capture(page, info, "top-search-query-edited");
  await topSearch.fill(absentDestination);
  await assertNoMatch();
  await expect(topSearch).toBeFocused();
  await assertTopFrame();
  await capture(page, info, "top-search-query-returned");
  await topSearch.press("Escape");
  await expect(topResults).toBeHidden();
  await expect(topNoMatch).toBeHidden();
  await expect(topSearch).toBeFocused();
  await expect(topSearch).toHaveValue(absentDestination);
  await assertTopFrame();
  // Input-to-outside keyboard traversal must dismiss without stealing outside focus.
  await topSearch.fill(absentDestination + "-tab");
  await expect(topNoMatch).toBeVisible();
  await topSearch.press("Tab");
  const afterTab = await page.evaluateHandle(() => document.activeElement);
  expect(await afterTab.evaluate(node => node instanceof HTMLElement && node !== document.querySelector('input[aria-label="Search Studio"]') && !node.closest(".nav-search-results"))).toBe(true);
  await expect(topResults).toBeHidden();
  await expect(topSearch).toHaveValue(absentDestination + "-tab");
  await page.keyboard.press("Escape");
  expect(await afterTab.evaluate(node => node === document.activeElement)).toBe(true);
  await afterTab.dispose();
  await capture(page, info, "top-search-tab-dismissed");
  await topSearch.fill("templates");
  await expect(templateResult).toBeVisible();
  await topSearch.press("Shift+Tab");
  const beforeSearch = await page.evaluateHandle(() => document.activeElement);
  expect(await beforeSearch.evaluate(node => node instanceof HTMLElement && node !== document.querySelector('input[aria-label="Search Studio"]') && !node.closest(".nav-search-results"))).toBe(true);
  await expect(topResults).toBeHidden();
  await expect(topSearch).toHaveValue("templates");
  await page.keyboard.press("Escape");
  expect(await beforeSearch.evaluate(node => node === document.activeElement)).toBe(true);
  await beforeSearch.dispose();
  await capture(page, info, "top-search-shift-tab-dismissed");
  await topSearch.fill("");
  await topSearch.fill("templates");
  await topSearch.press("Tab");
  await expect(templateResult).toBeFocused();
  await page.keyboard.press("Shift+Tab");
  await expect(topSearch).toBeFocused();
  await expect(topResults).toBeVisible();
  await topSearch.press("Escape");
  await expect(topResults).toBeHidden();
  await expect(topSearch).toBeFocused();
  await topSearch.fill("");
  await sidebar.getByRole("button", { name: "Build", exact: true }).click();
  await sidebar.getByRole("navigation", { name: "Build", exact: true }).getByRole("link", { name: "Library", exact: true }).click();
  await expect(page.getByRole("table", { name: "Activity Definitions", exact: true })).toBeVisible();
  await capture(page, info, "top-navigation");
  await page.goto("/settings/appearance");
  await page.getByRole("radiogroup", { name: "Colour mode", exact: true }).getByRole("radio", { name: "High contrast", exact: true }).click();
  await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "high-contrast");
  await sidebar.getByRole("button", { name: "Integrations", exact: true }).click();
  await sidebar.getByRole("navigation", { name: "Integrations", exact: true }).getByRole("link", { name: "Connections", exact: true }).click();
  await expect(page.getByRole("table", { name: "Connections", exact: true })).not.toHaveAttribute("aria-busy", "true");
  await sidebar.getByRole("button", { name: "Integrations", exact: true }).click();
  await capture(page, info, "high-contrast");
  await page.keyboard.press("Escape");
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

test("outlined Secret create journey", async ({ page }, info) => {
  const name = `outlined-${info.project.name}-${Date.now()}`;
  const base = process.env.ELSA_WORKBENCH_URL!;
  expect(base, "Owned Workbench URL").toBeTruthy();
  ownedCleanups.get(page)!.push(async () => {
    const removed = await page.request.delete(`${base}/secrets/${encodeURIComponent(name)}`, { timeout: 5_000 });
    expect([204, 404], `Cleanup for owned secret ${name}`).toContain(removed.status());
  });
  await page.goto("/security/secrets");
  const open = page.getByRole("button", { name: "Create secret", exact: true }).first();
  await expect(open).toBeEnabled();
  await open.press("Enter");
  const dialog = page.getByRole("dialog", { name: "Create secret", exact: true });
  const technicalName = dialog.getByLabel("Technical name", { exact: true });
  await expect(technicalName).toBeFocused();
  await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeInViewport();
  await capture(page, info, "secret-empty");
  // Value is filled first, so only the name can keep Create disabled.
  await dialog.getByLabel("Value", { exact: true }).fill("disposable-outlined-acceptance-value");
  await technicalName.fill("invalid name");
  await expect(technicalName).toHaveAttribute("aria-invalid", "true");
  await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeDisabled();
  await capture(page, info, "secret-invalid");
  await technicalName.fill(name);
  await expect(technicalName).not.toHaveAttribute("aria-invalid", "true");
  await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeEnabled();
  await expect(dialog.getByLabel("Value", { exact: true })).toHaveAttribute("type", "password");
  await capture(page, info, "secret-filled");
  await dialog.getByRole("button", { name: /^More options/ }).click();
  await dialog.getByLabel("Display name", { exact: true }).fill(`Outlined ${info.project.name}`);
  await dialog.getByLabel("Description", { exact: true }).fill("Metadata retained through creation and reopening.");
  await dialog.getByLabel("Scope", { exact: true }).selectOption({ label: "Custom…" });
  await dialog.getByLabel("Custom scope value", { exact: true }).fill("Team:Outlined");
  await dialog.getByLabel("Scope", { exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "secret-options");
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(open).toBeFocused();
  const edit = page.getByRole("button", { name: `Edit ${name}`, exact: true });
  await expect(edit).toBeVisible();
  await capture(page, info, "secret-list");
  await edit.click();
  const editor = page.getByRole("dialog", { name: "Edit secret", exact: true });
  await expect(editor.getByLabel("Display name", { exact: true })).toHaveValue(`Outlined ${info.project.name}`);
  await expect(editor.getByLabel("Description", { exact: true })).toHaveValue("Metadata retained through creation and reopening.");
  await expect(editor.getByLabel("Scope", { exact: true })).toHaveValue("Team:Outlined");
  await capture(page, info, "secret-reopened");
  await editor.getByRole("button", { name: "Cancel", exact: true }).click();
  await open.press("Enter");
  await dialog.getByLabel("Technical name", { exact: true }).fill("cancelled-draft");
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
  await expect(open).toBeFocused();
  await capture(page, info, "secret-cancelled");
});

test("executable designer parity", async ({ page }, info) => {
  await executableDesignerJourney(page, info, capture, id => designerDefinitions.get(page)!.push(id));
});

test("choose data for activity outputs", async ({ page }, info) => {
  await chooseDataJourney(page, info, capture, cleanup => { ownedCleanups.get(page)!.push(cleanup); });
});
