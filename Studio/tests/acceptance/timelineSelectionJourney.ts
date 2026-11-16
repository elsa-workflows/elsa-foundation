import { waitCapturedPlaybackReady } from "./waitCapturedPlaybackReady";
import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";
import type { RuntimeHistoryPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/runtimeHistoryTypes";
import type { WorkflowInstanceDetails } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;

/** Browse real committed occurrences while retaining Timeline; Activity is an explicit destination. */
export async function timelineSelectionJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const backend = process.env.ELSA_WORKBENCH_URL;
  expect(backend, "The owned runner supplies its disposable Workbench URL").toBeTruthy();
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${backend}${path}`, { method, data, timeout: 10_000 });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  async function version(type: string) {
    const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(type)}`);
    const id = catalog.items.find(item => item.definition.activityTypeKey === type)?.definition.headVersionId;
    expect(id, `Registered ${type}`).toBeTruthy();
    return id!;
  }
  const [flowVersion, lineVersion] = await Promise.all([
    version("Elsa.Activities.Flowchart.Activities.Flowchart"), version("Elsa.Activities.Primitives.Activities.WriteLine")
  ]);
  const line = (nodeId: string) => ({ nodeId, activityVersionId: lineVersion, outputs: [],
    inputs: [{ referenceKey: "text", value: { expressionType: "Literal", value: `Timeline ${nodeId}` } }] });
  const submitted = await api<{ definition: { id: string }; version: { id: string } }>("/design/workflows/definitions/submit", "POST", {
    name: `Timeline selection ${info.project.name} ${crypto.randomUUID().slice(0, 8)}`,
    state: {
      variables: [], inputs: [], outputs: [], strategyOptions: { persistenceMode: "Durable" },
      rootActivity: { nodeId: "root", activityVersionId: flowVersion, inputs: [], outputs: [],
        structure: { kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: {
          activities: [line("first"), line("second")], startNodeId: "first",
          connections: [{ id: "first-second", source: { nodeId: "first", port: "Done" }, target: { nodeId: "second" } }],
          nodeMetadata: {}, connectionMetadata: {}
        } }
      }
    }
  });
  registerCleanup(async () => { await api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE"); });
  const published = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
  const run = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
  const path = `/runtime/workflows/instances/${run.workflowExecutionId}`;
  await expect.poll(async () => (await api<WorkflowInstanceDetails>(path)).instance.status, { timeout: 30_000 }).toBe("Completed");
  const details = await api<WorkflowInstanceDetails>(path);
  expect(details.persistenceMode).toBe("Durable");
  const first = details.activities.find(activity => activity.authoredActivityId === "first")!;
  const second = details.activities.find(activity => activity.authoredActivityId === "second")!;
  expect(first?.activityExecutionId).toBeTruthy();
  expect(second?.activityExecutionId).toBeTruthy();
  expect(first.activityExecutionId).not.toBe(second.activityExecutionId);
  await info.attach("execution-identities.json", { body: JSON.stringify({ definitionId: submitted.definition.id,
    versionId: submitted.version.id, ...published, workflowExecutionId: run.workflowExecutionId, first, second }, null, 2), contentType: "application/json" });

  const history = await api<RuntimeHistoryPage>(`${path}/history?limit=100`);
  expect(["captured", "partial"]).toContain(history.coverage.state);
  expect(history.coverage.capturedFromStart).toBe(true);
  expect(history.nextContinuationToken).toBeNull();
  const firstEvent = history.items.find(item => item.activityExecutionId === first.activityExecutionId && item.name === "ActivityCompleted")!;
  const secondEvent = history.items.find(item => item.activityExecutionId === second.activityExecutionId && item.name === "ActivityCompleted")!;
  expect(firstEvent?.historyEventId).toBeTruthy(); expect(secondEvent?.historyEventId).toBeTruthy();
  await info.attach("recorded-event-identities.json", { body: JSON.stringify({ coverage: history.coverage, firstEvent, secondEvent }, null, 2), contentType: "application/json" });

  if (info.project.name.startsWith("narrow")) await page.setViewportSize({ width: 320, height: 900 });
  await page.goto(`/workflows/instances/${run.workflowExecutionId}?view=timeline`);
  await waitCapturedPlaybackReady(page);
  const views = page.getByRole("navigation", { name: "Run workspace views", exact: true });
  const timeline = views.getByRole("button", { name: "Timeline", exact: true });
  const region = page.getByRole("region", { name: "Run timeline", exact: true });
  await expect(timeline).toHaveAttribute("aria-pressed", "true");
  await region.getByRole("combobox", { name: "History detail", exact: true }).selectOption("all");
  const entries = region.getByRole("list", { name: "Run history events", exact: true }).getByRole("button");
  const firstRow = entries.nth(history.items.findIndex(item => item.historyEventId === firstEvent.historyEventId));
  const secondRow = entries.nth(history.items.findIndex(item => item.historyEventId === secondEvent.historyEventId));
  async function settledCapture(name: string, target: Locator) {
    await expect(region).toHaveAttribute("aria-busy", "false");
    await expect(page.getByRole("button", { name: "Retry execution scope", exact: true })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Retry event", exact: true })).toHaveCount(0);
    await target.evaluate(element => element.scrollIntoView({ block: "center", inline: "nearest" }));
    await expect(target).toBeInViewport({ ratio: 1 });
    await capture(page, info, name);
  }
  await expect(entries).toHaveCount(history.items.length);
  await expect(secondRow).toHaveAttribute("data-history-event-id", secondEvent.historyEventId);
  await settledCapture("timeline-populated", secondRow);
  await secondRow.click();
  await expect(timeline).toHaveAttribute("aria-pressed", "true");
  await expect(secondRow).toHaveAttribute("aria-pressed", "true");
  await expect(secondRow).toBeFocused();
  await settledCapture("timeline-click", secondRow);
  const eventDetails = page.getByRole("region", { name: "History event details", exact: true });
  await eventDetails.getByText("Technical details", { exact: true }).click();
  await expect(eventDetails).toContainText(secondEvent.historyEventId);
  await expect(eventDetails).toContainText(second.activityExecutionId);
  await firstRow.press("Enter");
  await expect(timeline).toHaveAttribute("aria-pressed", "true");
  await expect(firstRow).toHaveAttribute("aria-pressed", "true");
  await expect(secondRow).toHaveAttribute("aria-pressed", "false");
  await expect(firstRow).toBeFocused();
  await settledCapture("timeline-keyboard", firstRow);
  await eventDetails.getByText("Technical details", { exact: true }).click();
  await expect(eventDetails).toContainText(firstEvent.historyEventId);
  await expect(eventDetails).toContainText(first.activityExecutionId);
  await expect(eventDetails).not.toContainText(second.activityExecutionId);

  // Execution inspection is explicit; selecting an event keeps the chronology and its focus.
  await eventDetails.getByRole("button", { name: "Inspect owning execution →", exact: true }).click();
  await expect(timeline).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByRole("tab", { name: "Activity", exact: true })).toHaveAttribute("aria-selected", "true");
  await page.getByRole("tablist", { name: "Activity execution views", exact: true })
    .getByRole("tab", { name: "Data", exact: true }).click();
  const technical = page.locator(".wf-activity-execution-details");
  await technical.locator("summary").click();
  await expect(technical).toContainText(first.activityExecutionId);
  await expect(technical).not.toContainText(second.activityExecutionId);
  const exactOwner = technical.getByText(first.activityExecutionId, { exact: true });
  await expect(exactOwner).toHaveAttribute("title", first.activityExecutionId);
  // Long technical values use ellipsis; the complete row, including its copy action, must be visible.
  const exactOwnerRow = technical.locator(".wf-activity-meta-item").filter({ has: page.getByText(first.activityExecutionId, { exact: true }) });
  await exactOwnerRow.evaluate(element => element.scrollIntoView({ block: "center", inline: "nearest" }));
  await expect(exactOwnerRow).toBeInViewport({ ratio: 1 });
  await settledCapture("activity-details", exactOwnerRow);
  await firstRow.press("Enter");
  await expect(timeline).toHaveAttribute("aria-pressed", "true");
  await expect(firstRow).toHaveAttribute("aria-pressed", "true");
  await expect(firstRow).toBeFocused();
  await expect(eventDetails).toBeVisible();
  await settledCapture("timeline-return", firstRow);
}
