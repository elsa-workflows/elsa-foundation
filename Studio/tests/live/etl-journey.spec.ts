import { expect, test, type Locator, type Page, type TestInfo } from "@playwright/test";

test("authors and runs an immutable ETL pipeline through a Sequence workflow", async ({ page }, testInfo) => {
  test.skip(
    process.env.ELSA_ETL_LIVE_JOURNEY !== "1" || !process.env.ELSA_STUDIO_URL,
    "Set ELSA_ETL_LIVE_JOURNEY=1 and ELSA_STUDIO_URL for the dedicated file ETL fixture Workbench."
  );

  await signIn(page);
  await page.setViewportSize({ width: 390, height: 844 });

  // Author a real ETL draft from the host-approved fixture aliases.
  await page.goto("/etl/definitions");
  await expect(page.getByRole("heading", { name: "ETL definitions" })).toBeVisible({ timeout: 60_000 });
  const newDefinition = page.getByRole("button", { name: "New definition" });
  await expect(newDefinition).toBeEnabled({ timeout: 60_000 });
  await newDefinition.focus();
  await expect(newDefinition).toBeFocused();
  await newDefinition.press("Enter");

  const definitionId = `etl-live-${Date.now()}`;
  const createDialog = page.getByRole("dialog", { name: "New ETL definition" });
  await expect(createDialog).toBeVisible();
  await createDialog.getByRole("textbox", { name: "New ETL definition" }).fill(definitionId);
  await createDialog.getByRole("button", { name: "Create draft" }).click();
  await expect(page.getByRole("heading", { name: `Draft: ${definitionId}` })).toBeVisible();

  await selectBinding(page, "Source file", "source");
  await selectBinding(page, "Lookup file", "lookup");
  await selectBinding(page, "Accepted output", "output");
  await selectBinding(page, "Rejected records", "errors");

  // Duplicate a schema name to demonstrate a compiler diagnostic without relying on file inspection.
  const sourceName = page.getByRole("textbox", { name: "Source CSV schema field 2 name" });
  await sourceName.fill("id");
  await page.getByRole("button", { name: "Save draft" }).click();
  await expect(page.getByRole("status").filter({ hasText: `Saved ${definitionId}` })).toBeVisible();
  await page.getByRole("button", { name: "Validate saved revision" }).click();
  await expect(page.getByRole("heading", { name: "Validation feedback" })).toBeVisible();
  await expect(page.getByRole("status").filter({ hasText: /Validation found [1-9]\d* issues?/ })).toBeVisible();

  // Correct the draft, save it, and validate that exact saved revision before explicit publication.
  await sourceName.fill("name");
  await page.getByRole("button", { name: "Save draft" }).click();
  await expect(page.getByRole("status").filter({ hasText: `Saved ${definitionId}` })).toBeVisible();
  await page.getByRole("button", { name: "Validate saved revision" }).click();
  await expect(page.getByRole("status").filter({ hasText: /is valid as immutable version/ })).toBeVisible();
  await expect(page.getByRole("button", { name: "Publish immutable version" })).toBeEnabled();
  await page.getByRole("button", { name: "Publish immutable version" }).click();
  const publishEtlDialog = page.getByRole("dialog", { name: "Publish immutable ETL version?" });
  await publishEtlDialog.getByRole("button", { name: "Publish version" }).click();

  const version = page.getByRole("button", { name: /^View version 1\b/ });
  await expect(version).toBeVisible({ timeout: 60_000 });
  await version.click();
  const artifact = page.getByRole("region", { name: "Published ETL artifact" });
  const selection = (await artifact.locator("code").innerText()).trim();
  expect(selection).not.toBe("");
  await expect(artifact).toContainText(selection);
  const sourceTable = artifact.getByRole("region", { name: "Source CSV schema fields table" });
  await sourceTable.scrollIntoViewIfNeeded();
  await sourceTable.focus();
  await expect(sourceTable).toBeFocused();
  await sourceTable.press("ArrowRight");
  await expect.poll(() => sourceTable.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
  await sourceTable.press("ArrowLeft");
  await expect.poll(() => sourceTable.evaluate(element => element.scrollLeft)).toBe(0);
  await captureNarrowEvidence(page, testInfo, "etl-definition-mobile");

  const workflowName = `ETL live journey ${Date.now()}`;
  const executionId = await createAndRunSequence(page, {
    definitionId, selection, workflowName, writeLineText: "ETL successor completed"
  });

  // Inspect the durable ETL result by its exact published selection, not by host paths or an inferred run ID.
  const { runDetails } = await openRunBySelection(page, selection, "Succeeded");
  await expect(runDetails.getByRole("status")).toHaveText("Succeeded", { timeout: 60_000 });
  await expect(metric(runDetails, "Published pipeline")).toHaveText(selection);
  await expect(metric(runDetails, "Source records")).toHaveText("2");
  await expect(metric(runDetails, "Accepted records")).toHaveText("2");
  await expect(metric(runDetails, "Rejected records")).toHaveText("0");
  await expect(metric(runDetails, "Filtered records")).toHaveText("0");

  const partitions = page.getByRole("region", { name: "Partition progress table" });
  const partitionRows = partitions.locator("tbody tr");
  await expect(partitionRows).toHaveCount(2);
  for (let index = 0; index < 2; index++)
    await expect(partitionRows.nth(index)).toContainText("Complete");
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(runDetails).toBeVisible();
  await runDetails.scrollIntoViewIfNeeded();
  await captureNarrowEvidence(page, testInfo, "etl-run-mobile");

  // The native ETL wait must complete successfully before the following Write Line executes.
  // Reload the real instance view while the runtime delivers the ETL completion if the first read races it.
  await page.setViewportSize({ width: 1440, height: 900 });
  await expectNativeSequenceCompleted(page, executionId, testInfo, "etl-native-sequence-completed.png");
  await testInfo.attach("etl-journey-result", {
    body: JSON.stringify({ definitionId, selection, workflowName, executionId,
      acceptedRecords: 2, rejectedRecords: 0, filteredRecords: 0, completedPartitions: 2,
      nativeActivities: ["Sequence", "RunEtl", "WriteLine"], status: "Succeeded" }, null, 2),
    contentType: "application/json"
  });
});

test("HTTP connector authors, paginates, and completes through a Sequence workflow", async ({ page }, testInfo) => {
  test.skip(
    process.env.ELSA_ETL_HTTP_LIVE_JOURNEY !== "1" || !process.env.ELSA_STUDIO_URL,
    "Set ELSA_ETL_HTTP_LIVE_JOURNEY=1 and ELSA_STUDIO_URL for the dedicated HTTP ETL fixture Workbench."
  );
  test.setTimeout(180_000);

  await signIn(page);
  await page.setViewportSize({ width: 1440, height: 900 });

  const definitionId = `etl-http-live-${Date.now()}`;
  const selection = await authorAndPublishConnector(page, definitionId, "http-source");
  const workflowName = `HTTP ETL live journey ${Date.now()}`;
  const executionId = await createAndRunSequence(page, {
    definitionId, selection, workflowName, writeLineText: "HTTP ETL successor completed", catalog: "connector"
  });

  const { runDetails } = await openRunBySelection(page, selection, "Succeeded", true);
  await expect(runDetails.getByRole("status")).toHaveText("Succeeded", { timeout: 60_000 });
  await expect(metric(runDetails, "Published pipeline")).toHaveText(selection);
  await expect(metric(runDetails, "Source records")).toHaveText("Unknown for an HTTP source");
  await expect(metric(runDetails, "Accepted records")).toHaveText("4");
  await expect(metric(runDetails, "Rejected records")).toHaveText("0");

  const connectorOutput = page.getByRole("region", { name: "Connector output progress" });
  await expect(metric(connectorOutput, "Source type")).toHaveText("HTTP JSON");
  await expect(metric(connectorOutput, "Main output type")).toHaveText("HTTP JSON");
  await expect(metric(connectorOutput, "Error output type")).toHaveText("JSON Lines");
  const acceptedMainRouteBodies = Number(await metric(connectorOutput, "Main route bodies accepted").innerText());
  expect(acceptedMainRouteBodies).toBe(4);
  await expect(metric(connectorOutput, "Error route bodies accepted")).toHaveText("0");

  const partitions = page.getByRole("region", { name: "Connector partition progress table" });
  const partitionRows = partitions.locator("tbody tr");
  await expect(partitionRows).toHaveCount(2);
  for (let index = 0; index < 2; index++) {
    await expect(partitionRows.nth(index)).toContainText("HTTP page 1,");
    await expect(partitionRows.nth(index)).toContainText("source exhausted");
  }
  await page.screenshot({ path: testInfo.outputPath("etl-http-run.png"), fullPage: true });
  await partitions.scrollIntoViewIfNeeded();
  await page.screenshot({ path: testInfo.outputPath("etl-http-partitions.png"), fullPage: true });

  await expectNativeSequenceCompleted(page, executionId, testInfo, "etl-http-native-sequence-completed.png");
  await testInfo.attach("etl-http-journey-result", {
    body: JSON.stringify({ definitionId, selection, workflowName, executionId, sourceRecords: "unknown for an HTTP source",
      acceptedRecords: 4, acceptedMainRouteBodies, rejectedRecords: 0,
      sourcePartitions: 2, nativeActivities: ["Sequence", "RunEtl", "WriteLine"], status: "Succeeded" }, null, 2),
    contentType: "application/json"
  });
});

test("HTTP connector shows a waiting 429 and accepts Studio cancellation", async ({ page }, testInfo) => {
  test.skip(
    process.env.ELSA_ETL_HTTP_LIVE_JOURNEY !== "1" || !process.env.ELSA_STUDIO_URL,
    "Set ELSA_ETL_HTTP_LIVE_JOURNEY=1 and ELSA_STUDIO_URL for the dedicated HTTP ETL fixture Workbench."
  );
  test.setTimeout(180_000);

  await signIn(page);
  await page.setViewportSize({ width: 1440, height: 900 });

  const definitionId = `etl-http-wait-${Date.now()}`;
  const selection = await authorAndPublishConnector(page, definitionId, "http-wait");
  const workflowName = `HTTP ETL cancellation ${Date.now()}`;
  const executionId = await createAndRunSequence(page, {
    definitionId, selection, workflowName, writeLineText: "Must not run after HTTP cancellation", catalog: "connector"
  });

  const { runEntry, runDetails } = await openRunBySelection(page, selection, /Preparing input|Processing/, true);
  const partitions = page.getByRole("region", { name: "Connector partition progress table" });
  await expect(async () => {
    await page.getByRole("button", { name: "Refresh", exact: true }).click();
    await expect(runDetails.getByRole("status")).toContainText(/Preparing input|Processing/);
    await expect(partitions).toContainText(/Retry phase: waiting; attempt [1-9]\d*\./);
    await expect(partitions).toContainText("Deadline:");
    await expect(partitions).toContainText("Last HTTP status: 429");
    await expect(partitions).toContainText("Next attempt after");
  }).toPass({ timeout: 60_000, intervals: [500, 1_000, 2_000] });

  await expect(metric(runDetails, "Source records")).toHaveText("Unknown for an HTTP source");
  const connectorOutput = page.getByRole("region", { name: "Connector output progress" });
  await expect(metric(connectorOutput, "Main route bodies accepted")).toHaveText("0");
  await expect(metric(connectorOutput, "Error route bodies accepted")).toHaveText("0");

  await page.getByRole("button", { name: "Cancel run", exact: true }).click();
  const cancelDialog = page.getByRole("dialog").last();
  await expect(cancelDialog).toContainText("Completed output writes are retained");
  await cancelDialog.getByRole("button", { name: "Cancel run", exact: true }).click();

  const cancellationReceipt = page.getByRole("status").filter({ hasText: "Cancellation accepted." });
  await expect(cancellationReceipt).toContainText("Actual terminal outcome: Cancelled.");
  await expect(runDetails.getByRole("status")).toHaveText("Cancelled");
  await expect(runEntry).toContainText("Cancelled");
  await expect(metric(connectorOutput, "Main route bodies accepted")).toHaveText("0");
  await expect(metric(connectorOutput, "Error route bodies accepted")).toHaveText("0");
  await page.screenshot({ path: testInfo.outputPath("etl-http-429-cancelled.png"), fullPage: true });
  await expectNativeSequenceHasNoCompletedWriteLine(page, executionId, testInfo);
  await testInfo.attach("etl-http-cancellation-result", {
    body: JSON.stringify({ definitionId, selection, workflowName, status: "Cancelled",
      retryPhase: "waiting", lastStatusCode: 429, acceptedMainRouteBodies: 0, acceptedErrorRouteBodies: 0 }, null, 2),
    contentType: "application/json"
  });
});

async function authorAndPublishConnector(page: Page, definitionId: string, sourceBinding: string) {
  await page.goto("/etl/connectors");
  await expect(page.getByRole("heading", { name: "Connector pipelines" })).toBeVisible({ timeout: 60_000 });
  await page.getByRole("button", { name: "New definition" }).click();

  const createDialog = page.getByRole("dialog", { name: "New ETL definition" });
  await createDialog.getByRole("textbox", { name: "New ETL definition" }).fill(definitionId);
  await createDialog.getByRole("button", { name: "Create draft" }).click();
  await expect(page.getByRole("heading", { name: `Draft: ${definitionId}` })).toBeVisible();

  await selectBinding(page, "Source HTTP binding", sourceBinding);
  await fill(page, "Records JSON Pointer", "/items");
  await fill(page, "Continuation query parameter", "pageToken");
  await selectBinding(page, "Accepted records binding", "http-output");
  await page.getByLabel("Rejected records destination kind", { exact: true }).selectOption("jsonl");
  await selectBinding(page, "Rejected records binding", "errors");
  await expect(page.getByText("No lookup is configured", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Save draft" }).click();
  await expect(page.getByRole("status").filter({ hasText: `Saved ${definitionId}` })).toBeVisible();
  await page.getByRole("button", { name: "Validate saved revision" }).click();
  await expect(page.getByRole("status").filter({ hasText: /is valid as immutable version/ })).toBeVisible({ timeout: 60_000 });
  await page.getByRole("button", { name: "Publish immutable version" }).click();
  const publishDialog = page.getByRole("dialog", { name: "Publish immutable ETL version?" });
  await publishDialog.getByRole("button", { name: "Publish version" }).click();

  const version = page.getByRole("button", { name: /^View version 1\b/ });
  await expect(version).toBeVisible({ timeout: 60_000 });
  await version.click();
  const artifact = page.getByRole("region", { name: "Published ETL artifact" });
  await expect(artifact).toBeVisible();
  const selection = (await artifact.locator("code").innerText()).trim();
  expect(selection).not.toBe("");
  await expect(artifact).toContainText(selection);
  return selection;
}

async function createAndRunSequence(page: Page, {
  definitionId, selection, workflowName, writeLineText, catalog = "file"
}: { definitionId: string; selection: string; workflowName: string; writeLineText?: string; catalog?: "file" | "connector" }) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/workflows/definitions");
  const create = page.getByRole("button", { name: "Create", exact: true });
  await expect(create).toBeVisible({ timeout: 60_000 });
  await create.click();
  const workflowDialog = page.getByRole("dialog", { name: "Create Workflow" });
  await workflowDialog.getByLabel("Display name").fill(workflowName);
  const sequenceRoot = workflowDialog.getByRole("radio", { name: "Sequence" });
  await expect(sequenceRoot).toBeEnabled();
  await sequenceRoot.focus();
  await sequenceRoot.press("Space");
  await expect(sequenceRoot).toBeChecked();
  await workflowDialog.getByRole("button", { name: "Create", exact: true }).click();
  await expect(page.getByRole("button", { name: "Definitions" })).toBeVisible();
  await expect(page.locator(".wf-sequence-designer")).toBeVisible();

  await addActivityByKeyboard(page, "Add the first step", "Run Etl");
  const etlCatalog = page.getByLabel("Pipeline catalog", { exact: true });
  await expect(etlCatalog).toBeVisible();
  await etlCatalog.selectOption(catalog);
  await expect(etlCatalog).toHaveValue(catalog);
  const etlDefinition = page.getByLabel("Published ETL definition", { exact: true });
  await expect(etlDefinition).toBeVisible();
  await selectPagedOption(page, etlDefinition, definitionId, "Load more definitions");
  const etlVersion = page.getByLabel("Published ETL version", { exact: true });
  await expect.poll(async () => etlVersion.locator("option").evaluateAll((options, expected) =>
    options.some(option => (option as HTMLOptionElement).value === expected), selection
  ), { timeout: 60_000 }).toBe(true);
  await etlVersion.selectOption(selection);
  await expect(etlVersion).toHaveValue(selection);

  if (writeLineText !== undefined) {
    await addActivityByKeyboard(page, /^Insert step after /, "Write Line");
    await page.getByRole("button", { name: /^2\. Write Line/ }).click();
    await fill(page, "Text", writeLineText);
    await expect(page.getByRole("list", { name: "Sequence steps" }).getByRole("listitem")).toHaveCount(2);
  }

  await page.getByRole("button", { name: "Publish options" }).click();
  await page.getByRole("menuitemradio", { name: "Review & publish" }).click();
  const workflowReview = page.getByRole("dialog", { name: "Publish workflow", exact: true });
  await expect(workflowReview).toBeVisible({ timeout: 60_000 });
  await workflowReview.getByRole("button", { name: "Publish", exact: true }).click();
  const publishedDialog = page.getByRole("dialog", { name: "Published" });
  await expect(publishedDialog).toBeVisible({ timeout: 60_000 });
  await publishedDialog.getByRole("button", { name: "Close" }).click();

  await page.goto("/workflows/executables");
  const executableRow = page.getByRole("table", { name: "Workflow executables" })
    .getByRole("row").filter({ hasText: workflowName });
  await expect(executableRow).toHaveCount(1, { timeout: 60_000 });
  await executableRow.getByRole("button", { name: "Run", exact: true }).click();
  const openRun = page.getByRole("button", { name: /^Open Run / });
  await expect(openRun).toBeVisible({ timeout: 60_000 });
  const executionId = (await openRun.innerText()).replace(/^Open Run /, "").trim();
  expect(executionId).not.toBe("");
  return executionId;
}

async function openRunBySelection(page: Page, selection: string, expectedStatus: string | RegExp, expectConnectorPartitions = false) {
  await page.goto("/etl/runs");
  await expect(page.getByRole("heading", { name: "ETL runs" })).toBeVisible({ timeout: 60_000 });
  const runEntry = page.locator("button.etl-run-select").filter({ hasText: selection });
  const refresh = page.getByRole("button", { name: "Refresh", exact: true });
  await expect(async () => {
    await refresh.click();
    await expect(runEntry).toHaveCount(1, { timeout: 5_000 });
    await expect(runEntry).toContainText(expectedStatus, { timeout: 5_000 });
  }).toPass({ timeout: 90_000, intervals: [1_000, 2_000, 5_000] });

  const connectorPartitionsResponse = expectConnectorPartitions
    ? page.waitForResponse(response => response.url().includes("/etl/runs/") && response.url().includes("/connector-partitions"), { timeout: 60_000 })
    : null;
  await runEntry.click();
  if (connectorPartitionsResponse) {
    const response = await connectorPartitionsResponse;
    expect(response.status()).toBe(200);
  }

  const runDetails = page.getByRole("region", { name: "Run details" });
  await expect(runDetails).toBeVisible();
  return { runEntry, runDetails };
}

async function expectNativeSequenceCompleted(page: Page, executionId: string, testInfo: TestInfo, screenshotName: string) {
  const timeline = page.getByRole("list", { name: "Execution timeline" });
  const runEtlEntry = timeline.getByRole("button").filter({ hasText: /Run\s*Etl/i });
  const successorEntry = timeline.getByRole("button").filter({ hasText: "Write Line" });
  const sequenceEntry = timeline.getByRole("button").filter({ hasText: "Sequence" });
  await expect(async () => {
    await page.goto(`/workflows/instances/${encodeURIComponent(executionId)}`);
    await expect(timeline).toBeVisible({ timeout: 5_000 });
    await expect(sequenceEntry).toHaveCount(1, { timeout: 5_000 });
    await expect(sequenceEntry.locator('[data-status="completed"]')).toBeVisible({ timeout: 5_000 });
    await expect(runEtlEntry).toHaveCount(1, { timeout: 5_000 });
    await expect(runEtlEntry.locator('[data-status="completed"]')).toBeVisible({ timeout: 5_000 });
    await expect(successorEntry).toHaveCount(1, { timeout: 5_000 });
    await expect(successorEntry.locator('[data-status="completed"]')).toBeVisible({ timeout: 5_000 });
    const entries = await timeline.getByRole("button").allTextContents();
    const etlIndex = entries.findIndex(text => /Run\s*Etl/i.test(text));
    const successorIndex = entries.findIndex(text => text.includes("Write Line"));
    expect(etlIndex).toBeGreaterThanOrEqual(0);
    expect(successorIndex).toBeGreaterThan(etlIndex);
  }).toPass({ timeout: 90_000, intervals: [1_000, 2_000, 5_000] });
  await page.screenshot({ path: testInfo.outputPath(screenshotName) });
}

async function expectNativeSequenceHasNoCompletedWriteLine(page: Page, executionId: string, testInfo: TestInfo) {
  await page.goto(`/workflows/instances/${encodeURIComponent(executionId)}`);
  const timeline = page.getByRole("list", { name: "Execution timeline" });
  const runEtlEntry = timeline.getByRole("button").filter({ hasText: /Run\s*Etl/i });
  const sequenceEntry = timeline.getByRole("button").filter({ hasText: "Sequence" });
  const successorEntry = timeline.getByRole("button").filter({ hasText: "Write Line" });
  await expect(async () => {
    await page.reload();
    await expect(timeline).toBeVisible({ timeout: 5_000 });
    await expect(runEtlEntry).toHaveCount(1, { timeout: 5_000 });
    await expect(runEtlEntry.locator('[data-status="cancelled"]')).toBeVisible({ timeout: 5_000 });
    await expect(sequenceEntry).toHaveCount(1, { timeout: 5_000 });
    await expect(sequenceEntry.locator('[data-status="cancelled"]')).toBeVisible({ timeout: 5_000 });
  }).toPass({ timeout: 90_000, intervals: [1_000, 2_000, 5_000] });
  await expect(successorEntry.locator('[data-status="completed"]')).toHaveCount(0);
  await page.screenshot({ path: testInfo.outputPath("etl-http-sequence-cancelled.png") });
}

async function signIn(page: Page) {
  await page.goto("/");
  await page.locator("input[name=username], input[type=text]").first().fill(process.env.ELSA_STUDIO_USER ?? "admin");
  await page.locator("input[type=password]").first().fill(process.env.ELSA_STUDIO_PASSWORD ?? "Password123!");
  await page.locator("button[type=submit], button:has-text('Sign in')").first().click();
  await page.waitForLoadState("networkidle");
}

async function selectBinding(page: Page, label: string, alias: string) {
  const select = page.getByLabel(label, { exact: true });
  await expect.poll(async () => select.locator("option").evaluateAll((options, expected) =>
    options.some(option => (option as HTMLOptionElement).value === expected), alias
  ), { timeout: 60_000 }).toBe(true);
  await select.selectOption(alias);
}

async function selectPagedOption(page: Page, select: Locator, value: string, loadMoreLabel: string) {
  // The control becomes enabled only after its initial API page has arrived.
  await expect(select).toBeEnabled({ timeout: 60_000 });
  for (let pageNumber = 0; pageNumber < 20; pageNumber++) {
    const hasOption = await select.locator("option").evaluateAll((options, expected) =>
      options.some(option => (option as HTMLOptionElement).value === expected), value
    );
    if (hasOption) {
      await select.selectOption(value);
      return;
    }

    const loadMore = page.getByRole("button", { name: loadMoreLabel, exact: true });
    if (!(await loadMore.isVisible())) break;
    const previousCount = await select.locator("option").count();
    await loadMore.click();
    await expect.poll(() => select.locator("option").count()).toBeGreaterThan(previousCount);
  }

  throw new Error(`The published ETL definition ${value} was not available in the bounded picker pages.`);
}

async function addActivityByKeyboard(page: Page, triggerName: string | RegExp, activityName: string) {
  const trigger = page.getByRole("button", { name: triggerName });
  await expect(trigger).toBeEnabled();
  await trigger.focus();
  await expect(trigger).toBeFocused();
  await trigger.press("Enter");
  const picker = page.getByRole("listbox", { name: "Activity picker" });
  await expect(picker).toBeVisible();
  const search = page.getByRole("searchbox", { name: "Search activities" });
  await search.focus();
  await page.keyboard.type(activityName);
  await expect(search).toHaveValue(activityName);
  await expect(picker.getByRole("option").first()).toContainText(new RegExp(activityName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"), "i"));
  await search.press("Enter");
  await expect(picker).toBeHidden();
}

async function fill(page: Page, label: string, value: string) {
  const box = page.getByRole("textbox", { name: label, exact: true });
  await box.fill(value);
  await box.press("Tab");
}

function metric(section: Locator, label: string) {
  return section.getByText(label, { exact: true }).locator("xpath=following-sibling::dd[1]");
}

async function captureNarrowEvidence(page: Page, testInfo: TestInfo, name: string) {
  const layout = await page.locator(".etl-shell").evaluate(element => {
    const bounds = element.getBoundingClientRect();
    const content = element.closest("main")!;
    const overflowText: { text: string; parentClasses: string; right: number }[] = [];
    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      const parent = node.parentElement!;
      if (!node.textContent?.trim() || parent.closest(".etl-editor-table-wrap, .etl-table-scroll")) continue;
      const range = document.createRange();
      range.selectNodeContents(node);
      const right = Math.max(...Array.from(range.getClientRects(), rect => rect.right));
      if (right > window.innerWidth + 1)
        overflowText.push({ text: node.textContent.slice(0, 160), parentClasses: parent.className, right });
    }
    return {
      viewportWidth: window.innerWidth, left: bounds.left, right: bounds.right,
      contentWidth: content.clientWidth, contentScrollWidth: content.scrollWidth,
      documentWidth: document.documentElement.scrollWidth,
      overflowText,
      overflowElements: Array.from(element.querySelectorAll("*")).filter(child =>
        child.getBoundingClientRect().right > window.innerWidth + 1 &&
        !child.closest(".etl-editor-table-wrap, .etl-table-scroll")
      ).slice(0, 50).map(child => {
        const rect = child.getBoundingClientRect();
        const style = getComputedStyle(child);
        const parent = child.parentElement!;
        return { tag: child.tagName, classes: child.className, left: rect.left, right: rect.right,
          width: rect.width, scrollWidth: child.scrollWidth, overflowX: style.overflowX,
          minWidth: style.minWidth, display: style.display, parentClasses: parent.className,
          parentWidth: parent.clientWidth, parentOverflowX: getComputedStyle(parent).overflowX };
      })
    };
  });
  await testInfo.attach(`${name}-layout`, { body: JSON.stringify(layout, null, 2), contentType: "application/json" });
  await page.screenshot({ path: testInfo.outputPath(`${name}.png`) });
  expect.soft(layout.left).toBeGreaterThanOrEqual(0);
  expect.soft(layout.right).toBeLessThanOrEqual(layout.viewportWidth + 1);
  expect.soft(layout.contentScrollWidth).toBeLessThanOrEqual(layout.contentWidth + 1);
}
