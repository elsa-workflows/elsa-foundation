import { describe, expect, it, vi } from "vitest";
import { createHttpClient, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  captureAgentSource, createAgentSource, discardAgentSource, listAgentDefinitions, listAgentSourceRevisions,
  readAgentDraft, readAgentRevision, readAgentSourceRevision, saveAgentSource, type AgentContent, type AgentDraft, type AgentRevision
} from "../agentSourcesApi";

function copywriter(): AgentContent {
  return {
    instructions: "Write a headline and short blurb from the product brief.",
    model: { providerKey: "openai", connectionName: "copywriter", modelId: "model" },
    inputs: [{ key: "brief", name: "productBrief", type: { alias: "String", collectionKind: "Single" }, required: true, allowNull: false }],
    results: ["headline", "blurb"].map(name => ({ key: name, name, type: { alias: "String", collectionKind: "Single" as const }, required: true, allowNull: false })),
    settings: { futureSetting: "preserve" }, limits: { maximumModelCalls: 1, timeoutMilliseconds: 10000 }, allowContinuity: false
  };
}

describe("Agent-owned source transport", () => {
  it.each(["https://studio.test/", "https://studio.test/authoring/"])("keeps opaque selectors through the actual SDK and selected base %s", async baseUrl => {
    for (const [agentId, draftId] of [["..", "."], [".", ".."], ["writer/one", "draft/two"], ["writer%2Fone", "%25 + 名 "]]) {
      const fetchRequest = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) =>
        new Response("{}", { status: 200, headers: { "Content-Type": "application/json" } }));
      const context: StudioEndpointContext = { baseUrl, http: createHttpClient(baseUrl, { fetch: fetchRequest, applyTimeout: false }) };
      const draft: AgentDraft = { agentId, draftId, name: "Copywriter", revision: 7, content: copywriter() };
      await createAgentSource(context, draft);
      await readAgentDraft(context, agentId, draftId);
      await saveAgentSource(context, draft);
      await captureAgentSource(context, draft, "review one");
      await discardAgentSource(context, draft);
      for (const [requestUrl, init] of fetchRequest.mock.calls) {
        const url = new URL(String(requestUrl));
        expect(url.pathname.startsWith(new URL(baseUrl).pathname + "agent/definitions")).toBe(true);
        const body = init?.body ? JSON.parse(String(init.body)) as { agentId?: string; draftId?: string } : {};
        // Compare the logical selectors seen after the real SDK's URL resolution.
        // The legacy path form is read only by this regression oracle, not used as
        // a fallback by the client. Dot normalization makes those selectors differ.
        const segments = url.pathname.split("/");
        const index = segments.indexOf("definitions");
        const selectedAgent = body.agentId ?? url.searchParams.get("agentId") ?? decodeURIComponent(segments[index + 1] ?? "");
        const selectedDraft = body.draftId ?? url.searchParams.get("draftId") ?? decodeURIComponent(segments[index + 3] ?? "");
        expect(selectedAgent).toBe(agentId);
        expect(selectedDraft).toBe(draftId);
      }
    }
  });

  it("inspects exact retained content and copy provenance after draft replacement without a latest fallback", async () => {
    const retained: AgentRevision = { id: "S/old", sourceCopyId: "discarded-draft", operationId: "original-capture", revision: 7,
      fingerprint: "original-fingerprint", content: copywriter() };
    const missing = new Error("Snapshot not found");
    const getJson = vi.fn().mockResolvedValueOnce(retained).mockResolvedValueOnce({ total: 2, items: [retained] }).mockRejectedValueOnce(missing);
    const context = { baseUrl: "https://studio.test/", http: { getJson } } as unknown as StudioEndpointContext;
    const signal = new AbortController().signal;
    expect(await readAgentSourceRevision(context, "writer/one ", "S/old", signal)).toBe(retained);
    expect(await listAgentSourceRevisions(context, "writer/one ", { skip: 1, take: 1 }, signal)).toEqual({ total: 2, items: [retained] });
    await expect(readAgentSourceRevision(context, "writer/one ", "missing", signal)).rejects.toBe(missing);
    expect(getJson.mock.calls).toEqual([
      ["/agent/definitions/revision?agentId=writer%2Fone+&snapshotId=S%2Fold", { signal }],
      ["/agent/definitions/revisions?agentId=writer%2Fone+&skip=1&take=1", { signal }],
      ["/agent/definitions/revision?agentId=writer%2Fone+&snapshotId=missing", { signal }]
    ]);
  });

  it("preserves losing local edits on a CAS conflict without retrying or forwarding authority fields", async () => {
    const conflict = new Error("revision-conflict");
    const putJson = vi.fn().mockRejectedValue(conflict);
    const context = { baseUrl: "https://studio.test/", http: { putJson } } as unknown as StudioEndpointContext;
    const draft = { agentId: "writer/one", draftId: "draft two", revision: 7, name: "Local edits", content: copywriter(),
      authority: "not-request-data", tenantId: "not-request-data" };
    const original = structuredClone(draft);
    await expect(saveAgentSource(context, draft)).rejects.toBe(conflict);
    expect(draft).toEqual(original);
    expect(putJson.mock.calls).toEqual([["/agent/definitions/draft",
      { agentId: original.agentId, draftId: original.draftId, expectedRevision: 7, name: original.name, content: original.content }]]);
  });

  it("keeps an uncertain creation's original identities and typed source contract without manufacturing an activity draft", async () => {
    const unknown = new Error("Reply lost after write");
    const postJson = vi.fn().mockRejectedValue(unknown);
    const context = { baseUrl: "https://studio.test/", http: { postJson } } as unknown as StudioEndpointContext;
    const request = { agentId: "writer/one", draftId: "original draft", name: "Copywriter", content: copywriter(),
      authority: "not-request-data", tenantId: "not-request-data" };
    const original = structuredClone(request);
    await expect(createAgentSource(context, request)).rejects.toBe(unknown);
    expect(request).toEqual(original);
    expect(postJson.mock.calls).toEqual([["/agent/definitions",
      { agentId: original.agentId, draftId: original.draftId, name: original.name, content: original.content }]]);
  });

  it("preserves a discarded directory entry as metadata and keeps copy-scoped inspection explicit", async () => {
    const page = { total: 2, items: [{ agentId: "writer", name: "Copywriter", sourceCopyId: "old draft", draftId: null, revision: null }] };
    const draft: AgentDraft = { agentId: "writer/one", draftId: "new draft", revision: 0, name: "Replacement", content: copywriter() };
    const retained: AgentRevision = { id: "S/1", sourceCopyId: "old draft", operationId: "original-capture", revision: 7,
      fingerprint: "original-fingerprint", content: copywriter() };
    const getJson = vi.fn().mockResolvedValueOnce(page).mockResolvedValueOnce(draft).mockResolvedValueOnce(retained);
    const context = { baseUrl: "https://studio.test/", http: { getJson } } as unknown as StudioEndpointContext;
    const signal = new AbortController().signal;
    expect(await listAgentDefinitions(context, { search: " Copywriter ", skip: 1, take: 1 }, signal)).toBe(page);
    expect(await readAgentDraft(context, "writer/one", "new draft", signal)).toBe(draft);
    expect(await readAgentRevision(context, "writer/one", "old draft", "S/1", signal)).toBe(retained);
    expect(getJson.mock.calls).toEqual([
      ["/agent/definitions?skip=1&take=1&search=Copywriter", { signal }],
      ["/agent/definitions/draft?agentId=writer%2Fone&draftId=new+draft", { signal }],
      ["/agent/definitions/draft-revision?agentId=writer%2Fone&draftId=old+draft&snapshotId=S%2F1", { signal }]
    ]);
  });

  it("captures the observed revision under the caller's stable operation and discards only that copy", async () => {
    const snapshot = { id: "snapshot", sourceCopyId: "draft two", operationId: "review one", revision: 7, fingerprint: "fingerprint", content: copywriter() };
    const postJson = vi.fn().mockResolvedValueOnce(snapshot).mockResolvedValueOnce(true);
    const context = { baseUrl: "https://studio.test/", http: { postJson } } as unknown as StudioEndpointContext;
    const draft: AgentDraft = { agentId: "writer/one", draftId: "draft two", revision: 7, name: "Copywriter", content: copywriter() };
    expect(await captureAgentSource(context, draft, "review one")).toBe(snapshot);
    expect(await discardAgentSource(context, draft)).toBe(true);
    expect(postJson.mock.calls).toEqual([
      ["/agent/definitions/captures", { agentId: "writer/one", draftId: "draft two", expectedRevision: 7, operationId: "review one" }],
      ["/agent/definitions/discard", { agentId: "writer/one", draftId: "draft two", expectedRevision: 7 }]
    ]);
  });
});
