import { describe, expect, it, vi } from "vitest";
import { createDecision, deleteCase, exportDecision, listDecisions, runCases } from "../decisionsApi";
import { asset, stubApi } from "./helpers";
describe("authenticated capability-discovered Decisions API", () => {
  it("preserves the selected shell prefix and exact tagged numeric text", async () => {
    const api = stubApi(); await listDecisions(api);
    expect(api.backend.http.getJson).toHaveBeenCalledWith("/tenant/decisions", { signal: undefined });
    const created = { name: "Approval", xml: asset.xml!, isSensitive: false };
    await createDecision(api, created);
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions", expect.objectContaining(created));
    await runCases(api, asset, [{ id: "case1", name: "Boundary", revision: "c1", inputs: { amount: { kind: "number", value: "9007199254740993" } }, expectedStatus: "Success", expectedResult: { kind: "number", value: "9007199254740993" } }]);
    expect(api.backend.http.postJson).toHaveBeenLastCalledWith("/tenant/decisions/approval/test", expect.objectContaining({ candidateRevision: "r1", candidateHash: "hash1", cases: [{ id: "case1", revision: "c1" }] }));
  });
  it("includes the revision and operation key for case deletion", async () => {
    const api = stubApi(); await deleteCase(api, asset.id, { id: "case1", name: "Case", revision: "c1", inputs: {}, expectedStatus: "Success", expectedResult: null });
    expect(api.backend.http.deleteJson.mock.calls[0][0]).toMatch(/cases\/case1\?expectedRevision=c1&operationId=/);
  });
  it("requires authenticated response transport for XML downloads without global fetch fallback", async () => {
    const api = stubApi(), fetch = vi.spyOn(globalThis, "fetch"); api.backend.http.requestResponse = undefined as never;
    await expect(exportDecision(api, asset.id)).rejects.toThrow("authenticated decision downloads");
    expect(fetch).not.toHaveBeenCalled(); fetch.mockRestore();
  });
});
