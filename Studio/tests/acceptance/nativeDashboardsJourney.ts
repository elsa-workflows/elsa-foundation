import { chromium, expect, type Locator, type Page, type Request, type TestInfo } from "@playwright/test";
import { attachDashboardJson, dashboardBackend, registerDashboardCleanup, observeNativeWeaverBoundary, seedDashboardRuns, signInDashboardReader, tag, type Capture, type Resource, type Result } from "./dashboardJourneySupport";

/** Native authoring against real Workflow/Identity/Dashboard authorities; no provider or result mocks. */
export async function nativeDashboardsJourney(page: Page, info: TestInfo, capture: Capture) {
  info.setTimeout(300_000);
  const requestedViewport = info.project.use.viewport;
  if (requestedViewport && requestedViewport.width < 700) await page.setViewportSize({ width: 320, height: requestedViewport.height });
  await capture(page, info, "dashboard-native-reference");
  const backendSelection = new URL(page.url()).searchParams.get("backend");
  const expectResourceLocation = (kind: "questions" | "dashboards", id: string) => expect(page).toHaveURL(url =>
    url.pathname === `/${kind}/${id}` && url.searchParams.get("backend") === backendSelection);
  const backend = await dashboardBackend(page.request);
  const suffix = `${info.project.name}-${Date.now()}`;
  // Own the independent reader browser; trace:on records both contexts in the runner trace.
  const readerBrowser = await chromium.launch();
  try {
    const readerContext = await readerBrowser.newContext({ baseURL: process.env.ELSA_STUDIO_URL, viewport: page.viewportSize() ?? undefined, colorScheme: info.project.use.colorScheme });
    await readerContext.addInitScript(mode => { localStorage.removeItem("elsa-studio-theme"); localStorage.setItem("elsa-studio-workspace-style", "linen"); localStorage.setItem("elsa-studio-theme-mode", mode); }, info.project.use.colorScheme ?? "light");
    // A separate browser context provides a real independent password identity and cookie/token scope.
    const reader = await readerContext.newPage();
    // Set up the independent reader before creating records; setup failure leaves the store clean.
    const seeded = await seedDashboardRuns(backend, suffix);
    const resources: { kind: string; id: string }[] = [];
    const boundary = observeNativeWeaverBoundary(page);
    const readerErrors: string[] = []; reader.on("pageerror", error => readerErrors.push(error.message));
    const readerBoundary = observeNativeWeaverBoundary(reader);
    let questionId = ""; let dashboardId = "";
    async function profile(name: "dashboard-native-reader" | "dashboard-native-no-source") {
      const authority = await backend.api<{ revision: string }>("/__qualification/principals/0/authority");
      await backend.api("/__qualification/principals/0/grant-profile", "POST", { profile: name, revision: authority.revision });
    }
    async function share(kind: "questions" | "dashboards", id: string, revoke = false) {
      await page.goto(`/${kind}/${id}`);
      await page.getByRole("button", { name: "Share", exact: true }).click();
      const dialog = page.getByRole("dialog", { name: /^Share / });
      if (revoke) await dialog.getByRole("button", { name: "Remove dashboard-qa-reader", exact: true }).click();
      else {
        await dialog.getByLabel("Find authorized users or roles", { exact: true }).fill("dashboard-reader");
        await dialog.getByRole("button", { name: "Search audience", exact: true }).click();
        const candidate = dialog.getByRole("listitem").filter({ hasText: "dashboard-reader" });
        await candidate.getByRole("button", { name: "Add audience", exact: true }).click();
      }
      if (kind === "dashboards") {
        const dependencies = dialog.getByRole("region", { name: "Included question access review", exact: true });
        await expect(dependencies).toContainText("Tiles 1, 2, 3, 4");
        await expect(dependencies).toContainText("Current question audience: 1 user grants and 0 role grants.");
        await expect(dialog.getByRole("button", { name: "Save audience", exact: true })).toBeEnabled();
        await capture(page, info, "dashboard-share-dependency-review");
        await captureScrollPosition(page, info, capture, "dashboard-share-dependency-review-end", dialog.getByRole("button", { name: "Save audience", exact: true }), "end");
      }
      await capture(page, info, revoke ? "question-revoke" : kind === "questions" ? "question-share" : "dashboard-share");
      await dialog.getByRole("button", { name: "Save audience", exact: true }).click();
      await expect(dialog).toBeHidden();
    }
    registerDashboardCleanup(page, async () => {
      try {
        await profile("dashboard-native-reader");
        for (const resource of [...resources].reverse()) {
          const response = await backend.response(`/_elsa/studio/${resource.kind}/${resource.id}`);
          if (response.status() === 404) continue;
          expect(response.ok()).toBe(true); const current = await response.json() as Resource;
          await backend.api(`/_elsa/studio/${resource.kind}/${resource.id}`, "DELETE", undefined, tag(current.revision));
        }
      } finally {
        const response = await backend.response(`/design/workflows/definitions/${seeded.definitionId}`, "DELETE");
        expect(response.ok() || response.status() === 404).toBe(true);
      }
    });
    try {
      await page.goto("/questions");
      await page.getByRole("searchbox", { name: "Search questions", exact: true }).fill(suffix);
      await page.getByRole("button", { name: "Search", exact: true }).click();
      await expect(page.getByRole("heading", { name: "No matching definitions", exact: true })).toBeVisible();
      await capture(page, info, "questions-empty");
      await page.getByRole("button", { name: "Create question", exact: true }).first().press("Enter");
      await page.getByLabel("Name", { exact: true }).fill(`Started run quality ${suffix}`);
      await page.getByRole("textbox", { name: "Description", exact: true }).fill("A native question over this journey's three actual published workflow runs.");
      await page.getByRole("combobox", { name: "Dataset", exact: true }).selectOption("elsa.workflows.runs");
      await page.getByRole("group", { name: "Group by", exact: true }).getByLabel("Current status", { exact: true }).check();
      await page.getByRole("group", { name: "Group by", exact: true }).getByLabel("Run kind", { exact: true }).check();
      await page.getByRole("combobox", { name: "Metric 1", exact: true }).selectOption("workflow.started-count");
      await page.getByLabel("Result name", { exact: true }).fill("started");
      await page.getByRole("button", { name: "Add metric", exact: true }).click();
      await page.getByRole("combobox", { name: "Metric 2", exact: true }).selectOption("workflow.failed-count");
      await page.getByLabel("Result name", { exact: true }).nth(1).fill("failed");
      await page.getByRole("button", { name: "Add metric", exact: true }).click();
      await page.getByRole("combobox", { name: "Metric 3", exact: true }).selectOption("workflow.failure-rate");
      await page.getByLabel("Result name", { exact: true }).nth(2).fill("rate");
      await page.getByRole("button", { name: "Add parameter", exact: true }).click();
      await page.getByLabel("Parameter name", { exact: true }).fill("workflow");
      await page.getByLabel("Default / preview value", { exact: true }).fill(seeded.definitionId);
      const source = page.getByRole("group", { name: "Source filters", exact: true });
      await source.getByRole("button", { name: "Add condition", exact: true }).click();
      await source.getByRole("combobox", { name: "Field", exact: true }).selectOption("definitionId");
      await source.getByRole("combobox", { name: "Comparison value source", exact: true }).selectOption("parameter");
      await source.getByRole("combobox", { name: "Comparison value parameter", exact: true }).selectOption("workflow");
      await page.getByRole("combobox", { name: "Time field", exact: true }).selectOption("startedAt");
      await page.getByRole("combobox", { name: "Time range", exact: true }).selectOption("last7Days");
      await page.getByLabel("Time zone", { exact: true }).fill("UTC");
      const having = page.getByRole("group", { name: "Result conditions", exact: true });
      await having.getByRole("button", { name: "Add condition", exact: true }).click();
      await having.getByRole("combobox", { name: "Result metric", exact: true }).selectOption("started");
      await having.getByLabel("Comparison value", { exact: true }).fill("0");
      await page.getByRole("button", { name: "Add sort", exact: true }).click();
      await page.getByRole("combobox", { name: "Sort column", exact: true }).selectOption("started");
      await page.getByRole("combobox", { name: "Direction", exact: true }).selectOption("descending");
      await page.getByLabel("Maximum result rows/groups", { exact: true }).fill("25");
      await page.getByRole("combobox", { name: "Visualization", exact: true }).selectOption("bar");
      await page.getByRole("combobox", { name: "Category / x axis", exact: true }).selectOption("status");
      await page.getByRole("listbox", { name: "Value columns", exact: true }).selectOption(["started", "failed", "rate"]);
      await page.getByRole("region", { name: "Ask Weaver", exact: true }).getByLabel("Your question", { exact: true }).fill("A provider-disabled native question remains editable");
      await expect(page.getByRole("button", { name: "Ask Weaver", exact: true })).toBeDisabled();
      await capture(page, info, "question-controls");
      // A real native validation response identifies the invalid field and moves focus there.
      const limit = page.getByLabel("Maximum result rows/groups", { exact: true });
      await limit.fill("0");
      const invalidResponse = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/dashboard-queries/validate"));
      await page.getByRole("button", { name: "Validate question", exact: true }).click();
      const invalid = await invalidResponse; expect(invalid.ok()).toBe(true);
      expect(await invalid.json()).toMatchObject({ isValid: false, problems: expect.arrayContaining([expect.objectContaining({ path: "query.limit" })]) });
      await expect(page.getByRole("group", { name: "Sort and limit", exact: true })).toHaveAttribute("aria-invalid", "true");
      await expect(limit).toBeFocused(); await expect(limit).toHaveValue("0");
      await capture(page, info, "question-validation");
      await limit.fill("25");
      const validResponse = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/dashboard-queries/validate"));
      await page.getByRole("button", { name: "Validate question", exact: true }).click();
      expect(await (await validResponse).json()).toMatchObject({ isValid: true, problems: [] });
      await expect(page.getByRole("group", { name: "Sort and limit", exact: true })).not.toHaveAttribute("aria-invalid", "true");
      await expect(page.getByLabel("Name", { exact: true })).toHaveValue(`Started run quality ${suffix}`);
      const preview = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/dashboard-queries/preview"));
      await page.getByRole("button", { name: "Preview question", exact: true }).click();
      const resultResponse = await preview; expect(resultResponse.ok()).toBe(true);
      const result = await resultResponse.json() as Result;
      expect(result.outcome).toBe("succeeded"); expect(result.coverage.status).toBe("unknown"); expect(result.rows).toHaveLength(1);
      expect(result.rows[0].values).toMatchObject({ status: { value: "completed" }, runKind: { value: "publishedRun" }, started: { value: "3" }, failed: { value: "0" }, rate: { value: "0" } });
      await expect(page.locator(".named-chart-series")).toHaveCount(3);
      await expect(page.getByRole("region", { name: "Exact result values", exact: true }).getByRole("cell", { name: "3", exact: true })).toBeVisible();
      await capture(page, info, "question-preview");
      await captureNativeResultSections(page, info, capture, "question-preview", page.locator(".named-preview > .named-result"));
      await page.getByRole("button", { name: "View matching records for result 1", exact: true }).click();
      const drill = page.getByRole("dialog", { name: "Matching source records", exact: true });
      for (const id of seeded.executionIds) await expect(drill.getByRole("cell", { name: id, exact: true })).toBeVisible();
      await capture(page, info, "question-drill");
      await captureReadableDrillValues(page, info, capture, "question-drill-values", drill);
      await drill.getByRole("button", { name: "Close Matching source records", exact: true }).press("Escape");
      const savedResponse = page.waitForResponse(response => response.request().method() === "POST" && /\/_elsa\/studio\/questions$/.test(response.url()));
      await page.getByRole("button", { name: "Save question", exact: true }).click();
      const saved = await (await savedResponse).json() as Resource; questionId = saved.id; resources.push({ kind: "questions", id: saved.id });
      expect(saved.ownerId).not.toBe("dashboard-qa-reader");
      expect((await backend.api<{ items: unknown[] }>(`/_elsa/studio/questions/${saved.id}/audience`)).items).toEqual([]);
      await expectResourceLocation("questions", saved.id);
      const execute = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/questions/${saved.id}/execute`));
      await page.getByRole("button", { name: "Refresh results", exact: true }).click();
      const executed = await execute; expect(executed.ok()).toBe(true); expect(executed.request().headers()["if-match"]).toBe(`"${saved.revision}"`);
      await capture(page, info, "question-saved");
      const freshRun = await backend.api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${seeded.published.artifactId}/execute`, "POST", { sourceReferenceId: seeded.published.sourceReferenceId });
      await expect.poll(async () => (await backend.api<{ instance: { status: string } }>(`/runtime/workflows/instances/${freshRun.workflowExecutionId}`)).instance.status, { timeout: 30_000 }).toBe("Completed");
      seeded.executionIds.push(freshRun.workflowExecutionId);
      seeded.observedInstances.push({ workflowExecutionId: freshRun.workflowExecutionId, status: (await backend.api<{ instance: { status: string } }>(`/runtime/workflows/instances/${freshRun.workflowExecutionId}`)).instance.status });
      const refreshedResponse = page.waitForResponse(response => response.url().endsWith(`/questions/${saved.id}/execute`));
      await page.getByRole("button", { name: "Refresh results", exact: true }).click();
      const refreshed = await (await refreshedResponse).json() as Result;
      expect(refreshed.rows[0].values.started.value).toBe("4");
      await expect(page.getByRole("region", { name: "Exact result values", exact: true }).getByRole("cell", { name: "4", exact: true })).toBeVisible();
      await capture(page, info, "question-refreshed");
      await captureDashboardAppearances(page, info, capture, "question-editor-preview", async name => {
        await expect(page.getByRole("combobox", { name: "Dataset", exact: true })).toHaveValue("elsa.workflows.runs");
        await expect(page.getByRole("region", { name: "Exact result values", exact: true }).getByRole("cell", { name: "4", exact: true })).toBeVisible();
        await expect(page.getByRole("button", { name: "Save question", exact: true })).toBeEnabled();
        await capture(page, info, name);
        const parameters = page.getByRole("group", { name: "Parameters and accepted defaults", exact: true });
        await captureScrollPosition(page, info, capture, `${name}-parameters`, parameters.getByRole("textbox", { name: "Parameter name", exact: true }));
        await captureScrollPosition(page, info, capture, `${name}-parameters-end`, parameters.getByRole("button", { name: "Remove parameter 1", exact: true }), "end");
        const presentation = page.getByRole("group", { name: "Presentation", exact: true });
        await captureScrollPosition(page, info, capture, `${name}-presentation`, presentation.getByRole("combobox", { name: "Visualization", exact: true }));
        await captureScrollPosition(page, info, capture, `${name}-presentation-end`, presentation.getByRole("textbox", { name: "Display label", exact: true }), "end");
        await captureNativeResultSections(page, info, capture, name, page.locator(".named-preview > .named-result"));
      });
      const savedDrill = page.waitForResponse(response => response.url().endsWith(`/questions/${saved.id}/drill`));
      await page.getByRole("button", { name: "View matching records for result 1", exact: true }).click();
      expect((await savedDrill).ok()).toBe(true); await expect(drill.getByRole("cell", { name: seeded.executionIds[0], exact: true })).toBeVisible(); await drill.press("Escape");
      await page.getByRole("textbox", { name: "Description", exact: true }).fill("Native edit creates the second definition revision.");
      const updated = page.waitForResponse(response => response.request().method() === "PUT" && response.url().endsWith(`/questions/${saved.id}`));
      await page.getByRole("button", { name: "Save question", exact: true }).click();
      const updatedReply = await updated; expect(updatedReply.ok()).toBe(true);
      const updatedQuestion = await updatedReply.json() as Resource;
      await expect(page.getByRole("button", { name: "View matching records for result 1", exact: true })).toHaveCount(0);
      const afterSaveRefresh = page.waitForResponse(response => response.url().endsWith(`/questions/${saved.id}/execute`));
      await page.getByRole("button", { name: "Refresh results", exact: true }).click();
      const afterSaveReply = await afterSaveRefresh; expect(afterSaveReply.ok()).toBe(true);
      expect(afterSaveReply.request().headers()["if-match"]).toBe(tag(updatedQuestion.revision)["If-Match"]);
      const afterSaveDrill = page.waitForResponse(response => response.url().endsWith(`/questions/${saved.id}/drill`));
      await page.getByRole("button", { name: "View matching records for result 1", exact: true }).click();
      const currentDrillReply = await afterSaveDrill; expect(currentDrillReply.ok()).toBe(true);
      expect(currentDrillReply.request().headers()["if-match"]).toBe(tag(updatedQuestion.revision)["If-Match"]);
      await expect(drill.getByRole("cell", { name: seeded.executionIds[0], exact: true })).toBeVisible();
      await capture(page, info, "question-saved-refresh-drill");
      await captureReadableDrillValues(page, info, capture, "question-saved-refresh-drill-values", drill);
      await drill.press("Escape");
      await page.getByRole("button", { name: "History", exact: true }).click();
      const history = page.getByRole("dialog", { name: "Definition history", exact: true });
      await expect(history.getByRole("button", { name: /^Inspect revision / })).toHaveCount(2);
      await history.getByRole("button", { name: `Inspect revision ${saved.revision}`, exact: true }).click();
      await expect(history.locator("pre")).toContainText("workflow.started-count");
      await capture(page, info, "question-history");
      await history.getByRole("button", { name: `Restore revision ${saved.revision}`, exact: true }).click();
      await expect(history).toBeHidden(); await expect(page.getByRole("textbox", { name: "Description", exact: true })).toHaveValue(saved.description);
      await page.getByRole("button", { name: "Copy saved version", exact: true }).click();
      const copy = page.getByRole("dialog", { name: "Save a private copy", exact: true });
      await copy.getByRole("textbox").fill(`Private question copy ${suffix}`);
      const copiedResponse = page.waitForResponse(response => response.url().endsWith(`/questions/${saved.id}/copies`));
      await copy.getByRole("button", { name: "Confirm", exact: true }).click();
      const copied = await (await copiedResponse).json() as Resource; resources.push({ kind: "questions", id: copied.id });
      expect((await backend.api<{ items: unknown[] }>(`/_elsa/studio/questions/${copied.id}/audience`)).items).toEqual([]);
      expect((await backend.api<{ items: unknown[] }>(`/_elsa/studio/questions/${copied.id}/revisions`)).items).toHaveLength(1);
      await expectResourceLocation("questions", copied.id); await capture(page, info, "question-copy");
      await page.getByRole("combobox", { name: "Query form", exact: true }).selectOption("records");
      const columns = page.getByRole("group", { name: "Columns", exact: true });
      for (const label of ["Run", "Current status", "Run kind"]) await columns.getByLabel(label, { exact: true }).check();
      await page.getByRole("combobox", { name: "Visualization", exact: true }).selectOption("table");
      await page.getByRole("listbox", { name: "Value columns", exact: true }).selectOption([]);
      const recordsResponse = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/questions/${copied.id}/preview`));
      await page.getByRole("button", { name: "Preview question", exact: true }).click();
      const recordsReply = await recordsResponse;
      expect(recordsReply.request().headers()["if-match"]).toBe(tag(copied.revision)["If-Match"]);
      const records = await recordsReply.json() as Result;
      expect(records.rows).toHaveLength(4);
      expect(new Set(records.rows.map(row => row.values.executionId.value))).toEqual(new Set(seeded.executionIds));
      await capture(page, info, "question-records");
      const dashboardNavigation = page.getByRole("navigation", { name: "Operate", exact: true }).getByRole("link", { name: "Dashboards", exact: true });
      async function revealDashboardNavigation() {
        if (!await dashboardNavigation.isVisible()) await page.getByRole("button", { name: "Operate", exact: true }).click();
        await expect(dashboardNavigation).toBeVisible();
      }
      await revealDashboardNavigation();
      const cancelPrompt = page.waitForEvent("dialog"); const cancelledNavigation = dashboardNavigation.click();
      const discard = await cancelPrompt; expect(discard.type()).toBe("confirm"); expect(discard.message()).toContain("Discard unsaved changes"); await discard.dismiss(); await cancelledNavigation;
      await expectResourceLocation("questions", copied.id); await expect(page.getByRole("combobox", { name: "Query form", exact: true })).toHaveValue("records");
      await capture(page, info, "question-navigation-cancelled");
      const tabs = page.getByRole("tablist", { name: "Open screens", exact: true });
      const questionTabId = await tabs.getByRole("tab", { selected: true }).getAttribute("data-tab-id"); expect(questionTabId).toBeTruthy();
      await revealDashboardNavigation();
      await dashboardNavigation.click({ modifiers: ["Alt"] }); await expect(page.getByRole("heading", { name: "Dashboards", level: 2, exact: true })).toBeVisible();
      const dashboardTabId = await tabs.getByRole("tab", { selected: true }).getAttribute("data-tab-id"); expect(dashboardTabId).not.toBe(questionTabId);
      await tabs.locator(`[data-tab-id="${questionTabId}"]`).click();
      await expect(page.getByRole("combobox", { name: "Query form", exact: true })).toHaveValue("records");
      await expect(page.getByRole("group", { name: "Columns", exact: true }).getByLabel("Run", { exact: true })).toBeChecked();
      await capture(page, info, "question-retained-tab");
      // Close the clean parked collection so the next ordinary sidebar navigation replaces
      // the dirty editor and must ask its guard, rather than switching to an existing tab.
      await tabs.locator(`[data-tab-id="${dashboardTabId}"]`).press("Delete");
      await expect(tabs.locator(`[data-tab-id="${dashboardTabId}"]`)).toHaveCount(0);
      await revealDashboardNavigation();
      const confirmPrompt = page.waitForEvent("dialog"); const confirmedNavigation = dashboardNavigation.click();
      const confirmedDiscard = await confirmPrompt; expect(confirmedDiscard.type()).toBe("confirm"); await confirmedDiscard.accept(); await confirmedNavigation;
      await expect(page.getByRole("heading", { name: "Dashboards", level: 2, exact: true })).toBeVisible(); await capture(page, info, "question-navigation-confirmed");
      await page.getByRole("button", { name: "Create dashboard", exact: true }).first().click();
      await page.getByLabel("Name", { exact: true }).fill(`Private operations ${suffix}`);
      for (let index = 0; index < 2; index++) {
        await page.getByRole("button", { name: "Add saved question", exact: true }).click();
        const picker = page.getByRole("dialog", { name: "Add saved question", exact: true });
        await picker.getByLabel("Search accessible questions", { exact: true }).fill(saved.name);
        await picker.getByRole("button", { name: "Search questions", exact: true }).click();
        await picker.getByRole("button", { name: `Add ${saved.name}`, exact: true }).click();
      }
      await page.getByRole("combobox", { name: "Size for question 1", exact: true }).selectOption("wide");
      await page.getByRole("button", { name: "Move question 1 later", exact: true }).press("Enter");
      await expect(page.getByRole("button", { name: "Move question 2 earlier", exact: true })).toBeFocused();
      await expect(page.locator(".named-announcement")).toContainText("Tile moved to position 2 of 2");
      await capture(page, info, "dashboard-layout");
      // Keep the two-tile endpoint focus proof, then exercise native admission with four references.
      for (let index = 0; index < 2; index++) {
        await page.getByRole("button", { name: "Add saved question", exact: true }).click();
        const picker = page.getByRole("dialog", { name: "Add saved question", exact: true });
        await picker.getByLabel("Search accessible questions", { exact: true }).fill(saved.name);
        await picker.getByRole("button", { name: "Search questions", exact: true }).click();
        await picker.getByRole("button", { name: `Add ${saved.name}`, exact: true }).click();
      }
      const dashboardResponse = page.waitForResponse(response => response.request().method() === "POST" && /\/_elsa\/studio\/dashboards$/.test(response.url()));
      await page.getByRole("button", { name: "Save layout", exact: true }).click();
      const dashboard = await (await dashboardResponse).json() as Resource; dashboardId = dashboard.id; resources.push({ kind: "dashboards", id: dashboard.id });
      expect(dashboard.tiles.map(tile => tile.questionId)).toEqual([saved.id, saved.id, saved.id, saved.id]); expect(new Set(dashboard.tiles.map(tile => tile.id)).size).toBe(4);
      expect(dashboard.tiles.map(tile => tile.size)).toEqual(["medium", "wide", "medium", "medium"]);
      expect((await backend.api<{ items: unknown[] }>(`/_elsa/studio/dashboards/${dashboard.id}/audience`)).items).toEqual([]);
      await page.reload(); await expect(page.locator(".named-tile .named-result")).toHaveCount(4);
      await expect(page.getByRole("combobox", { name: "Size for question 2", exact: true })).toHaveValue("wide");
      await capture(page, info, "dashboard-reopened");
      await capture(page, info, "dashboard-multi-tile");
      await page.getByRole("button", { name: "Back to dashboards", exact: true }).click();
      await expect(page.getByRole("link", { name: dashboard.name, exact: true })).toBeVisible();
      await captureDashboardAppearances(page, info, capture, "dashboards-collection", async name => {
        await expect(page.getByRole("button", { name: "Create dashboard", exact: true })).toBeVisible();
        await expect(page.getByRole("link", { name: dashboard.name, exact: true })).toBeVisible();
        await capture(page, info, name);
      });
      await page.getByRole("link", { name: dashboard.name, exact: true }).click();
      await expect(page.locator(".named-tile .named-result")).toHaveCount(4);
      await captureDashboardAppearances(page, info, capture, "dashboard-and-dependency-dialogs", async name => {
        await expect(page.locator(".named-tile .named-result")).toHaveCount(4);
        await capture(page, info, `${name}-dashboard`);
        for (let index = 0; index < 4; index++) {
          const tile = page.locator(".named-tile").nth(index);
          await captureScrollPosition(page, info, capture, `${name}-tile-${index + 1}-header`, tile.locator(":scope > header"));
          await captureNativeResultSections(page, info, capture, `${name}-tile-${index + 1}`, tile.locator(".named-result"));
        }
        const shareButton = page.getByRole("button", { name: "Share", exact: true });
        await shareButton.click();
        const shareReview = page.getByRole("dialog", { name: `Share ${dashboard.name}`, exact: true });
        await expect(shareReview.getByRole("region", { name: "Included question access review", exact: true })).toContainText("Tiles 1, 2, 3, 4");
        await expect(shareReview.getByRole("button", { name: "Save audience", exact: true })).toBeEnabled();
        await capture(page, info, `${name}-share-review`);
        await captureScrollPosition(page, info, capture, `${name}-share-review-end`, shareReview.getByRole("button", { name: "Save audience", exact: true }), "end");
        await shareReview.getByRole("button", { name: `Close Share ${dashboard.name}`, exact: true }).press("Escape");
        await expect(shareButton).toBeFocused();
        const copyButton = page.getByRole("button", { name: "Copy saved version", exact: true });
        await copyButton.click();
        const copyReview = page.getByRole("dialog", { name: "Review dashboard copy dependencies", exact: true });
        await expect(copyReview.getByRole("region", { name: "Included question access review", exact: true })).toContainText(saved.name);
        await expect(copyReview.getByRole("button", { name: "Continue with retained references", exact: true })).toBeEnabled();
        await capture(page, info, `${name}-copy-review`);
        await captureScrollPosition(page, info, capture, `${name}-copy-review-end`, copyReview.getByRole("button", { name: "Continue with retained references", exact: true }), "end");
        await copyReview.getByRole("button", { name: "Close Review dashboard copy dependencies", exact: true }).press("Escape");
        await expect(copyButton).toBeFocused();
      });
      // CDP adds controlled network latency only: all requests and result data still reach real APIs.
      // This qualifies visible UI cancellation and late-network-result rejection, not server compute cancellation.
      const network = await page.context().newCDPSession(page);
      const firstTile = page.locator(".named-tile").first();
      try {
        await network.send("Network.enable");
        await network.send("Network.emulateNetworkConditions", { offline: false, latency: 1500, downloadThroughput: -1, uploadThroughput: -1 });
        await page.getByRole("button", { name: "Refresh all", exact: true }).click();
        await firstTile.getByRole("button", { name: "Cancel refresh for question 1", exact: true }).click();
        await expect(firstTile.getByText("Refresh cancelled. No saved definition was changed.", { exact: true })).toBeVisible();
        await expect(firstTile.locator(".named-result")).toHaveCount(0);
        await capture(page, info, "dashboard-refresh-cancelled");
      } finally {
        try { await network.send("Network.emulateNetworkConditions", { offline: false, latency: 0, downloadThroughput: -1, uploadThroughput: -1 }); }
        finally { await network.detach(); }
      }
      const retriedRequest = page.waitForRequest(request => request.method() === "POST" && request.url().endsWith(`/dashboards/${dashboard.id}/tiles/${dashboard.tiles[0].id}/execute`));
      await firstTile.getByRole("button", { name: "Retry refresh for question 1", exact: true }).click();
      const retriedResponse = await (await retriedRequest).response(); expect(retriedResponse?.ok()).toBe(true);
      const retried = await retriedResponse!.json() as Result; expect(retried.outcome).toBe("succeeded"); expect(retried.rows[0].values.started.value).toBe("4");
      await expect(page.locator(".named-tile .named-result")).toHaveCount(4);
      await capture(page, info, "dashboard-refresh-retried");
      await page.getByRole("button", { name: "Settings for question 1", exact: true }).click();
      const tileSettings = page.getByRole("dialog", { name: "Tile presentation and parameters", exact: true });
      await tileSettings.getByRole("combobox", { name: "Tile visualization", exact: true }).selectOption("line");
      await tileSettings.getByLabel("Tile label", { exact: true }).fill("Native run quality trend");
      await tileSettings.getByLabel("Override the saved default", { exact: true }).check();
      await tileSettings.getByLabel("Override value for workflow", { exact: true }).fill(seeded.definitionId);
      await capture(page, info, "dashboard-tile-settings"); await tileSettings.getByRole("button", { name: "Use tile settings", exact: true }).click();
      const layoutUpdated = page.waitForResponse(response => response.request().method() === "PUT" && response.url().endsWith(`/dashboards/${dashboard.id}`));
      await page.getByRole("button", { name: "Save layout", exact: true }).click(); expect((await layoutUpdated).ok()).toBe(true);
      await expect(page.locator(".named-tile").first().locator(".named-line")).toHaveCount(3);
      const tileDrill = page.waitForResponse(response => response.url().includes(`/dashboards/${dashboard.id}/tiles/`) && response.url().endsWith("/drill"));
      await page.locator(".named-tile").first().getByRole("button", { name: "View matching records for result 1", exact: true }).click();
      expect((await tileDrill).ok()).toBe(true); await expect(drill.getByRole("cell", { name: seeded.executionIds[0], exact: true })).toBeVisible();
      await capture(page, info, "dashboard-drill");
      await captureReadableDrillValues(page, info, capture, "dashboard-drill-values", drill);
      await drill.press("Escape");
      await page.getByRole("button", { name: "Copy saved version", exact: true }).click();
      const dependencyReview = page.getByRole("dialog", { name: "Review dashboard copy dependencies", exact: true });
      const dependencies = dependencyReview.getByRole("region", { name: "Included question access review", exact: true });
      await expect(dependencies).toContainText(saved.name); await expect(dependencies).toContainText("Tiles 1, 2, 3, 4");
      await expect(dependencies).toContainText("This question is personal; it has no audience grants.");
      await capture(page, info, "dashboard-copy-dependency-review");
      await captureScrollPosition(page, info, capture, "dashboard-copy-dependency-review-end", dependencyReview.getByRole("button", { name: "Continue with retained references", exact: true }), "end");
      await dependencies.getByRole("button", { name: `Copy ${saved.name} as an independent question`, exact: true }).click();
      await dependencies.getByLabel(`Name for private copy of ${saved.name}`, { exact: true }).fill(`Independent dependency ${suffix}`);
      await captureScrollPosition(page, info, capture, "dashboard-copy-question-form", dependencies.getByRole("button", { name: "Create independent private question", exact: true }), "end");
      const independentCopyResponse = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/questions/${saved.id}/copies`));
      await dependencies.getByRole("button", { name: "Create independent private question", exact: true }).click();
      const independentCopy = await (await independentCopyResponse).json() as Resource; resources.push({ kind: "questions", id: independentCopy.id });
      await expect(dependencies.getByRole("status")).toContainText("This dashboard still references the original question.");
      expect((await backend.api<Resource>(`/_elsa/studio/dashboards/${dashboard.id}`)).tiles.map(tile => tile.questionId)).toEqual([saved.id, saved.id, saved.id, saved.id]);
      await capture(page, info, "dashboard-copy-independent-question");
      await dependencyReview.getByRole("button", { name: "Continue with retained references", exact: true }).click();
      await copy.getByRole("textbox").fill(`Private dashboard copy ${suffix}`);
      const dashboardCopyResponse = page.waitForResponse(response => response.url().endsWith(`/dashboards/${dashboard.id}/copies`));
      await copy.getByRole("button", { name: "Confirm", exact: true }).click();
      const dashboardCopy = await (await dashboardCopyResponse).json() as Resource; resources.push({ kind: "dashboards", id: dashboardCopy.id });
      expect(dashboardCopy.tiles.map(tile => tile.questionId)).toEqual([saved.id, saved.id, saved.id, saved.id]);
      expect((await backend.api<{ items: unknown[] }>(`/_elsa/studio/dashboards/${dashboardCopy.id}/audience`)).items).toEqual([]);
      await expectResourceLocation("dashboards", dashboardCopy.id); await capture(page, info, "dashboard-copy");
      await share("questions", questionId); await share("dashboards", dashboardId);
      await signInDashboardReader(reader, info);
      await reader.getByRole("searchbox", { name: "Search questions", exact: true }).fill(suffix);
      await reader.getByRole("button", { name: "Search", exact: true }).click();
      await expect(reader.getByRole("heading", { name: "No matching definitions", exact: true })).toBeVisible();
      const sharedSearch = reader.waitForResponse(response => {
        const url = new URL(response.url());
        return url.pathname.endsWith("/_elsa/studio/questions") && url.searchParams.get("ownership") === "shared";
      });
      await reader.getByRole("button", { name: "Shared with me", exact: true }).click();
      expect(new URL((await sharedSearch).url()).searchParams.get("search")).toBe(suffix);
      await expect(reader.getByRole("searchbox", { name: "Search questions", exact: true })).toHaveValue(suffix);
      await expect(reader.getByRole("link", { name: saved.name, exact: true })).toBeVisible();
      await capture(reader, info, "question-shared-search");
      await reader.getByRole("link", { name: saved.name, exact: true }).click();
      await expect(reader.getByRole("button", { name: "Save question", exact: true })).toHaveCount(0);
      await expect(reader.getByRole("button", { name: "Share", exact: true })).toHaveCount(0);
      await reader.getByRole("button", { name: "Refresh results", exact: true }).click();
      await expect(reader.getByRole("region", { name: "Exact result values", exact: true }).getByRole("cell", { name: "4", exact: true })).toBeVisible();
      await capture(reader, info, "shared-question");
      await reader.goto(`/dashboards/${dashboardId}`); await expect(reader.locator(".named-tile .named-result")).toHaveCount(4);
      await capture(reader, info, "shared-dashboard");
      // IAM permission changes are qualified only after a new authenticated reader session.
      await profile("dashboard-native-no-source"); await readerContext.clearCookies(); await signInDashboardReader(reader, info);
      await reader.goto(`/questions/${questionId}`);
      await expect(reader.getByText(/^Parts of this definition are withheld under your current access\./)).toBeVisible();
      await expect(reader.locator(".named-query-controls")).toHaveCount(0); await expect(reader.locator(".named-result")).toHaveCount(0);
      await expect(reader.getByRole("button", { name: "Save question", exact: true })).toHaveCount(0);
      await expect(reader.getByRole("button", { name: "Replace unavailable query", exact: true })).toHaveCount(0);
      await capture(reader, info, "question-source-unavailable");
      // Elevate only the owned resource grant to editor. The independently authenticated reader
      // still lacks source access, and the earlier read-only assertions remain unchanged.
      const beforeRepair = await backend.api<Resource>(`/_elsa/studio/questions/${questionId}`);
      const originalAudience = await backend.api<{ items: { kind: string; id: string; access: string }[] }>(`/_elsa/studio/questions/${questionId}/audience`);
      expect(originalAudience.items).toContainEqual({ kind: "user", id: "dashboard-qa-reader", access: "viewer" });
      const editorAudience = originalAudience.items.map(grant => grant.kind === "user" && grant.id === "dashboard-qa-reader" ? { ...grant, access: "editor" } : grant);
      await backend.api(`/_elsa/studio/questions/${questionId}/audience`, "PUT", { items: editorAudience }, tag(beforeRepair.revision));
      await reader.reload();
      await expect(reader.getByRole("button", { name: "Replace unavailable query", exact: true })).toBeVisible();
      await expect(reader.locator(".named-query-controls")).toHaveCount(0);
      const repairWrites: string[] = [];
      const collectRepairWrite = (request: Request) => { if (["POST", "PUT", "DELETE"].includes(request.method()) && new URL(request.url()).pathname.endsWith(`/_elsa/studio/questions/${questionId}`)) repairWrites.push(request.method()); };
      reader.on("request", collectRepairWrite);
      try {
        await reader.getByRole("button", { name: "Replace unavailable query", exact: true }).click();
        const repair = reader.getByRole("dialog", { name: "Replace unavailable query", exact: true });
        await expect(repair).toContainText("blank replacement query");
        await repair.getByRole("button", { name: "Confirm", exact: true }).click();
        await expect(reader.getByRole("combobox", { name: "Dataset", exact: true })).toHaveValue("");
        await expect(reader.getByRole("group", { name: "Metrics", exact: true }).getByRole("combobox")).toHaveCount(0);
        await expect(reader.getByRole("group", { name: "Parameters and accepted defaults", exact: true }).getByRole("textbox")).toHaveCount(0);
        await expect(reader.locator(".named-result")).toHaveCount(0);
        await expect(reader.getByRole("status").filter({ hasText: "Started a blank replacement query. Nothing has been saved." })).toBeVisible();
        await capture(reader, info, "question-repair-blank");
        expect(repairWrites).toEqual([]);
        // Explicitly discard the local replacement before reopening the saved definition.
        const discardRepair = reader.waitForEvent("dialog"); const back = reader.getByRole("button", { name: "Back to questions", exact: true }).click();
        const discard = await discardRepair; expect(discard.type()).toBe("confirm"); expect(discard.message()).toContain("Discard unsaved changes"); await discard.accept(); await back;
        await expect(reader.getByRole("heading", { name: "Questions", level: 2, exact: true })).toBeVisible();
      } finally { reader.off("request", collectRepairWrite); }
      const afterRepair = await backend.api<Resource>(`/_elsa/studio/questions/${questionId}`);
      expect(afterRepair.definition).toEqual(beforeRepair.definition);
      await backend.api(`/_elsa/studio/questions/${questionId}/audience`, "PUT", originalAudience, tag(afterRepair.revision));
      await profile("dashboard-native-reader"); await readerContext.clearCookies(); await signInDashboardReader(reader, info);
      // With the reader's question still open, revoke only that resource's ACL through the native owner dialog.
      await reader.goto(`/questions/${questionId}`); await reader.getByRole("button", { name: "Refresh results", exact: true }).click();
      await expect(reader.getByRole("button", { name: "Save question", exact: true })).toHaveCount(0);
      await expect(reader.getByRole("combobox", { name: "Dataset", exact: true })).toHaveValue("elsa.workflows.runs");
      await expect(reader.locator(".named-result")).toBeVisible();
      await capture(reader, info, "question-repair-reopened");
      await expect(reader.locator(".named-result")).toBeVisible(); await share("questions", questionId, true);
      await reader.getByRole("button", { name: "Refresh results", exact: true }).click();
      await expect(reader.getByRole("heading", { name: "Current access could not authorize this definition", exact: true })).toBeVisible();
      await expect(reader.locator(".named-result")).toHaveCount(0); await expect(reader.locator(".named-query-controls")).toHaveCount(0);
      await capture(reader, info, "question-access-lost");
      await reader.goto(`/dashboards/${dashboardId}`);
      await expect(reader.getByText("This question is unavailable under your current access. Its reference is preserved during layout saves.", { exact: true })).toHaveCount(4);
      await expect(reader.locator(".named-result")).toHaveCount(0); await capture(reader, info, "dashboard-unavailable-tiles");
      // A dashboard reference added after the question editor opened must still earn confirmation.
      await page.goto(`/questions/${copied.id}`);
      await expect(page.getByLabel("Name", { exact: true })).toHaveValue(copied.name);
      await expect(page.getByRole("region", { name: "Question reuse", exact: true })).toHaveCount(0);
      const lateReference = await backend.api<Resource>("/_elsa/studio/dashboards", "POST", { name: `Late question reference ${suffix}`, description: "Added after opening the question editor", operationKey: `late-reference-${suffix}`, tiles: [{ id: "late-reference", questionId: copied.id, size: "medium" }] });
      resources.push({ kind: "dashboards", id: lateReference.id });
      await page.getByLabel("Maximum result rows/groups", { exact: true }).fill("23");
      await page.getByRole("button", { name: "Save question", exact: true }).click();
      const lateReuse = page.getByRole("dialog", { name: "Update reused question", exact: true });
      await expect(lateReuse).toContainText("Save draft as new question");
      await expect(lateReuse).toContainText(lateReference.name);
      await capture(page, info, "question-new-reuse-confirmation");
      await lateReuse.getByRole("button", { name: "Cancel", exact: true }).click();
      await expect(page.getByRole("region", { name: "Question reuse", exact: true })).toContainText(lateReference.name);
      await expect(page.getByLabel("Maximum result rows/groups", { exact: true })).toHaveValue("23");
      expect((await backend.api<Resource>(`/_elsa/studio/questions/${copied.id}`)).revision).toBe(copied.revision);
      const discardLateDraft = page.waitForEvent("dialog"); const closeLateDraft = page.getByRole("button", { name: "Back to questions", exact: true }).click();
      const discardLate = await discardLateDraft; expect(discardLate.type()).toBe("confirm"); await discardLate.accept(); await closeLateDraft;
      await expect(page.getByRole("link", { name: saved.name, exact: true })).toBeVisible();
      await captureDashboardAppearances(page, info, capture, "questions-collection", async name => {
        await expect(page.getByRole("button", { name: "Create question", exact: true })).toBeVisible();
        await expect(page.getByRole("link", { name: saved.name, exact: true })).toBeVisible();
        await capture(page, info, name);
      });
      // A current saved revision mismatch is rejected instead of silently overwriting the editor's draft.
      await page.goto(`/questions/${questionId}`); const current = await backend.api<Resource>(`/_elsa/studio/questions/${questionId}`);
      await page.getByRole("textbox", { name: "Description", exact: true }).fill("Keep this local conflicted draft");
      await page.getByLabel("Maximum result rows/groups", { exact: true }).fill("24");
      await backend.api(`/_elsa/studio/questions/${questionId}`, "PUT", { name: current.name, description: "Concurrent saved edit", definition: current.definition }, tag(current.revision));
      await page.getByRole("button", { name: "Save question", exact: true }).click();
      await expect(page.getByRole("dialog", { name: "Update reused question", exact: true })).toHaveCount(0);
      await expect(page.locator(".named-inline-error")).toContainText("The saved question changed. Your unsaved draft and original revision remain here.");
      await expect(page.getByRole("region", { name: "Question reuse", exact: true })).toContainText(`Private operations ${suffix}`);
      await expect(page.locator(".named-inline-error")).toBeVisible(); await expect(page.getByRole("textbox", { name: "Description", exact: true })).toHaveValue("Keep this local conflicted draft");
      await expect(page.getByLabel("Maximum result rows/groups", { exact: true })).toHaveValue("24");
      await capture(page, info, "question-conflict");
      await page.getByRole("button", { name: "Save draft as new question", exact: true }).click();
      const recovery = page.getByRole("dialog", { name: "Save draft as new question", exact: true });
      await recovery.getByRole("textbox").fill(`Recovered local draft ${suffix}`);
      const recoveredResponse = page.waitForResponse(response => response.request().method() === "POST" && /\/_elsa\/studio\/questions$/.test(response.url()));
      await recovery.getByRole("button", { name: "Confirm", exact: true }).click();
      const recovered = await (await recoveredResponse).json() as Resource; resources.push({ kind: "questions", id: recovered.id });
      expect(recovered.description).toBe("Keep this local conflicted draft");
      expect(recovered.definition).toMatchObject({ query: { limit: 24 } });
      expect((await backend.api<{ items: unknown[] }>(`/_elsa/studio/questions/${recovered.id}/audience`)).items).toEqual([]);
      await expectResourceLocation("questions", recovered.id); await capture(page, info, "question-conflict-recovered");

      // List is a presentation override on an existing tile. Keep all four identities,
      // their order and question references while saving and reopening the native layout.
      await page.goto(`/dashboards/${dashboardId}`); await expect(page.locator(".named-tile .named-result")).toHaveCount(4);
      await page.getByRole("button", { name: "Settings for question 3", exact: true }).click();
      const listOptions = page.getByRole("dialog", { name: "Tile presentation and parameters", exact: true });
      await listOptions.getByRole("combobox", { name: "Tile visualization", exact: true }).selectOption("list");
      await listOptions.getByLabel("Tile label", { exact: true }).fill("Run quality as a list");
      await listOptions.getByRole("button", { name: "Use tile settings", exact: true }).click();
      const listSavedResponse = page.waitForResponse(response => response.request().method() === "PUT" && response.url().endsWith(`/dashboards/${dashboardId}`));
      await page.getByRole("button", { name: "Save layout", exact: true }).click();
      const listSavedReply = await listSavedResponse; expect(listSavedReply.ok()).toBe(true); const listLayout = await listSavedReply.json() as Resource;
      expect(listLayout.tiles.map(tile => tile.id)).toEqual(dashboard.tiles.map(tile => tile.id));
      expect(listLayout.tiles.map(tile => tile.questionId)).toEqual([saved.id, saved.id, saved.id, saved.id]);
      await page.reload(); await expect(page.locator(".named-tile .named-result")).toHaveCount(4);
      const listRefreshResponse = page.waitForResponse(response => response.url().endsWith(`/dashboards/${dashboardId}/tiles/${listLayout.tiles[2].id}/execute`));
      await page.getByRole("button", { name: "Refresh all", exact: true }).click();
      const listRefreshReply = await listRefreshResponse; expect(listRefreshReply.ok()).toBe(true); const listResult = await listRefreshReply.json() as Result;
      expect(listResult.rows[0].values.started.value).toBe("4");
      const listTile = page.locator(".named-tile").nth(2);
      await expect(listTile.locator(".named-result-list")).toContainText("Started runs: 4");
      await captureScrollPosition(page, info, capture, "dashboard-list-tile", listTile.locator(".named-result-list"));
      await captureScrollPosition(page, info, capture, "dashboard-list-values", listTile.getByRole("region", { name: "Exact result values", exact: true }), "end");

      // The source requires no grouping for a single-value metric. Refine the independent
      // recovered question through ordinary controls, rather than forcing an invalid tile.
      await page.goto(`/questions/${recovered.id}`);
      const longQuestionName = `Metric${"Q".repeat(140)}${suffix.replaceAll("-", "")}`;
      await page.getByRole("textbox", { name: "Name", exact: true }).fill(longQuestionName);
      const metricGrouping = page.getByRole("group", { name: "Group by", exact: true });
      await metricGrouping.getByRole("checkbox", { name: "Current status", exact: true }).uncheck();
      await metricGrouping.getByRole("checkbox", { name: "Run kind", exact: true }).uncheck();
      const metricPresentation = page.getByRole("group", { name: "Presentation", exact: true });
      await metricPresentation.getByRole("combobox", { name: "Visualization", exact: true }).selectOption("metric");
      await metricPresentation.getByRole("combobox", { name: "Category / x axis", exact: true }).selectOption("");
      await metricPresentation.getByRole("listbox", { name: "Value columns", exact: true }).selectOption(["started"]);
      const metricPreviewResponse = page.waitForResponse(response => response.url().endsWith(`/questions/${recovered.id}/preview`));
      await page.getByRole("button", { name: "Preview question", exact: true }).click();
      const metricPreviewReply = await metricPreviewResponse; expect(metricPreviewReply.ok()).toBe(true); const metricPreview = await metricPreviewReply.json() as Result;
      expect(metricPreview.outcome).toBe("succeeded"); expect(metricPreview.rows).toHaveLength(1); expect(metricPreview.rows[0].values.started.value).toBe("4");
      const metricSavedResponse = page.waitForResponse(response => response.request().method() === "PUT" && response.url().endsWith(`/questions/${recovered.id}`));
      await page.getByRole("button", { name: "Save question", exact: true }).click();
      const metricSavedReply = await metricSavedResponse; expect(metricSavedReply.ok()).toBe(true); const metricQuestion = await metricSavedReply.json() as Resource;
      expect(metricQuestion.definition).toMatchObject({ query: { groupBy: [] }, visualization: { kind: "metric", y: ["started"] } });
      await page.goto("/dashboards"); await page.getByRole("button", { name: "Create dashboard", exact: true }).first().click();
      const longDashboardName = `Metric${"A".repeat(140)}${suffix.replaceAll("-", "")}`;
      await page.getByLabel("Name", { exact: true }).fill(longDashboardName);
      await page.getByRole("button", { name: "Add saved question", exact: true }).click();
      const metricPicker = page.getByRole("dialog", { name: "Add saved question", exact: true });
      await metricPicker.getByLabel("Search accessible questions", { exact: true }).fill(metricQuestion.name);
      await metricPicker.getByRole("button", { name: "Search questions", exact: true }).click();
      await expect(metricPicker.getByRole("button", { name: `Add ${metricQuestion.name}`, exact: true })).toBeVisible();
      await capture(page, info, "dashboard-long-question-picker");
      await expect.poll(() => metricPicker.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
      await metricPicker.getByRole("button", { name: `Add ${metricQuestion.name}`, exact: true }).click();
      const metricDashboardResponse = page.waitForResponse(response => response.request().method() === "POST" && /\/_elsa\/studio\/dashboards$/.test(response.url()));
      await page.getByRole("button", { name: "Save layout", exact: true }).click();
      const metricDashboardReply = await metricDashboardResponse; expect(metricDashboardReply.ok()).toBe(true); const metricDashboard = await metricDashboardReply.json() as Resource;
      resources.push({ kind: "dashboards", id: metricDashboard.id }); expect(metricDashboard.tiles).toHaveLength(1); expect(metricDashboard.tiles[0].questionId).toBe(metricQuestion.id);
      await page.reload(); await expect(page.locator(".named-tile .named-metric strong")).toHaveText("4");
      const metricTileRefresh = page.waitForResponse(response => response.url().endsWith(`/dashboards/${metricDashboard.id}/tiles/${metricDashboard.tiles[0].id}/execute`));
      await page.getByRole("button", { name: "Refresh all", exact: true }).click();
      const metricTileReply = await metricTileRefresh; expect(metricTileReply.ok()).toBe(true); const metricResult = await metricTileReply.json() as Result;
      expect(metricResult.rows).toHaveLength(1); expect(metricResult.rows[0].values.started.value).toBe("4");
      await expect(page.locator(".named-tile .named-metric strong")).toHaveText("4");
      await captureScrollPosition(page, info, capture, "dashboard-metric-tile", page.locator(".named-tile .named-metric"));
      const metricValues = page.getByRole("region", { name: "Exact result values", exact: true });
      await captureScrollPosition(page, info, capture, "dashboard-metric-values", metricValues, "end");
      await metricValues.evaluate(element => { element.scrollLeft = element.scrollWidth; });
      await capture(page, info, "dashboard-metric-values-end");
      await captureScrollPosition(page, info, capture, "dashboard-long-name", page.getByRole("heading", { name: longDashboardName, level: 2, exact: true }));
      await page.getByRole("button", { name: "Share", exact: true }).click();
      const longNameShare = page.getByRole("dialog", { name: `Share ${longDashboardName}`, exact: true });
      await expect(longNameShare.getByRole("button", { name: "Save audience", exact: true })).toBeEnabled();
      await capture(page, info, "dashboard-long-name-share");
      await expect.poll(() => longNameShare.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
      const closeLongNameShare = longNameShare.getByRole("button", { name: `Close Share ${longDashboardName}`, exact: true });
      await expect(closeLongNameShare).toBeInViewport();
      await closeLongNameShare.press("Escape"); await expect(longNameShare).toBeHidden();
      const finalOriginal = await backend.api<Resource>(`/_elsa/studio/dashboards/${dashboardId}`);
      expect(finalOriginal.tiles.map(tile => tile.id)).toEqual(dashboard.tiles.map(tile => tile.id));
      expect(finalOriginal.tiles.map(tile => tile.questionId)).toEqual([saved.id, saved.id, saved.id, saved.id]);
      await attachDashboardJson(info, "dashboard-native-presentations.json", { source: process.env.UX_SOURCE_SHA ?? "unknown", expectedStartedRuns: 4, list: { dashboardId, tileId: listLayout.tiles[2].id, rows: listResult.rows }, metric: { questionId: metricQuestion.id, questionRevision: metricQuestion.revision, dashboardId: metricDashboard.id, tileId: metricDashboard.tiles[0].id, previewRows: metricPreview.rows, rows: metricResult.rows }, originalTileIds: finalOriginal.tiles.map(tile => tile.id), originalQuestionReferences: finalOriginal.tiles.map(tile => tile.questionId), mechanism: "Ordinary native controls, explicit save, reload and real native refresh; no model or mocked results" });
      await boundary.verify(info); await readerBoundary.verify(info, "reader"); expect(readerErrors).toEqual([]);
      await attachDashboardJson(info, "dashboard-native-oracle.json", { definitionId: seeded.definitionId, executionIds: seeded.executionIds, observedInstances: seeded.observedInstances, expectedInitialStarted: 3, expectedRefreshedStarted: 4, expectedFailed: 0, expectedFailureRatePercent: 0, actualQueryRows: result.rows, actualRefreshedRows: refreshed.rows, actualRetriedTileRows: retried.rows, coverage: result.coverage, questionId, dashboardId, tileCount: 4, cancellationQualification: { mechanism: "Owned Chromium CDP network latency, 1500ms", responseMocks: false, actualNativeApis: true, assertionScope: "UI cancellation and late-network-result withholding; server compute cancellation is not qualified", networkResetAndDetached: true }, repairQualification: { withheldEditorReplacement: "Confirmed blank local draft, discarded without Save", readerNativeWrites: repairWrites.length, savedDefinitionPreserved: true, originalViewerGrantRestored: true } });
    } finally {
      boundary.dispose(); readerBoundary.dispose();
      // Playwright merges owner and reader contexts into the required trace attachment.
      await readerContext.close();
    }
  } finally { await readerBrowser.close(); }
}

