import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { createCanvasEditorPreferences } from "../graph-authoring/canvasEditorPreferences";

function api(subject = "user", tenantId = "tenant", hostId = "studio", baseUrl = "https://backend.example") {
  const missing = Object.assign(new Error("Preference endpoint is not composed"), { status: 404 });
  return { backend: { baseUrl, http: { getJson: vi.fn(async () => { throw missing; }), putJson: vi.fn(async () => { throw missing; }) } },
    runtime: { hostId, identity: { subject, tenantId } } } as unknown as Pick<ElsaStudioModuleApi, "backend" | "runtime">;
}

describe("canvas editor user preference", () => {
  beforeEach(() => localStorage.clear());
  it("retains an authenticated choice when a host lacks the preferences endpoint, scoped to its user/tenant/host/backend", async () => {
    const initial = api();
    await createCanvasEditorPreferences(initial).save("bpmn-js");
    expect(await createCanvasEditorPreferences(api()).load()).toBe("bpmn-js");
    for (const anotherScope of [api("another"), api("user", "another"), api("user", "tenant", "another"), api("user", "tenant", "studio", "https://another.example")]) {
      expect(await createCanvasEditorPreferences(anotherScope).load()).toBeNull();
    }
    expect(initial.backend.http.putJson).toHaveBeenCalledWith("/_elsa/studio/preferences/workflow-authoring",
      { schemaVersion: 1, value: { editorId: "bpmn-js" } }, { headers: { "X-Elsa-Studio-Host-Id": "studio", "If-None-Match": "*" } });
  });
  it("uses the backend revision and preserves a conflict instead of writing an invisible local override", async () => {
    const scoped = api();
    scoped.backend.http.getJson = vi.fn(async () => ({ schemaVersion: 1, revision: "saved", value: { editorId: "bpmn-js" } })) as typeof scoped.backend.http.getJson;
    const conflict = Object.assign(new Error("Preference changed elsewhere"), { status: 412 });
    scoped.backend.http.putJson = vi.fn(async () => { throw conflict; });
    const store = createCanvasEditorPreferences(scoped);
    expect(await store.load()).toBe("bpmn-js");
    await expect(store.save("integrated")).rejects.toBe(conflict);
    expect(localStorage.length).toBe(0);
    expect(scoped.backend.http.putJson).toHaveBeenCalledWith(expect.any(String), expect.any(Object), { headers: { "X-Elsa-Studio-Host-Id": "studio", "If-Match": '"saved"' } });
  });
});
