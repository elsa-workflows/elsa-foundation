import { describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
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
  it("inspects exact retained content and copy provenance after draft replacement without a latest fallback", async () => {
    const retained: AgentRevision = { id: "S/old", sourceCopyId: "discarded-draft", operationId: "original-capture", revision: 7,
      fingerprint: "original-fingerprint", content: copywriter() };
    const missing = new Error("Snapshot not found");
    const getJson = vi.fn().mockResolvedValueOnce(retained).mockResolvedValueOnce({ total: 2, items: [retained] }).mockRejectedValueOnce(missing);
    const context = { http: { getJson } } as unknown as StudioEndpointContext;
    const signal = new AbortController().signal;
    expect(await readAgentSourceRevision(context, "writer/one ", "S/old", signal)).toBe(retained);
    expect(await listAgentSourceRevisions(context, "writer/one ", { skip: 1, take: 1 }, signal)).toEqual({ total: 2, items: [retained] });
    await expect(readAgentSourceRevision(context, "writer/one ", "missing", signal)).rejects.toBe(missing);
    expect(getJson.mock.calls).toEqual([
      ["/agent/definitions/writer%2Fone%20/revisions/S%2Fold", { signal }],
      ["/agent/definitions/writer%2Fone%20/revisions?skip=1&take=1", { signal }],
      ["/agent/definitions/writer%2Fone%20/revisions/missing", { signal }]
    ]);
  });

  it("preserves losing local edits on a CAS conflict without retrying or forwarding authority fields", async () => {
    const conflict = new Error("revision-conflict");
    const putJson = vi.fn().mockRejectedValue(conflict);
    const context = { http: { putJson } } as unknown as StudioEndpointContext;
    const draft = { agentId: "writer/one", draftId: "draft two", revision: 7, name: "Local edits", content: copywriter(),
      authority: "not-request-data", tenantId: "not-request-data" };
    const original = structuredClone(draft);
    await expect(saveAgentSource(context, draft)).rejects.toBe(conflict);
    expect(draft).toEqual(original);
    expect(putJson.mock.calls).toEqual([["/agent/definitions/writer%2Fone/drafts/draft%20two",
      { expectedRevision: 7, name: original.name, content: original.content }]]);
  });

  it("keeps an uncertain creation's original identities and typed source contract without manufacturing an activity draft", async () => {
    const unknown = new Error("Reply lost after write");
    const postJson = vi.fn().mockRejectedValue(unknown);
    const context = { http: { postJson } } as unknown as StudioEndpointContext;
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
    const context = { http: { getJson } } as unknown as StudioEndpointContext;
    const signal = new AbortController().signal;
    expect(await listAgentDefinitions(context, { search: " Copywriter ", skip: 1, take: 1 }, signal)).toBe(page);
    expect(await readAgentDraft(context, "writer/one", "new draft", signal)).toBe(draft);
    expect(await readAgentRevision(context, "writer/one", "old draft", "S/1", signal)).toBe(retained);
    expect(getJson.mock.calls).toEqual([
      ["/agent/definitions?skip=1&take=1&search=Copywriter", { signal }],
      ["/agent/definitions/writer%2Fone/drafts/new%20draft", { signal }],
      ["/agent/definitions/writer%2Fone/drafts/old%20draft/revisions/S%2F1", { signal }]
    ]);
  });

  it("captures the observed revision under the caller's stable operation and discards only that copy", async () => {
    const snapshot = { id: "snapshot", sourceCopyId: "draft two", operationId: "review one", revision: 7, fingerprint: "fingerprint", content: copywriter() };
    const postJson = vi.fn().mockResolvedValueOnce(snapshot).mockResolvedValueOnce(true);
    const context = { http: { postJson } } as unknown as StudioEndpointContext;
    const draft: AgentDraft = { agentId: "writer/one", draftId: "draft two", revision: 7, name: "Copywriter", content: copywriter() };
    expect(await captureAgentSource(context, draft, "review one")).toBe(snapshot);
    expect(await discardAgentSource(context, draft)).toBe(true);
    expect(postJson.mock.calls).toEqual([
      ["/agent/definitions/writer%2Fone/drafts/draft%20two/captures", { expectedRevision: 7, operationId: "review one" }],
      ["/agent/definitions/writer%2Fone/drafts/draft%20two/discard", { expectedRevision: 7 }]
    ]);
  });
});