async function captureReadableDrillValues(page: Page, info: TestInfo, capture: Capture, name: string, dialog: Locator) {
  for (const value of [/^completed$/i, /^publishedRun$/i]) {
    const word = dialog.getByRole("cell", { name: value }).first();
    await expect(word).toBeVisible();
    await expect.poll(() => word.evaluate(element => {
      const range = document.createRange(); range.selectNodeContents(element);
      return range.getClientRects().length;
    })).toBe(1);
    await word.scrollIntoViewIfNeeded();
  }
  await capture(page, info, name);
}

/** Studio scrolls inside its workspace/dialogs. Full-page browser screenshots alone miss their
 * lower content, so retain separate views at real element-aligned scroll positions. */
async function captureScrollPosition(page: Page, info: TestInfo, capture: Capture, name: string, target: Locator, block: "start" | "end" = "start") {
  await target.evaluate((element, block) => element.scrollIntoView({ block, inline: "nearest", behavior: "instant" }), block);
  await expect(target).toBeInViewport();
  await capture(page, info, name);
}

async function captureNativeResultSections(page: Page, info: TestInfo, capture: Capture, name: string, result: Locator) {
  const provenance = result.locator(".named-result-provenance > p");
  await captureScrollPosition(page, info, capture, `${name}-provenance`, provenance.first());
  await captureScrollPosition(page, info, capture, `${name}-coverage-periods`, provenance.last(), "end");
  const series = result.locator(".named-chart-series"); await expect(series).toHaveCount(3);
  for (let index = 0; index < 3; index++) await captureScrollPosition(page, info, capture, `${name}-series-${index + 1}`, series.nth(index));
  const values = result.getByRole("region", { name: "Exact result values", exact: true });
  await values.evaluate(element => { element.scrollLeft = 0; });
  await captureScrollPosition(page, info, capture, `${name}-exact-values`, values, "end");
  await values.evaluate(element => { element.scrollLeft = element.scrollWidth; });
  await capture(page, info, `${name}-exact-values-end`);
  await values.evaluate(element => { element.scrollLeft = 0; });
}

