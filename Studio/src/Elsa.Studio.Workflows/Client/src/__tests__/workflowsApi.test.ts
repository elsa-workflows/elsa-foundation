import { afterEach, describe, expect, it, vi } from "vitest";
import { listDefinitions } from "../workflowsApi";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("workflows API", () => {
  it("times out stalled backend requests with a configuration-focused error", async () => {
    vi.useFakeTimers();
    vi.stubGlobal("fetch", vi.fn((_input: RequestInfo | URL, init?: RequestInit) => new Promise((_resolve, reject) => {
      init?.signal?.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")));
    })));

    const request = listDefinitions(context(), { search: "", state: "active", page: 1, pageSize: 10 });
    const expectation = expect(request).rejects.toThrow("workflow-management API is responding");
    await vi.advanceTimersByTimeAsync(10000);

    await expectation;
  });
});

function context(): StudioEndpointContext {
  return {
    baseUrl: "https://localhost:7243/",
    http: {
      getJson: async () => ({}),
      postJson: async () => ({})
    }
  };
}
