import { beforeEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { promoteDraft } from "../api/workflowDesign";

beforeEach(() => clearApiCapabilityCache());
function fixture() {
  const capabilities = { capabilities: [{ id: "elsa.api.workflow-design", contractVersion: "1", links: [
    { rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true },
    { rel: "workflow-draft-promote-exact", href: "design/workflows/drafts/{draftId}/promote-exact", templated: true }
  ] }] };
  const getJson = vi.fn().mockResolvedValue(capabilities), postJson = vi.fn().mockResolvedValue({ id: "version", version: "1.0.0" });
  const context = { baseUrl: "https://backend.example/shell", http: { getJson, postJson } } as unknown as StudioEndpointContext;
  return { context, capabilities, getJson, postJson };
}

it("retains the ordinary promotion request for existing callers", async () => {
  const view = fixture(); await promoteDraft(view.context, "draft");
  expect(view.postJson).toHaveBeenCalledWith("/shell/design/workflows/drafts/draft/promote", {});
});

it("sends the retained direct promotion key with automatic authentication retry disabled", async () => {
  const view = fixture(); const abort = new AbortController();
  await promoteDraft(view.context, "draft", undefined, "same-operation", abort.signal);
  expect(view.postJson).toHaveBeenCalledWith("/shell/design/workflows/drafts/draft/promote", { operationKey: "same-operation" }, { retryOnUnauthorized: false, signal: abort.signal });
});

it("never sends a promotion when navigation aborts capability discovery", async () => {
  const view = fixture(); let resolve!: (value: object) => void;
  view.getJson.mockReturnValueOnce(new Promise<object>(done => { resolve = done; }));
  const abort = new AbortController(); const pending = promoteDraft(view.context, "draft", undefined, "same-operation", abort.signal);
  abort.abort(); resolve(view.capabilities);
  await expect(pending).rejects.toMatchObject({ name: "AbortError" }); expect(view.postJson).not.toHaveBeenCalled();
});
