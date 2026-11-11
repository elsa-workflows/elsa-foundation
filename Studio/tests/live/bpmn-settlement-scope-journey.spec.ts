import { existsSync, unlinkSync, writeFileSync } from "node:fs";
import { expect, test, type APIRequestContext, type Locator, type Page, type Route } from "@playwright/test";
import { studioPassword } from "./support/journey";
import { addPersonalBackend, signInToStudio, waitForSelectedBackend } from "./support/studioReady";

// Spec 1184 FR-012 / FR-014 on a real owned Studio/Workbench pair. Opt-in: the default
// UX host does not enable BPMN hosting. tools/bpmn/run_owned_settlement_scope_studio.py
// starts that composition. No retries or timeout inflation; wait on switcher, headings,
// and command/settlement text. Do not wait for fixture-only data-committed-backend-url.

test.skip(
  process.env.ELSA_BPMN_SETTLEMENT_LIVE !== "1" || !process.env.ELSA_STUDIO_URL || !process.env.ELSA_WORKBENCH_URL,
  "Set ELSA_BPMN_SETTLEMENT_LIVE=1 with ELSA_STUDIO_URL and ELSA_WORKBENCH_URL for the owned BPMN pair."
);

const workbenchUrl = () => {
  const url = process.env.ELSA_WORKBENCH_URL!;
  return url.endsWith("/") ? url : `${url}/`;
};

async function operator(playwright: { request: { newContext: (options?: { baseURL?: string }) => Promise<APIRequestContext> } }, user = "admin") {
  const api = await playwright.request.newContext({ baseURL: workbenchUrl() });
  const login = await api.post("/_elsa/identity/login", { data: { username: user, password: studioPassword } });
  expect(login.ok(), await login.text()).toBe(true);
  return api;
}

async function post(api: APIRequestContext, path: string, data: unknown) {
  const response = await api.post(path, { data });
  expect(response.ok(), `${path} ${response.status()} ${await response.text()}`).toBe(true);
  return response.json();
}

async function get(api: APIRequestContext, path: string) {
  const response = await api.get(path);
  expect(response.ok(), `${path} ${response.status()} ${await response.text()}`).toBe(true);
  return response.json();
}

function humanDefinition(processId: string, taskName: string) {
  return {
    formatVersion: 1,
    humanBindings: ["work"],
    process: {
      processId,
      elements: [
        { elementId: "start", elementType: "startEvent" },
        { elementId: "human", elementType: "userTask", name: taskName, bindingRef: "work" },
        { elementId: "end", elementType: "endEvent", name: "Complete" }
      ],
      sequenceFlows: [
        { flowId: "to-work", sourceRef: "start", targetRef: "human" },
        { flowId: "to-end", sourceRef: "human", targetRef: "end" }
      ]
    }
  };
}

async function publishHuman(api: APIRequestContext, processId: string, taskName: string) {
  return post(api, "/bpmn/artifacts", { requestId: `publish-${processId}`, definition: humanDefinition(processId, taskName) });
}

async function openBpmnProcesses(page: Page) {
  await page.goto("/workflows/bpmn");
  await expect(page.getByRole("heading", { name: "BPMN processes", exact: true, level: 2 })).toBeVisible({ timeout: 30_000 });
}

async function waitForStartableProcess(page: Page, sourceReferenceId: string) {
  let rowIndex = -1;
  await expect.poll(async () => {
    await page.getByRole("button", { name: "Refresh", exact: true }).click();
    const rows = page.locator("tbody tr");
    const count = await rows.count();
    for (let index = 0; index < count; index++) {
      const row = rows.nth(index);
      const trigger = row.getByRole("button", { name: /^Identifiers for / });
      if (await trigger.count() === 0) continue;
      await trigger.click();
      const dialog = page.getByRole("dialog", { name: /identifiers for/i });
      await expect(dialog).toBeVisible();
      const match = await dialog.getByLabel(`Source reference: ${sourceReferenceId}`).count();
      await page.getByRole("button", { name: "Close identifiers", exact: true }).click();
      await expect(dialog).toHaveCount(0);
      if (match) {
        rowIndex = index;
        return row.getByRole("button", { name: "Start process", exact: true }).count();
      }
    }
    return 0;
  }, { timeout: 30_000, intervals: [200, 500] }).toBe(1);
  return page.locator("tbody tr").nth(rowIndex);
}

async function holdStart(page: Page) {
  let release!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; });
  const handler = async (route: Route) => {
    const url = new URL(route.request().url());
    if (route.request().method() === "POST" && url.pathname.replace(/\/+$/, "") === "/bpmn/executions") await held;
    try {
      await route.continue();
    } catch (error) {
      // Sign-out or backend switch aborts the paused POST. That abandonment is the FR-014 case.
      if (!/already handled|Target closed|Request context disposed|Test ended|intercepted request was disposed/i.test(String(error))) throw error;
    }
  };
  await page.route("**/bpmn/executions", handler);
  return {
    release: () => release(),
    dispose: () => page.unroute("**/bpmn/executions", handler)
  };
}

