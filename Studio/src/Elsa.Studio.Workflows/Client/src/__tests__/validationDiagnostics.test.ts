import { describe, expect, it } from "vitest";
import {
  UNRESOLVED_VARIABLE_TYPE,
  collectVariableRepairItems,
  errorType,
  isUnresolvedVariableError,
  parseValidationErrorPath
} from "../validationDiagnostics";

describe("parseValidationErrorPath", () => {
  it("parses an activity input path", () => {
    expect(parseValidationErrorPath("node-1/inputs/Text")).toEqual({
      nodeId: "node-1",
      isWorkflowScope: false,
      bag: "inputs",
      referenceKey: "Text"
    });
  });

  it("parses a workflow variable path", () => {
    expect(parseValidationErrorPath("$workflow/variables/Count")).toEqual({
      nodeId: null,
      isWorkflowScope: true,
      bag: "variables",
      referenceKey: "Count"
    });
  });

  it("parses a bare node path", () => {
    expect(parseValidationErrorPath("node-1")).toEqual({
      nodeId: "node-1",
      isWorkflowScope: false,
      bag: null,
      referenceKey: null
    });
  });

  it("tolerates empty / undefined input", () => {
    expect(parseValidationErrorPath(undefined).nodeId).toBeNull();
    expect(parseValidationErrorPath("").referenceKey).toBeNull();
  });
});

describe("unresolved-variable classification", () => {
  it("reads the type from `type`, falling back to `code`", () => {
    expect(errorType({ type: "X" })).toBe("X");
    expect(errorType({ code: "Y" })).toBe("Y");
    expect(errorType({})).toBe("");
  });

  it("matches only the unresolved-variable type", () => {
    expect(isUnresolvedVariableError({ type: UNRESOLVED_VARIABLE_TYPE })).toBe(true);
    expect(isUnresolvedVariableError({ type: "Graph/OrphanActivity" })).toBe(false);
  });
});

describe("collectVariableRepairItems", () => {
  it("selects only unresolved-variable errors and parses navigable paths (copy/import repair surface)", () => {
    const errors = [
      { path: "node-7/inputs/Text", type: UNRESOLVED_VARIABLE_TYPE, message: "...is not visible from this activity's scope." },
      { path: "node-7", type: "Graph/StartActivity", message: "no start activity" }
    ];

    const items = collectVariableRepairItems(errors);

    expect(items).toHaveLength(1);
    expect(items[0].path.nodeId).toBe("node-7");
    expect(items[0].path.referenceKey).toBe("Text");
    expect(items[0].message).toContain("not visible");
  });

  it("returns an empty list when there are no errors", () => {
    expect(collectVariableRepairItems(null)).toEqual([]);
  });
});
