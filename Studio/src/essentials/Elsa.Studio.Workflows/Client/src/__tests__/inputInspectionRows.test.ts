import { describe, expect, it } from "vitest";
import { buildInputInspectionRows } from "../inputInspectionRows";
import type { ActivityExecutionInspectionValueSnapshot, WorkflowExecutableInputBinding } from "../workflowTypes";

describe("buildInputInspectionRows", () => {
  it("joins declarations, authored source, compiled behavior, and evidence only by ReferenceKey", () => {
    const rows = buildInputInspectionRows({
      declarations: [{ referenceKey: "message-key", name: "Message", displayName: "Message", typeName: "System.String" }],
      authoredSources: [{ executableNodeId: "node-1", inputKey: "message-key", expressionType: "JavaScript", value: "variables.message" }],
      compiledBindings: [{ inputKey: "message-key", inputName: "Message", source: "Expression", expression: { language: "JavaScript", expression: "variables.message" } }],
      evaluations: [evidence({ inputKey: "message-key", name: "Renamed", evaluationSequence: 1, evaluationId: "evaluation-1" })]
    });

    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({
      rowKey: "message-key",
      inputKey: "message-key",
      name: "Message",
      declaredType: "System.String",
      states: []
    });
    expect(rows[0]?.authoredSource?.value).toBe("variables.message");
    expect(rows[0]?.latestEvaluation?.name).toBe("Renamed");
  });

  it("never correlates keyless legacy evidence with a keyed declaration by display name", () => {
    const rows = buildInputInspectionRows({
      declarations: [{ referenceKey: "message-key", name: "Message", typeName: "System.String" }],
      evaluations: [evidence({ inputKey: undefined, name: "Message", evaluationId: undefined, evaluationSequence: undefined })]
    });

    expect(rows).toHaveLength(2);
    expect(rows[0]?.states).toEqual(expect.arrayContaining(["missingAuthoredSource", "missingCompiledBinding", "noEvaluation"]));
    expect(rows[1]?.rowKey).toMatch(/^legacy:evidence:/);
    expect(rows[1]?.states).toEqual(expect.arrayContaining(["legacyIdentity", "orphanEvidence"]));
  });

  it("orders evaluations by sequence and deduplicates scheduler replay by evaluationId", () => {
    const rows = buildInputInspectionRows({
      evaluations: [
        evidence({ inputKey: "value-key", evaluationSequence: 2, evaluationId: "resume", capturedAt: "2026-07-16T10:02:00Z" }),
        evidence({ inputKey: "value-key", evaluationSequence: 1, evaluationId: "invoke", capturedAt: "2026-07-16T10:01:00Z" }),
        evidence({ inputKey: "value-key", evaluationSequence: 2, evaluationId: "resume", capturedAt: "2026-07-16T10:03:00Z" })
      ]
    });

    expect(rows[0]?.evaluations.map(item => item.evaluationId)).toEqual(["invoke", "resume"]);
    expect(rows[0]?.latestEvaluation?.evaluationId).toBe("resume");
  });

  it("keeps duplicate and one-sided records visible and reports protected source independently", () => {
    const duplicate: WorkflowExecutableInputBinding = { inputKey: "secret-key", inputName: "Secret", source: "Reference", reference: { id: "safe-reference" } };
    const rows = buildInputInspectionRows({
      declarations: [{ referenceKey: "secret-key", name: "Secret", typeName: "System.String" }],
      compiledBindings: [duplicate, { ...duplicate, reference: { id: "other-reference" } }],
      authoredSourceAccess: "unavailable"
    });

    expect(rows).toHaveLength(2);
    expect(rows[0]?.states).toEqual(expect.arrayContaining(["compiledOnly", "sourceUnavailable", "unavailable", "duplicateKey"]));
    expect(rows[1]?.rowKey).toBe("secret-key:duplicate:1");
    expect(rows[1]?.states).toContain("duplicateKey");
  });

  it("preserves unknown structured binding discriminators without consulting legacy summary", () => {
    const rows = buildInputInspectionRows({
      compiledBindings: [{
        inputKey: "future-key",
        inputName: "Future",
        source: "FutureBinding",
        futureBinding: { discriminator: "opaque", value: 42 },
        summary: "must never be parsed"
      }]
    });

    expect(rows[0]?.compiledBinding?.futureBinding).toEqual({ discriminator: "opaque", value: 42 });
    expect(rows[0]?.states).toEqual(expect.arrayContaining(["missingDeclaration", "compiledOnly", "noEvaluation"]));
  });

  it("derives runtime failure and protected evidence states", () => {
    const rows = buildInputInspectionRows({
      evaluations: [evidence({
        inputKey: "failed-key",
        accessState: "redacted",
        failure: { code: "input_evaluation_failed", message: "Safe diagnostic", incidentId: "incident-1" }
      })]
    });

    expect(rows[0]?.states).toEqual(expect.arrayContaining(["orphanEvidence", "redacted", "failedEvaluation"]));
  });
});

function evidence(overrides: Partial<ActivityExecutionInspectionValueSnapshot> = {}): ActivityExecutionInspectionValueSnapshot {
  return {
    name: "Value",
    subject: "ActivityInput",
    inputKey: "value-key",
    evaluationId: "evaluation-1",
    evaluationPhase: "Invoke",
    evaluationSequence: 1,
    captureMode: "DiagnosticSnapshot",
    state: "captured",
    type: { typeName: "System.String" },
    capturedAt: "2026-07-16T10:00:00Z",
    snapshot: { kind: "string", preview: "runtime value", length: 13, truncated: false },
    captureReason: "Captured.",
    isSensitive: false,
    accessState: "visible",
    metadata: {},
    ...overrides
  };
}
