import { expect, type Page, type TestInfo } from "@playwright/test";
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

  // The selected-occurrence link also opens the inspector on narrow screens using normal navigation.
  await page.goto(`/workflows/instances/${run.workflowExecutionId}?tab=activity&selected=${first.activityExecutionId}`);
  const panel = page.getByLabel("Run details panel", { exact: true });
  const timelineTab = panel.getByRole("tab", { name: "Timeline", exact: true });
  async function settledCapture(name: string) {
    await expect(page.getByText("Resolving execution scope…", { exact: true })).toHaveCount(0);
    await expect(page.getByText("Loading visible execution evidence…", { exact: true })).toHaveCount(0);
    await expect(page.getByText("Loading recorded executions…", { exact: true })).toHaveCount(0);
    await capture(page, info, name);
  }
  await timelineTab.click();
  const entries = panel.getByRole("list", { name: "Execution timeline", exact: true }).getByRole("button");
  await expect(entries).toHaveCount(3);
  await settledCapture("timeline-populated");
  await entries.nth(2).click();
  await expect(timelineTab).toHaveAttribute("aria-selected", "true");
  await expect(entries.nth(2)).toHaveAttribute("data-selected", "true");
  await expect(entries.nth(2)).toBeFocused();
  await expect(page.locator('.wf-node[data-runtime-node-id="second"]')).toHaveAttribute("aria-pressed", "true");
  await settledCapture("timeline-click");
  await entries.nth(1).press("Enter");
  await expect(timelineTab).toHaveAttribute("aria-selected", "true");
  await expect(entries.nth(1)).toHaveAttribute("data-selected", "true");
  await expect(entries.nth(2)).toHaveAttribute("data-selected", "false");
  await expect(entries.nth(1)).toBeFocused();
  await expect(page.locator('.wf-node[data-runtime-node-id="first"]')).toHaveAttribute("aria-pressed", "true");
  await expect(page.locator('.wf-node[data-runtime-node-id="second"]')).toHaveAttribute("aria-pressed", "false");
  await settledCapture("timeline-keyboard");

  await panel.getByRole("tab", { name: "Activity", exact: true }).click();
  const technical = panel.locator(".wf-activity-execution-details");
  await technical.locator("summary").click();
  await expect(technical).toContainText(first.activityExecutionId);
  await expect(technical).not.toContainText(second.activityExecutionId);
  await settledCapture("activity-details");
  await timelineTab.click();
  await expect(entries.nth(1)).toHaveAttribute("data-selected", "true");
  await settledCapture("timeline-return");
}
