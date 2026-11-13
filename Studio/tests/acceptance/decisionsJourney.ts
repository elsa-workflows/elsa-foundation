import { readFileSync } from "node:fs";
import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
interface Asset { id: string; revision: string; sourceHash: string }
interface Version { id: string; number: number }
interface Receipt { passed: boolean; comparisons: { passed: boolean; status: string; expected?: unknown; actual?: unknown; failurePath?: string | null }[] }

/** Uses normal authored assets, authentication and server operations; no fixture responses or direct database writes. */
export async function decisionsJourney(page: Page, info: TestInfo, capture: Capture) {
  const backend = process.env.ELSA_WORKBENCH_URL ?? process.env.UX_BACKEND_URL;
  if (!backend || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Decisions acceptance requires the runner's owned disposable hosts.");
  if (info.project.name.startsWith("narrow")) await page.setViewportSize({ width: 320, height: 1000 });
  const surface = page.locator(".decisions-page:visible");
  const button = (name: string) => surface.getByRole("button", { name, exact: true });
  // Each Decisions workspace tab is named for what it holds, so several open tabs stay distinguishable.
  const activeTab = page.getByRole("tablist", { name: "Open screens", exact: true }).getByRole("tab", { selected: true });
  const tabTitle = (title: string) => expect(activeTab).toHaveAttribute("aria-label", title);
  const suffix = `${info.project.name}-${Date.now()}`;
  let caseCount = 0;
  async function part(name: string, locator: Locator) {
    await expect(locator).toBeVisible(); await locator.scrollIntoViewIfNeeded();
    await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => resolve())));
    await capture(page, info, name);
  }
  async function confirm(name: string) {
    const dialog = page.getByRole("dialog").or(page.getByRole("alertdialog"));
    await expect(dialog).toBeVisible(); await dialog.getByRole("button", { name, exact: true }).click();
  }
  async function save(): Promise<Asset> {
    const response = page.waitForResponse(value => ["POST", "PUT"].includes(value.request().method()) && /\/decisions(?:\/[^/?]+)?$/.test(new URL(value.url()).pathname));
    await button("Save decision").click(); const result = await response; expect(result.ok(), await result.text()).toBe(true);
    await expect(surface.getByText("Unsaved changes", { exact: true })).toHaveCount(0); return result.json();
  }
  async function run(passed: boolean): Promise<Receipt> {
    const response = page.waitForResponse(value => value.request().method() === "POST" && new URL(value.url()).pathname.endsWith("/test"));
    await button("Run saved cases").click(); const result = await response; expect(result.ok(), await result.text()).toBe(true);
    const receipt = await result.json() as Receipt; expect(receipt.passed).toBe(passed);
    await expect(surface.getByRole("heading", { name: passed ? "All cases passed" : "Cases failed", exact: true })).toBeVisible();
    if (info.project.name.startsWith("narrow")) {
      const explanation = surface.getByRole("region", { name: "Actual rule evaluation, scroll horizontally for input tests", exact: true }).first();
      await expect(explanation).toBeVisible();
      expect(await explanation.getByRole("rowheader").first().evaluate(element => element.getBoundingClientRect().width)).toBeGreaterThanOrEqual(120);
      expect(await explanation.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
    }
    return receipt;
  }
  async function addCase(amount: string, category: string, expected: string, status = "Success") {
    await button("Add case").click(); const form = surface.getByRole("form", { name: "New decision case" });
    if (caseCount === 0) {
      await expect(form.getByLabel("Case name", { exact: true })).toBeFocused();
      await form.getByLabel("Case name", { exact: true }).fill("Discard this case"); await page.keyboard.press("Escape");
      await expect(form).toHaveCount(0); await expect(button("Add case")).toBeFocused(); await button("Add case").click();
    }
    await form.getByLabel("Case name", { exact: true }).fill(`Boundary ${amount} ${category}`);
    await expect(button("Add case")).toBeDisabled();
    for (const edit of await surface.getByRole("button", { name: /^Edit Boundary / }).all()) await expect(edit).toBeDisabled();
    await expect(form.getByLabel("Case name", { exact: true })).toHaveValue(`Boundary ${amount} ${category}`);
    await form.getByRole("textbox", { name: "Case input amount", exact: true }).fill(amount);
    await form.getByRole("textbox", { name: "Case input customerCategory", exact: true }).fill(category);
    await form.getByRole("combobox", { name: "Expected outcome", exact: true }).selectOption(status);
    if (status === "Success") await form.getByRole("textbox", { name: "Expected approvalLevel", exact: true }).fill(expected);
    if (caseCount === 0) { await part("decision-case-editor", form.getByLabel("Case name", { exact: true })); await part("decision-case-expectation", form.getByRole("textbox", { name: "Expected approvalLevel", exact: true })); }
    const response = page.waitForResponse(value => value.request().method() === "POST" && new URL(value.url()).pathname.endsWith("/cases"));
    await form.getByRole("button", { name: "Save case", exact: true }).click(); const saved = await response;
    expect(saved.ok(), await saved.text()).toBe(true); const item = await saved.json();
    expect(item.inputs.amount).toEqual({ kind: "number", value: amount });
    expect(item.inputs.customerCategory).toEqual({ kind: "string", value: category });
    expect(item.name).toBe(`Boundary ${amount} ${category}`);
    if (status === "NoMatch") expect(item.expectedResult).toEqual({ kind: "null", value: null });
    await expect(form).toHaveCount(0);
    caseCount++;
  }

  const login = await page.request.post(`${backend}/_elsa/identity/login`, { data: { username: "admin", password: "Password123!" } });
  expect(login.ok()).toBe(true);
  const capabilities = await page.request.get(`${backend}/capabilities`); expect(capabilities.ok()).toBe(true);
  expect((await capabilities.json()).capabilities).toEqual(expect.arrayContaining([expect.objectContaining({ id: "elsa.api.decisions", contractVersion: "1" })]));

  await page.goto("/");
  const sidebar = page.locator(".sidebar");
  const build = sidebar.getByRole("button", { name: "Build", exact: true });
  await expect(build).toBeVisible();
  if (await build.getAttribute("aria-expanded") !== "true") await build.click();
  const decisions = sidebar.getByRole("navigation", { name: "Build", exact: true }).getByRole("link", { name: "Decisions", exact: true });
  await expect(decisions).toBeVisible();
  await decisions.click();
  await expect(surface.locator("#decisions-title")).toHaveText("Decisions");
  await expect(surface.locator(".studio-skeleton-list")).toHaveCount(0); await part("decisions-list", surface.locator("#decisions-title"));
  await tabTitle("Decisions");
  // The shared search shell draws the only border; the field inside it must not add a second one.
  const search = surface.getByRole("searchbox", { name: "Search decisions", exact: true });
  expect(await search.evaluate(element => { const style = getComputedStyle(element); return [style.borderTopWidth, style.backgroundColor, style.paddingLeft]; })).toEqual(["0px", "rgba(0, 0, 0, 0)", "0px"]);
  expect(await search.evaluate(element => getComputedStyle(element.closest(".studio-search")!).borderTopWidth)).toBe("1px");
  // Focused, the shell draws the one focus ring and the field draws none.
  await search.focus();
  expect(await search.evaluate(element => { const field = getComputedStyle(element); return [field.outlineStyle, field.boxShadow, getComputedStyle(element.closest(".studio-search")!).outlineStyle]; })).toEqual(["none", "none", "solid"]);
  await part("decisions-search", search);
  if ((info.project.use.viewport?.width ?? 0) >= 1440) {
    const entry = page.getByRole("link", { name: "Decisions", exact: true });
    if (!await entry.isVisible()) await page.getByRole("button", { name: /More tools/ }).click();
    await expect(entry.locator("svg.lucide-scale")).toBeVisible();
  }
  await button("New decision").click(); await expect(surface.getByRole("heading", { name: "New decision", exact: true })).toBeVisible();
  await expect(surface.getByText("Unsaved decision", { exact: true })).toBeVisible(); await expect(surface.getByText("Saved draft", { exact: true })).toHaveCount(0);
  await tabTitle("New decision");
  await part("decision-create", surface.getByLabel("Name", { exact: true }));
  await button("Back to decisions").click(); await expect(button("New decision")).toBeFocused(); await tabTitle("Decisions");
  await button("New decision").click(); await surface.getByLabel("Name", { exact: true }).fill(`Approval policy ${suffix}`);
  await surface.getByLabel("Description", { exact: true }).fill("Determine purchase approval requirements.");
  await surface.getByRole("textbox", { name: "Output 1 name", exact: true }).fill("approvalLevel");
  await button("Add input").click(); await surface.getByRole("textbox", { name: "Input 2 name", exact: true }).fill("customerCategory");
  for (let index = 0; index < 3; index++) await button("Add rule").click();
  const rules = [["<= 1000", "-", '"Automatic"'], ["(1000..10000]", "-", '"Manager"'], ["> 10000", '"Standard"', '"Finance"'], ["> 10000", '"Strategic"', '"Account owner"']];
  for (const [index, row] of rules.entries()) {
    await surface.getByRole("textbox", { name: `Rule ${index + 1}, input amount`, exact: true }).fill(row[0]);
    await surface.getByRole("textbox", { name: `Rule ${index + 1}, input customerCategory`, exact: true }).fill(row[1]);
    await surface.getByRole("textbox", { name: `Rule ${index + 1}, output approvalLevel`, exact: true }).fill(row[2]);
  }
  const asset = await save(); await tabTitle(`Approval policy ${suffix} · Draft`); await button("Validate").click();
  await expect(surface.getByText("The saved draft is valid for the supported DMN profile.", { exact: true })).toBeVisible();
  await page.reload(); await expect(surface.getByRole("textbox", { name: "Rule 4, output approvalLevel", exact: true })).toHaveValue('"Account owner"');
  await tabTitle(`Approval policy ${suffix} · Draft`);
  await part("decision-table", surface.getByRole("heading", { name: "Decision table", exact: true }));

  for (const values of [["1000", "Standard", "Automatic"], ["1000.01", "Standard", "Manager"], ["10000", "Standard", "Manager"], ["10000.01", "Standard", "Finance"], ["10000.01", "Strategic", "Account owner"], ["1234567890123456789012345678901234", "Standard", "Finance"]]) await addCase(values[0], values[1], values[2]);
  await addCase("10000.01", "Unlisted", "", "NoMatch");
  const initialCases = await run(true), noMatch = initialCases.comparisons.find(item => item.status === "NoMatch");
  expect(noMatch).toEqual(expect.objectContaining({ passed: true, expected: { kind: "null", value: null }, actual: { kind: "null", value: null } }));
  await part("decision-cases", surface.getByRole("heading", { name: "All cases passed", exact: true }));
  await surface.getByRole("textbox", { name: "Rule 2, input amount", exact: true }).fill("(1000..9999]"); await save();
  const regression = await run(false); expect(regression.comparisons.some(item => !item.passed && item.failurePath)).toBe(true);
  await part("decision-regression", surface.getByRole("heading", { name: "Cases failed", exact: true }));
  await surface.getByRole("textbox", { name: "Rule 2, input amount", exact: true }).fill("(1000..10000]"); await save(); await run(true);

  const publishing = page.waitForResponse(value => value.request().method() === "POST" && new URL(value.url()).pathname.endsWith("/publish"));
  await button("Publish").click(); const publication = await publishing; expect(publication.ok(), await publication.text()).toBe(true);
  const published = await publication.json() as Version;
  await expect(button("Clone to draft")).toBeVisible(); await expect(surface.getByRole("textbox", { name: "Rule 1, input amount", exact: true })).toBeDisabled();
  await tabTitle(`Approval policy ${suffix} · Version ${published.number}`);
  await part("decision-published", surface.locator("#decisions-title"));
  await surface.getByRole("textbox", { name: "Evaluate input amount", exact: true }).fill("9007199254740993");
  await surface.getByRole("textbox", { name: "Evaluate input customerCategory", exact: true }).fill("Standard");
  await button("Evaluate decision").click(); const explanation = surface.getByRole("region", { name: "Decision evaluation explanation" });
  await expect(explanation.getByRole("heading", { name: "Success", exact: true })).toBeVisible(); await expect(explanation.getByRole("cell", { name: "amount: true, customerCategory: true", exact: true })).toBeVisible();
  await part("decision-explanation", explanation.getByRole("heading", { name: "Success", exact: true }));
  await page.reload(); await expect(button("Clone to draft")).toBeVisible();

  const downloadWait = page.waitForEvent("download"); await button("Export DMN").click(); const download = await downloadWait;
  const sourcePath = info.outputPath("approval.dmn"); await download.saveAs(sourcePath); await info.attach("approval.dmn", { path: sourcePath, contentType: "application/xml" });
  await button("Clone to draft").click(); await confirm("Clone to draft"); await expect(surface.getByText("Saved draft", { exact: true })).toBeVisible();
  await expect(surface.getByRole("textbox", { name: "Rule 1, input amount", exact: true })).toBeEnabled(); await part("decision-clone", surface.locator("#decisions-title"));

  const second = await page.context().newPage();
  try {
    await second.goto(page.url()); await second.locator(".decisions-page").getByLabel("Description", { exact: true }).fill("Concurrent editor saved this description.");
    await second.getByRole("button", { name: "Save decision", exact: true }).click(); await expect(second.getByText("Decision saved.", { exact: true })).toBeVisible();
  } finally { await second.close(); }
  await surface.getByRole("textbox", { name: "Rule 1, input amount", exact: true }).fill("<= 999"); await button("Save decision").click();
  await expect(surface.getByText("Your unsaved work is still here.", { exact: false })).toBeVisible();
  await expect(surface.getByRole("textbox", { name: "Rule 1, input amount", exact: true })).toHaveValue("<= 999"); await part("decision-conflict", button("Reload server draft"));
  await button("Reload server draft").click(); await confirm("Reload"); await expect(surface.getByRole("textbox", { name: "Rule 1, input amount", exact: true })).toHaveValue("<= 1000");
  await button("Load test history").click(); const inspect = surface.getByRole("button", { name: /^Inspect run / }).first(); await expect(inspect).toBeVisible(); await inspect.click();
  await expect(surface.getByText("Detailed values were not retained or are unavailable with your permissions.", { exact: true })).toBeVisible();
  await part("decision-history", surface.getByRole("region", { name: "Decision test results" }));

  await button("Back to decisions").click(); await surface.getByRole("searchbox", { name: "Search decisions", exact: true }).fill(`no-match-${suffix}`);
  await expect(surface.getByRole("heading", { name: "No matching decisions", exact: true })).toBeVisible(); await part("decisions-no-matches", surface.getByRole("heading", { name: "No matching decisions", exact: true }));
  await button("Clear search").click(); await page.getByLabel("Import DMN file", { exact: true }).setInputFiles(sourcePath);
  const prompt = page.getByRole("dialog").or(page.getByRole("alertdialog")); await expect(prompt).toBeVisible();
  await prompt.getByRole("textbox").fill(`Imported approval ${suffix}`); await prompt.getByRole("button", { name: "Import", exact: true }).click();
  await expect(surface.getByRole("textbox", { name: "Rule 4, output approvalLevel", exact: true })).toHaveValue('"Account owner"');
  const roundtripWait = page.waitForEvent("download"); await button("Export DMN").click(); const roundtrip = await roundtripWait;
  const roundtripPath = info.outputPath("roundtrip.dmn"); await roundtrip.saveAs(roundtripPath); expect(readFileSync(roundtripPath, "utf8")).toBe(readFileSync(sourcePath, "utf8"));
  await info.attach("roundtrip.dmn", { path: roundtripPath, contentType: "application/xml" }); await part("decision-imported", surface.getByRole("heading", { name: "Decision table", exact: true }));
  const amount = surface.getByRole("textbox", { name: "Rule 1, input amount", exact: true }); await amount.focus(); await page.keyboard.press("Tab");
  await expect(surface.getByRole("textbox", { name: "Rule 1, input customerCategory", exact: true })).toBeFocused();
  await page.setViewportSize({ width: 320, height: 1000 }); await part("decision-table-320", surface.getByRole("heading", { name: "Decision table", exact: true }));
  const table = surface.getByRole("region", { name: "Decision rules, scroll horizontally for more columns", exact: true });
  expect(await table.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
  await table.evaluate(element => { element.scrollLeft = element.scrollWidth; }); await part("decision-table-320-scrolled", table);
  await info.attach("decision-identities", { body: JSON.stringify({ assetId: asset.id, versionId: published.id }), contentType: "application/json" });
  await pickerShortcut(asset.id, `Approval policy ${suffix}`);

  /**
   * A workflow author starts a new decision from the Evaluate Decision picker, saves it in the tab that opens,
   * and returns to the workflow to find the decision just created selected in the picker, with a toast saying so (#4086).
   */
  async function pickerShortcut(existingId: string, existingDecision: string) {
    await page.setViewportSize(info.project.use.viewport ?? { width: 1440, height: 1000 });
    async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
      const response = await page.request.fetch(`${backend}${path}`, { method, data }); const text = await response.text();
      expect(response.ok(), `${method} ${path}: ${response.status()} ${text}`).toBe(true); return text ? JSON.parse(text) as T : undefined as T;
    }
    async function head(key: string) {
      const result = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
      const entry = result.items.find(item => item.definition.activityTypeKey === key); expect(entry, key).toBeTruthy(); return entry!.definition.headVersionId;
    }
    const [sequence, evaluate] = await Promise.all([head("Elsa.Activities.Sequence.Activities.Sequence"), head("Elsa.Decisions.Workflows.Runtime.Activities.EvaluateDecision")]);
    const workflowName = `Decision picker ${suffix}`;
    const submitted = await api<{ definition: { id: string } }>("/design/workflows/definitions/submit", "POST", { name: workflowName,
      state: { inputs: [], outputs: [], variables: [], rootActivity: { nodeId: "root", activityVersionId: sequence, inputs: [], outputs: [],
        structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [{ nodeId: "decide", activityVersionId: evaluate, inputs: [], outputs: [] }] } } } } });
    try {
      await page.goto(`/workflows/definitions?definition=${submitted.definition.id}&backend=legacy`);
      await expect(page.locator('.wf-sequence-step[data-id="decide"]')).toBeVisible();
      // Autosave off, so the pick below stays an unsaved edit that only the open tab holds.
      const saveStatus = page.locator(".wf-editor-save-status");
      await info.attach("save-status-before-autosave-off", { body: await saveStatus.textContent() ?? "", contentType: "text/plain" });
      await page.getByRole("button", { name: "More workflow actions", exact: true }).click();
      const autosave = page.getByRole("menuitemcheckbox", { name: /Autosave/ });
      if (await autosave.getAttribute("aria-checked") === "true") await autosave.click();
      await page.keyboard.press("Escape");
      // Turning autosave off drops a pending autosave, so the header stops claiming one (#4085).
      await expect(saveStatus).not.toContainText("Autosaving");
      await page.locator('.wf-sequence-step[data-id="decide"]').press("Enter");
      if (page.viewportSize()!.width <= 600) await page.getByRole("button", { name: "Expand inspector panel", exact: true }).click();
      const picker = page.getByRole("combobox", { name: "Business decision", exact: true });
      await expect(picker.locator("option", { hasText: existingDecision })).toHaveCount(1);
      await picker.selectOption(existingId); await expect(picker).toHaveValue(existingId);
      const shortcut = page.getByRole("button", { name: "New decision", exact: true });
      await part("decision-picker", shortcut);
      await shortcut.focus(); await page.keyboard.press("Enter");
      const opened = page.getByRole("tablist", { name: "Open screens", exact: true }).getByRole("tab", { selected: true });
      await expect(surface.getByRole("heading", { name: "New decision", exact: true })).toBeVisible();
      await expect(surface.getByText("Unsaved decision", { exact: true })).toBeVisible();
      await tabTitle("New decision");
      await part("decision-picker-new-tab", surface.getByLabel("Name", { exact: true }));
      expect(await opened.getAttribute("aria-label") ?? await opened.textContent()).not.toContain(workflowName);
      const created = `Picker-created policy ${suffix}`;
      await surface.getByLabel("Name", { exact: true }).fill(created); await tabTitle("New decision"); const createdAsset = await save();
      // Saving opens the decision in the same tab, which now carries its name rather than "New decision".
      await tabTitle(`${created} · Draft`); await part("decision-picker-saved-tab", opened);
      // Back on the workflow: the list reloaded and the picker took the decision this New decision flow saved.
      // The header stops claiming an autosave, yet the workflow still reads as unsaved: its tab carries the marker (#4085).
      const workflowTab = page.getByRole("tab", { name: new RegExp(`^${workflowName}`) });
      await expect(workflowTab).toHaveAccessibleName(/unsaved changes/);
      await workflowTab.click();
      await expect(picker).toBeVisible(); await expect(picker).toHaveValue(createdAsset.id);
      // The host's success toast says what changed while the workflow tab was in the background.
      const selectedToast = page.getByRole("group", { name: "Decision selected", exact: true });
      await expect(selectedToast).toContainText(`${created} is now used by this Evaluate Decision activity.`); await selectedToast.hover();
      await part("decision-picker-selected-toast", selectedToast);
      expect(createdAsset.id).not.toBe(existingId);
      await expect(picker.locator("option:checked")).toHaveText(created);
      await expect(page.getByRole("tab", { name: `${created} · Draft`, exact: true })).toHaveCount(1);
      // A fresh decision has no published version yet, and the picker says so beside the new selection.
      const pickerPanel = page.locator(".decision-version-picker");
      await expect(pickerPanel.getByRole("alert").filter({ hasText: "This decision has no published version." })).toBeVisible();
      await expect(saveStatus).not.toContainText("Autosaving");
      await part("decision-picker-returned", pickerPanel);
    } catch (failure) {
      // Clean up best-effort without letting a cleanup failure replace the journey's own error.
      await page.request.delete(`${backend}/design/workflows/definitions/${submitted.definition.id}`).catch(() => undefined);
      throw failure;
    }
    await api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE");
  }
}