async function waitForUnconfirmedStart(row: Locator) {
  await expect(row.getByText("The start outcome is unconfirmed. Resume uses the retained request.", { exact: true })).toBeVisible();
  await expect(row.getByRole("button", { name: "Start process", exact: true })).toBeDisabled();
}

test.describe("FR-012 awaiting settlement and timeout path", () => {
  test("shows Cancelled · awaiting settlement while a native parent remains owed", async ({ page, playwright }, info) => {
    const holdPath = process.env.ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME;
    test.skip(!holdPath, "The owned runner must point ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME at a hold file path.");
    if (existsSync(holdPath!)) unlinkSync(holdPath!);

    const api = await operator(playwright);
    try {
      const published = await publishHuman(api, `settlement-human-${Date.now()}`, "Review settlement");
      const catalog = await get(api, `/design/activities/definitions?search=${encodeURIComponent("Elsa.Activities.Bpmn.Activities.RunBpmn")}`);
      const activity = catalog.items.find((item: { definition: { activityTypeKey: string } }) =>
        item.definition.activityTypeKey === "Elsa.Activities.Bpmn.Activities.RunBpmn");
      expect(activity).toBeTruthy();
      const submitted = await post(api, "/design/workflows/definitions/submit", {
        name: `RunBpmn settlement parent ${Date.now()}`,
        state: {
          variables: [], inputs: [], outputs: [],
          rootActivity: {
            nodeId: "run-bpmn",
            activityVersionId: activity.definition.headVersionId,
            outputs: [],
            inputs: [{ referenceKey: "Model", value: { value: published.sourceReferenceId, expressionType: "Literal" } }]
          }
        }
      });
      const native = await post(api, `/publishing/workflows/${submitted.version.id}/publish`, {});
      await post(api, `/runtime/workflows/executables/${native.artifactId}/execute`, { sourceReferenceId: native.sourceReferenceId });

      let childId = "";
      await expect.poll(async () => {
        const executions = await get(api, "/bpmn/executions?limit=50");
        childId = executions.items.find((item: { status: string }) => item.status === "Running")?.executionId ?? "";
        return childId;
      }, { timeout: 30_000, intervals: [200, 500, 1_000] }).not.toBe("");

      await signInToStudio(page);
      await page.goto(`/workflows/bpmn/runs/${encodeURIComponent(childId)}`);
      await expect(page.locator(".wf-bpmn-run-heading")).toContainText("Waiting for a person", { timeout: 30_000 });

      writeFileSync(holdPath!, "hold");
      const cancel = await api.post(`/bpmn/executions/${encodeURIComponent(childId)}/cancel`, {
        data: { requestId: `cancel-${childId}`, reason: "Settlement display" }
      });
      expect(cancel.ok(), await cancel.text()).toBe(true);

      await expect(async () => {
        const refresh = page.getByRole("button", { name: "Refresh run", exact: true });
        if (await refresh.isEnabled()) await refresh.click();
        await expect(page.getByRole("status").filter({ hasText: "Cancelled · awaiting settlement" })).toBeVisible({ timeout: 1_000 });
      }).toPass({ timeout: 30_000, intervals: [200, 500, 1_000] });
      await expect(page.getByText("Native parent: Pending", { exact: true })).toBeVisible();
      await info.attach("fr-012-awaiting-settlement.png", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });

      unlinkSync(holdPath!);
      await expect(async () => {
        const refresh = page.getByRole("button", { name: "Refresh run", exact: true });
        if (await refresh.isEnabled()) await refresh.click();
        await expect(page.locator(".wf-bpmn-run-heading")).toContainText("Cancelled", { timeout: 1_000 });
        await expect(page.getByText("awaiting settlement")).toHaveCount(0);
      }).toPass({ timeout: 30_000, intervals: [200, 500, 1_000] });
    } finally {
      if (holdPath && existsSync(holdPath)) unlinkSync(holdPath);
      await api.dispose();
    }
  });

  test("timeout path reports Completed without labelling the timed-out end event", async ({ page, playwright }, info) => {
    const api = await operator(playwright);
    try {
      const timed = {
        formatVersion: 6,
        humanBindings: ["review-work"],
        timerBindings: ["timeout-work"],
        claimBindings: ["review-work"],
        process: {
          processId: `timed-end-label-${Date.now()}`,
          elements: [
            { elementId: "start", elementType: "startEvent" },
            { elementId: "review", elementType: "userTask", name: "Review order", bindingRef: "review-work" },
            {
              elementId: "timeout", elementType: "boundaryEvent", bindingRef: "timeout-work", attachedToRef: "review",
              cancelActivity: true, eventDefinitions: [{ type: "timer", properties: { interval: "PT8S" } }]
            },
            { elementId: "done", elementType: "endEvent", name: "Complete" },
            { elementId: "timed-out", elementType: "endEvent", name: "Timed out" }
          ],
          sequenceFlows: [
            { flowId: "start-review", sourceRef: "start", targetRef: "review" },
            { flowId: "review-done", sourceRef: "review", targetRef: "done" },
            { flowId: "timeout-end", sourceRef: "timeout", targetRef: "timed-out" }
          ]
        }
      };
      const published = await post(api, "/bpmn/artifacts", { requestId: `timed-${Date.now()}`, definition: timed });
      const started = await post(api, "/bpmn/executions", { requestId: `timed-start-${Date.now()}`, sourceReferenceId: published.sourceReferenceId });

      await signInToStudio(page);
      await page.goto(`/workflows/bpmn/runs/${encodeURIComponent(started.executionId)}`);
      await expect(page.locator(".wf-bpmn-run-heading")).toBeVisible({ timeout: 30_000 });
      await expect(async () => {
        const refresh = page.getByRole("button", { name: "Refresh run", exact: true });
        if (await refresh.isEnabled()) await refresh.click();
        await expect(page.locator(".wf-bpmn-run-heading")).toContainText("Completed", { timeout: 1_000 });
      }).toPass({ timeout: 30_000, intervals: [500, 1_000] });

      const heading = page.locator(".wf-bpmn-run-heading");
      await expect(heading).toContainText("Completed");
      await expect(heading).not.toContainText("awaiting settlement");
      // Product gap: both end events report Completed/Done. Studio does not name the timed-out path.
      await expect(heading).not.toContainText("Timed out");
      await expect(heading).not.toContainText("timed-out");
      await info.attach("fr-012-timeout-end-event-gap.png", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
      await info.attach("fr-012-timeout-end-event-gap.json", {
        body: JSON.stringify({
          productGap: "Studio does not label which end event a timeout reached",
          observedHeading: await heading.innerText(),
          executionId: started.executionId
        }, null, 2),
        contentType: "application/json"
      });
    } finally {
      await api.dispose();
    }
  });
});