type DashboardAppearance = { style: string; mode: string; navigation: string; layout: string };
const dashboardAppearanceStyles: Record<string, string> = { linen: "Linen", minimal: "Minimal", quiet: "Quiet Studio", focus: "Focus Canvas", open: "Open Studio", theme: "Theme default" };
const dashboardAppearanceModes: Record<string, string> = { light: "Light", dark: "Dark", dim: "Dim", "high-contrast": "High contrast" };
const dashboardAppearanceNavigation: Record<string, string> = { theme: "Theme default", left: "Left", top: "Top" };

/** Supplement the full Linen light/dark journeys with every public workspace style, four modes
 * and both navigation arrangements at desktop and 320px. This changes live supported UI state;
 * it never substitutes CSS, html attributes, responses or another host for the composed app. */
export async function captureDashboardAppearances(page: Page, info: TestInfo, capture: Capture, surface: string, observe?: (name: string) => Promise<void>) {
  const original = await page.evaluate(() => ({
    style: localStorage.getItem("elsa-studio-workspace-style"), mode: localStorage.getItem("elsa-studio-theme-mode"), navigation: localStorage.getItem("elsa-studio-nav-mode"),
    attributes: { style: document.documentElement.getAttribute("data-workspace-style"), mode: document.documentElement.getAttribute("data-theme-appearance"), navigation: document.documentElement.getAttribute("data-nav-mode"), layout: document.documentElement.getAttribute("data-theme-layout") }
  }));
  const profiles: DashboardAppearance[] = info.project.use.colorScheme === "dark"
    ? [{ style: "quiet", mode: "high-contrast", navigation: "left", layout: "classic" }, { style: "open", mode: "light", navigation: "top", layout: "editorial" }]
    : [{ style: "minimal", mode: "dim", navigation: "left", layout: "classic" }, { style: "focus", mode: "dark", navigation: "top", layout: "floating" }];
  const observed: (DashboardAppearance & { capturePrefix: string })[] = [];
  let completed = false; let restored = false;
  async function choose(group: string, label: string) {
    const trigger = page.getByRole("button", { name: "Select theme", exact: true });
    await trigger.focus(); await trigger.press("Enter");
    const menu = page.locator(".theme-dropdown"); await expect(menu).toBeVisible();
    const choice = menu.getByRole("group", { name: group, exact: true }).getByRole("menuitemradio", { name: label, exact: true });
    await expect(choice).toBeEnabled(); await choice.press("Enter");
    await page.keyboard.press("Escape"); await expect(menu).toBeHidden();
    await expect(page.getByRole("button", { name: "Select theme", exact: true })).toBeFocused();
  }
  async function apply(style: string, mode: string, navigation: string) {
    if (!dashboardAppearanceStyles[style] || !dashboardAppearanceNavigation[navigation] || mode !== "system" && !dashboardAppearanceModes[mode]) throw new Error("Unsupported Dashboard appearance preference");
    await choose("Workspace style", dashboardAppearanceStyles[style]);
    if (mode === "system") {
      await page.getByRole("button", { name: "Select theme", exact: true }).click();
      await page.locator(".theme-dropdown").getByRole("menuitem", { name: "Follow system appearance", exact: true }).click();
      await page.keyboard.press("Escape");
    } else await choose("Appearance", dashboardAppearanceModes[mode]);
    await choose("Navigation", dashboardAppearanceNavigation[navigation]);
  }
  try {
    for (const [index, profile] of profiles.entries()) {
      await apply(profile.style, profile.mode, profile.navigation);
      await expect(page.locator("html")).toHaveAttribute("data-workspace-style", profile.style);
      await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", profile.mode);
      await expect(page.locator("html")).toHaveAttribute("data-theme-layout", profile.layout);
      await expect(page.locator("html")).toHaveAttribute("data-nav-mode", profile.navigation);
      const name = `${surface}-appearance-${index + 1}`;
      if (observe) await observe(name); else await capture(page, info, name);
      // Record a profile only after its product assertions and every required screenshot complete.
      observed.push({ ...profile, capturePrefix: name });
    }
    completed = true;
  } finally {
    try {
      await apply(original.style ?? "theme", original.mode ?? original.attributes.mode ?? "system", original.navigation ?? "theme");
      for (const [attribute, expected] of [["data-workspace-style", original.attributes.style], ["data-theme-appearance", original.attributes.mode], ["data-nav-mode", original.attributes.navigation], ["data-theme-layout", original.attributes.layout]] as const) {
        if (expected === null) await expect(page.locator("html")).not.toHaveAttribute(attribute); else await expect(page.locator("html")).toHaveAttribute(attribute, expected);
      }
      // Preserve the exact original storage representation after restoring its live UI state.
      await page.evaluate(values => {
        for (const [key, value] of [["elsa-studio-workspace-style", values.style], ["elsa-studio-theme-mode", values.mode], ["elsa-studio-nav-mode", values.navigation]]) {
          if (value === null) localStorage.removeItem(key!); else localStorage.setItem(key!, value!);
        }
      }, original);
      restored = true;
    } finally {
      await attachDashboardJson(info, `${surface}-appearance-matrix.json`, { surface, source: process.env.UX_SOURCE_SHA ?? "unknown", viewport: page.viewportSize(), baseline: original.attributes, requested: profiles, observed, completed, originalPreferencesRestored: restored, mechanism: "Supported Select theme menu, keyboard-operated workspace/mode/navigation choices", exclusions: [], independentVisualAcceptance: "Not implied by assertions; inspect attached screenshots" });
    }
  }
}
