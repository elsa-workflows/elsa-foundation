import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { deliverWorkflowTestRunWaitEvent, getWorkflowTestRunWaits, stopWorkflowTestRun } from "../api/publishing";

afterEach(clearApiCapabilityCache);

describe("workflow Test Run control API", () => {
  it("uses advertised wait and event relations and omits the route-bound run ID from the body", async () => {
    const response = { capabilities: [{
      id: "elsa.api.publishing",
      contractVersion: "1",
      links: [
        { rel: "workflow-test-run-waits", href: "publishing/workflow-test-runs/{testRunId}/waits", templated: true },
        { rel: "workflow-test-run-events", href: "publishing/workflow-test-runs/{testRunId}/events", templated: true },
        { rel: "workflow-test-run-stop", href: "publishing/workflow-test-runs/{testRunId}/stop", templated: true }
      ]
    }] };
    const getJson = vi.fn(async (url: string) => url === "/elsa/api/capabilities" ? response : { waits: [] });
    const postJson = vi.fn(async () => ({ status: "Delivered" }));
    const requestJson = vi.fn(async () => ({ testRunId: "test/run", status: "Stopped" }));
    const context = { baseUrl: "https://studio.example/elsa/api", http: { getJson, postJson, requestJson } } as unknown as StudioEndpointContext;

    await getWorkflowTestRunWaits(context, "test/run");
    await deliverWorkflowTestRunWaitEvent(context, "test/run", {
      waitId: "bookmark/1",
      deliveryId: "a56240db-cc7a-4e1a-a694-3e12335cdb11",
      modeId: "event",
      data: { payload: { id: 7 } }
    });
    await stopWorkflowTestRun(context, "test/run");

    expect(getJson).toHaveBeenLastCalledWith("/elsa/api/publishing/workflow-test-runs/test%2Frun/waits");
    expect(postJson).toHaveBeenCalledWith("/elsa/api/publishing/workflow-test-runs/test%2Frun/events", {
      waitId: "bookmark/1",
      deliveryId: "a56240db-cc7a-4e1a-a694-3e12335cdb11",
      modeId: "event",
      data: { payload: { id: 7 } }
    });
    expect(requestJson).toHaveBeenCalledWith("/elsa/api/publishing/workflow-test-runs/test%2Frun/stop", { method: "POST" });
  });
});
