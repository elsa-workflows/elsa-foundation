import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { getActivityExecutionValuePayload } from "../api/activityExecutionValuePayload";

afterEach(clearApiCapabilityCache);

describe("activity execution value payload client", () => {
  it("uses the advertised templated relation and validates the response identity and capture mode", async () => {
    const response = {
      evidenceId: "evidence/one",
      captureMode: "DiagnosticSnapshot",
      payload: { kind: "string", preview: "captured" }
    };
    const runtime = context(async url => url === "/capabilities" ? capabilitiesWithPayload : response);
    const controller = new AbortController();

    await expect(getActivityExecutionValuePayload(
      runtime,
      "workflow 1",
      "activity/1",
      "evidence/one",
      "DiagnosticSnapshot",
      controller.signal
    )).resolves.toEqual(response);

    expect(runtime.http.getJson).toHaveBeenNthCalledWith(
      2,
      "/runtime/workflows/instances/workflow%201/activity-executions/activity%2F1/value-evidence/evidence%2Fone/payload",
      { signal: controller.signal }
    );
  });

  it.each([
    [{ evidenceId: "another-evidence", captureMode: "Payload", payload: "value" }, "different evidence ID"],
    [{ evidenceId: "evidence-1", captureMode: "Payload", payload: "value" }, "different capture mode"]
  ])("rejects a payload response that does not match its evidence request", async (response, message) => {
    const runtime = context(async url => url === "/capabilities" ? capabilitiesWithPayload : response);

    await expect(getActivityExecutionValuePayload(
      runtime,
      "workflow-1",
      "activity-1",
      "evidence-1",
      "DiagnosticSnapshot"
    )).rejects.toThrow(message);
  });

  it("does not request evidence when the host does not advertise the payload relation", async () => {
    const runtime = context(async () => capabilitiesWithoutPayload);

    await expect(getActivityExecutionValuePayload(
      runtime,
      "workflow-1",
      "activity-1",
      "evidence-1",
      "Payload"
    )).rejects.toThrow("activity-execution-value-payload");

    expect(runtime.http.getJson).toHaveBeenCalledTimes(1);
    expect(runtime.http.getJson).toHaveBeenCalledWith("/capabilities");
  });

  it("does not request the payload endpoint after cancellation during capability lookup", async () => {
    let resolveCapabilities!: (value: unknown) => void;
    const runtime = context(url => url === "/capabilities"
      ? new Promise(resolve => { resolveCapabilities = resolve; })
      : Promise.resolve({ evidenceId: "evidence-1", captureMode: "Payload", payload: "value" }));
    const controller = new AbortController();
    const request = getActivityExecutionValuePayload(
      runtime,
      "workflow-1",
      "activity-1",
      "evidence-1",
      "Payload",
      controller.signal
    );

    await Promise.resolve();
    controller.abort();
    resolveCapabilities(capabilitiesWithPayload);

    await expect(request).rejects.toMatchObject({ name: "AbortError" });
    expect(runtime.http.getJson).toHaveBeenCalledTimes(1);
  });

  it("does not let cancelled capability lookups block payload requests on another host", async () => {
    let releaseCapabilities!: (value: unknown) => void;
    const oldHost = context(() => new Promise(resolve => { releaseCapabilities = resolve; }));
    const controllers = Array.from({ length: 3 }, () => new AbortController());
    const cancelledRequests = controllers.map(controller => getActivityExecutionValuePayload(
      oldHost, "workflow-old", "activity-old", "evidence-old", "Payload", controller.signal
    ).catch(error => error));
    const newHost = context(async url => url === "/capabilities"
      ? capabilitiesWithPayload
      : { evidenceId: "evidence-new", captureMode: "Payload", payload: "new host value" });

    try {
      await waitUntil(() => vi.mocked(oldHost.http.getJson).mock.calls.length === 1);
      controllers.forEach(controller => controller.abort());
      const request = getActivityExecutionValuePayload(
        newHost, "workflow-new", "activity-new", "evidence-new", "Payload"
      );
      await waitUntil(() => vi.mocked(newHost.http.getJson).mock.calls.length === 2);
      await expect(request).resolves.toMatchObject({ payload: "new host value" });
    } finally {
      releaseCapabilities(capabilitiesWithPayload);
      await Promise.all(cancelledRequests);
    }
    expect(oldHost.http.getJson).toHaveBeenCalledTimes(1);
  });

  it("limits concurrent payload requests while resolving a selected activity's evidence", async () => {
    let active = 0;
    let maximumActive = 0;
    const pending: Array<() => void> = [];
    const runtime = context(async url => {
      if (url === "/capabilities") return capabilitiesWithPayload;
      const evidenceId = decodeURIComponent(url.split("/").at(-2) ?? "");
      active++;
      maximumActive = Math.max(maximumActive, active);
      return new Promise(resolve => {
        pending.push(() => {
          active--;
          resolve({ evidenceId, captureMode: "Payload", payload: evidenceId });
        });
      });
    });
    const requests = Array.from({ length: 5 }, (_, index) => getActivityExecutionValuePayload(
      runtime,
      "workflow-1",
      "activity-1",
      `evidence-${index}`,
      "Payload"
    ));

    await waitUntil(() => pending.length === 3);
    expect(maximumActive).toBe(3);
    pending[0]!();
    await waitUntil(() => pending.length === 4);
    expect(maximumActive).toBe(3);
    pending.slice(1, 4).forEach(release => release());
    await waitUntil(() => pending.length === 5);
    pending[4]!();
    await expect(Promise.all(requests)).resolves.toHaveLength(5);
  });
});

const capabilitiesWithPayload = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [{
      rel: "activity-execution-value-payload",
      href: "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/value-evidence/{evidenceId}/payload"
    }]
  }]
};

const capabilitiesWithoutPayload = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [{ rel: "activity-execution", href: "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}" }]
  }]
};

function context(getJson: (url: string) => Promise<unknown>) {
  return {
    baseUrl: `test://runtime-value-payload-${Math.random()}`,
    http: { getJson: vi.fn(getJson) }
  } as unknown as StudioEndpointContext;
}

async function waitUntil(predicate: () => boolean) {
  for (let attempt = 0; attempt < 50; attempt++) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  throw new Error("Condition was not met before timeout.");
}
