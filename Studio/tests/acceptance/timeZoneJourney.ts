import { expect, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;

/** Real publication, UI commands, retained input evidence and date filters; no fixture responses. */
export async function timeZoneJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const base = process.env.ELSA_WORKBENCH_URL!;
  expect(base, "Owned backend URL").toBeTruthy();
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${base}${path}`, { method, data, timeout: 15_000 });
    const text = await response.text();
    expect(response.ok(), `${method} ${path}: ${response.status()} ${text}`).toBe(true);
    return text ? JSON.parse(text) as T : undefined as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  const definitions: string[] = [];
  registerCleanup(async () => {
    const failures: unknown[] = [];
    for (const id of definitions) {
      try { await api(`/design/workflows/definitions/${id}`, "DELETE"); }
      catch (error) { failures.push(error); }
    }
    if (failures.length) throw new AggregateError(failures, "Timezone journey cleanup failed");
  });

  await page.goto("/settings/appearance");
  const picker = () => page.getByLabel("Time zone", { exact: true });
  await expect(picker()).toHaveValue("browser");
  await capture(page, info, "settings-browser");
  await picker().focus();
  await picker().press("Home");
  await picker().press("ArrowDown");
  await expect(picker()).toHaveValue("UTC");
  await picker().selectOption("Europe/Amsterdam");
  await expect(page.locator("time")).toContainText(/UTC\+0[12]:00/);
  await capture(page, info, "settings-amsterdam");
  await page.reload();
  await expect(picker()).toHaveValue("Europe/Amsterdam");
  await capture(page, info, "settings-persisted");

  const type = "Elsa.Activities.Primitives.Activities.WriteLine";
  const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(type)}`);
  const line = catalog.items.find(item => item.definition.activityTypeKey === type);
  expect(line, "Published WriteLine catalog entry").toBeTruthy();
  const submitted = await api<{ definition: { id: string }; version: { id: string } }>("/design/workflows/definitions/submit", "POST", {
    name: `Timezone input ${info.project.name}-${crypto.randomUUID().slice(0, 8)}`,
    state: {
      variables: [], outputs: [],
      inputs: [{ referenceKey: "when", name: "when", displayName: "When", description: "The instant to retain", category: "", uiHint: "", storageDriverType: null,
        type: { alias: "DateTimeOffset", collectionKind: "Single" }, isRequired: true, isNullable: false }],
      rootActivity: { nodeId: "consumer", activityVersionId: line!.definition.headVersionId, outputs: [],
        inputs: [{ referenceKey: "text", value: { expressionType: "Input", value: { referenceKey: "when" } } }] }
    }
  });
  definitions.push(submitted.definition.id);
  const publication = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
  const executePath = `/runtime/workflows/executables/${publication.artifactId}/execute`;
  const executeUrl = `${base}${executePath}`;
  const inspectorUrl = `/workflows/executables/${publication.artifactId}?backend=legacy&ref=${publication.sourceReferenceId}`;
  const starts: string[] = [];
  page.on("request", request => { if (request.method() === "POST" && new URL(request.url()).pathname === executePath) starts.push(request.postData() ?? ""); });

  async function openRun() {
    await page.goto(inspectorUrl);
    const button = page.getByRole("button", { name: "Run", exact: true }).first();
    await expect(button).toBeEnabled();
    await button.focus();
    await button.press("Enter");
    const dialog = page.getByRole("dialog", { name: "Run workflow", exact: true });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByLabel("When", { exact: true })).toBeFocused();
    return dialog;
  }
  let dialog = await openRun();
  await expect(dialog).toContainText("Europe/Amsterdam");
  await dialog.getByLabel("When", { exact: true }).fill("2026-03-29T02:30");
  const beforeInvalid = starts.length;
  await dialog.getByRole("button", { name: "Run workflow", exact: true }).click();
  await expect(dialog.getByLabel("When", { exact: true })).toHaveAttribute("aria-invalid", "true");
  await expect(dialog).toContainText("does not exist in Europe/Amsterdam");
  await expect(dialog.getByLabel("When", { exact: true })).toHaveValue("2026-03-29T02:30");
  expect(starts).toHaveLength(beforeInvalid);
  await capture(page, info, "run-gap-error");
  await dialog.press("Escape");
  await expect(page.getByRole("button", { name: "Run", exact: true }).first()).toBeFocused();

  async function run(wallTime: string, expected: string, name: string, pause = false) {
    dialog = await openRun();
    await dialog.getByLabel("When", { exact: true }).fill(wallTime);
    await capture(page, info, name);
    let release!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; });
    if (pause) await page.route(executeUrl, async route => { await gate; await route.continue(); });
    const response = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname === executePath);
    const request = page.waitForRequest(request => request.method() === "POST" && new URL(request.url()).pathname === executePath);
    try {
      await dialog.getByRole("button", { name: "Run workflow", exact: true }).click();
      expect((await request).postDataJSON().inputs.when).toBe(expected);
      if (pause) {
        await expect(page.getByRole("button", { name: "Running...", exact: true }).first()).toBeDisabled();
        await capture(page, info, "run-busy");
      }
    } finally { release(); }
    const result = await response;
    expect(result.ok(), await result.text()).toBe(true);
    if (pause) await page.unroute(executeUrl);
    const runId = (await result.json() as { workflowExecutionId: string }).workflowExecutionId;
    const instance = `/runtime/workflows/instances/${runId}`;
    await expect.poll(async () => (await api<{ instance: { status: string } }>(instance)).instance.status, { timeout: 60_000 }).toBe("Completed");
    const executions = await api<{ items: { execution: { activityExecutionId: string; authoredActivityId: string } }[] }>(`${instance}/activity-executions`);
    const consumer = executions.items.find(item => item.execution.authoredActivityId === "consumer")!.execution.activityExecutionId;
    const detail = await api<{ valueSnapshots: { evidenceId: string; inputKey: string | null }[] }>(`${instance}/activity-executions/${consumer}`);
    const evidence = detail.valueSnapshots.find(value => value.inputKey === "text");
    expect(evidence, "Retained consumer date input").toBeTruthy();
    const retained = await api<{ payload: { preview: string } }>(`${instance}/activity-executions/${consumer}/value-evidence/${evidence!.evidenceId}/payload`);
    expect(retained.payload.preview).toBe(expected);
    return runId;
  }
  await run("2026-01-15T09:30", "2026-01-15T09:30:00+01:00", "run-winter");
  await run("2026-07-15T09:30", "2026-07-15T09:30:00+02:00", "run-summer");
  const amsterdamRun = await run("2026-10-25T02:30", "2026-10-25T02:30:00+02:00", "run-fold");
  async function inspectCreated(runId: string, offset: RegExp, name: string) {
    await page.goto(`/workflows/instances/${runId}`);
    const panel = page.getByRole("complementary", { name: "Run details panel", exact: true });
    await expect(panel).toBeVisible();
    await panel.getByRole("tab", { name: "Details", exact: true }).click();
    const created = panel.locator("dt").filter({ hasText: /^Created$/ }).locator("xpath=following-sibling::dd[1]");
    await expect(created).toContainText(offset);
    await capture(page, info, name);
  }
  await inspectCreated(amsterdamRun, /UTC\+0[12]:00/, "run-result-amsterdam");
  await page.goto("/settings/appearance");
  await picker().selectOption("UTC");
  const lastRun = await run("2026-07-15T09:30:12.345", "2026-07-15T09:30:12.345+00:00", "run-utc", true);
  await inspectCreated(lastRun, /UTC\+00:00/, "run-result");

  const rejected = await page.request.post(executeUrl, { data: { sourceReferenceId: publication.sourceReferenceId, inputs: { when: "2026-01-15T09:30" } } });
  expect(rejected.status()).toBe(400);
  const rejection = await rejected.text();
  expect(rejection).toContain("json-datetime-offset-missing");
  expect(rejection).toContain("when");
  await info.attach("offsetless-api-refusal.json", { body: rejection, contentType: "application/json" });

  await page.goto("/settings/appearance");
  await picker().selectOption("Europe/Amsterdam");
  await page.goto("/workflows/instances");
  await page.getByRole("button", { name: /^More filters/ }).click();
  await page.getByLabel("From", { exact: true }).fill("2026-01-15T09:30");
  await page.getByLabel("To", { exact: true }).fill("2026-07-15T09:30");
  const filtered = page.waitForRequest(request => request.method() === "GET" && request.url().includes("/runtime/workflows/instances") && new URL(request.url()).searchParams.has("from"));
  const filteredResponse = page.waitForResponse(response => response.request().method() === "GET" && response.url().includes("/runtime/workflows/instances") && new URL(response.url()).searchParams.has("from"));
  await page.getByRole("button", { name: "Apply filters", exact: true }).click();
  const query = new URL((await filtered).url()).searchParams;
  expect(query.get("from")).toBe("2026-01-15T08:30:00.000Z");
  expect(query.get("to")).toBe("2026-07-15T07:30:00.000Z");
  const filterResult = await filteredResponse;
  expect(filterResult.ok(), await filterResult.text()).toBe(true);
  await expect(page.getByRole("status", { name: "Loading workflow runs" })).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "No workflow runs match these filters", exact: true })).toBeVisible();
  await capture(page, info, "date-filters");

  await page.goto("/settings/appearance");
  await page.getByRole("button", { name: "Reset appearance", exact: true }).click();
  await expect(picker()).toHaveValue("browser");
  await capture(page, info, "reset-browser");
  await page.evaluate(() => localStorage.setItem("elsa-studio-time-zone", "Invalid/Unregistered"));
  await page.reload();
  await expect(picker()).toHaveValue("browser");
  await capture(page, info, "invalid-zone-fallback");
  await info.attach("run-requests.json", { body: JSON.stringify(starts.map(value => JSON.parse(value)), null, 2), contentType: "application/json" });
}