test.describe("FR-014 pending start does not follow a scope change", () => {
  test.describe.configure({ mode: "serial" });

  test("does not apply a pending start after an account switch", async ({ page, playwright }) => {
    const api = await operator(playwright);
    const hold = await holdStart(page);
    try {
      const published = await publishHuman(api, `scope-account-${Date.now()}`, "Account switch wait");
      await signInToStudio(page, "admin");
      await openBpmnProcesses(page);
      const accountRow = await waitForStartableProcess(page, published.sourceReferenceId);

      await accountRow.getByRole("button", { name: "Start process", exact: true }).click();
      await waitForUnconfirmedStart(accountRow);

      await page.getByRole("button", { name: /^Account:/ }).click();
      await page.getByRole("menuitem", { name: "Sign out", exact: true }).click();
      await expect(page.getByLabel("Username", { exact: true })).toBeVisible();
      await expect(page.getByText("Last start: accepted")).toHaveCount(0);

      await signInToStudio(page, "operator");
      await openBpmnProcesses(page);
      await expect(page.getByText("Last start: accepted")).toHaveCount(0);
      await expect(page.getByText("The start outcome is unconfirmed. Resume uses the retained request.")).toHaveCount(0);
      expect(published.sourceReferenceId).toBeTruthy();
    } finally {
      hold.release();
      await hold.dispose();
      await api.dispose();
    }
  });

  test("does not apply a pending start after a backend switch", async ({ page, playwright }) => {
    const api = await operator(playwright);
    const hold = await holdStart(page);
    try {
      const published = await publishHuman(api, `scope-backend-${Date.now()}`, "Backend switch wait");
      await signInToStudio(page, "admin");
      await addPersonalBackend(page, "Same-host alternate", workbenchUrl());
      await openBpmnProcesses(page);
      const backendRow = await waitForStartableProcess(page, published.sourceReferenceId);

      await backendRow.getByRole("button", { name: "Start process", exact: true }).click();
      await waitForUnconfirmedStart(backendRow);

      await page.getByRole("button", { name: /Select backend/ }).click();
      await page.locator(".backend-switcher-popover").getByRole("button", { name: /Same-host alternate.*Connect/ }).click();
      await expect(page.getByRole("alert").filter({ hasText: "Wait for backend changes to finish before switching." })).toBeVisible();
      await waitForSelectedBackend(page, "Default backend");
      await expect(page).toHaveURL(/[?&]backend=legacy/);
      await expect(backendRow.getByText("The start outcome is unconfirmed. Resume uses the retained request.", { exact: true })).toBeVisible();

      hold.release();
      await expect(backendRow.getByText("Last start: accepted")).toBeVisible({ timeout: 30_000 });

      await page.getByRole("button", { name: /Select backend/ }).click();
      await page.locator(".backend-switcher-popover").getByRole("button", { name: /Same-host alternate.*Connect/ }).click();
      await waitForSelectedBackend(page, "Same-host alternate");
      await expect(page).toHaveURL(/[?&]backend=personal-/);
      await expect(page.locator("html")).not.toHaveAttribute("data-committed-backend-url");

      await openBpmnProcesses(page);
      await expect(page.getByText("Last start: accepted")).toHaveCount(0);
      await expect(page.getByText("The start outcome is unconfirmed. Resume uses the retained request.")).toHaveCount(0);
    } finally {
      hold.release();
      await hold.dispose();
      await api.dispose();
    }
  });
});
