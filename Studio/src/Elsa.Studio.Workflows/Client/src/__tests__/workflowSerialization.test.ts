import { describe, expect, it } from "vitest";
import { buildDraftFromJson, buildExportPayload, serializeDraftToJson } from "../workflowSerialization";
import type { WorkflowDraft } from "../workflowTypes";

const draft = (): WorkflowDraft => ({
  id: "draft-1",
  definitionId: "def-1",
  state: {
    variables: [{
      referenceKey: "count-ref",
      name: "count",
      type: { alias: "Int32", collectionKind: "Single" },
      storageDriverType: null,
      default: null
    }],
    rootActivity: { nodeId: "root", activityVersionId: "v1", inputs: [], outputs: [] },
    inputs: [],
    outputs: []
  },
  layout: [{ nodeId: "root", x: 10, y: 20 }],
  validationErrors: []
});

describe("workflow serialization", () => {
  it("builds an export payload with name, id, state and layout", () => {
    const payload = buildExportPayload(draft(), "My Workflow");
    expect(payload.name).toBe("My Workflow");
    expect(payload.definitionId).toBe("def-1");
    expect(payload.layout).toEqual([{ nodeId: "root", x: 10, y: 20 }]);
    expect(payload.state.rootActivity?.nodeId).toBe("root");
  });

  it("omits the name when none is given", () => {
    expect("name" in buildExportPayload(draft())).toBe(false);
  });

  it("round-trips draft <-> JSON without losing state", () => {
    const original = draft();
    const result = buildDraftFromJson(serializeDraftToJson(original), original);
    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.draft.state.variables).toEqual(original.state.variables);
      expect(result.draft.layout).toEqual(original.layout);
      // Preserves draft identity fields not present in the serialized JSON.
      expect(result.draft.id).toBe("draft-1");
    }
  });

  it("reports a parse error for malformed JSON", () => {
    const result = buildDraftFromJson("{ not json", draft());
    expect(result.ok).toBe(false);
  });

  it("rejects JSON without a state object", () => {
    const result = buildDraftFromJson(JSON.stringify({ layout: [] }), draft());
    expect(result.ok).toBe(false);
  });

  it("rejects a non-array layout", () => {
    const result = buildDraftFromJson(JSON.stringify({ state: {}, layout: 5 }), draft());
    expect(result.ok).toBe(false);
  });

  it("falls back to the current layout when omitted", () => {
    const current = draft();
    const result = buildDraftFromJson(JSON.stringify({ state: {} }), current);
    expect(result.ok).toBe(true);
    if (result.ok) expect(result.draft.layout).toEqual(current.layout);
  });
});
