import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { RuntimeValueEvidenceContent } from "../workflow-editor/RuntimeValueEvidence";
import type { ActivityExecutionInspectionValueSnapshot } from "../workflowTypes";

describe("RuntimeValueEvidenceContent presentation defaults", () => {
  it("keeps shared diagnostic object roots expanded unless compact Activity presentation is requested", () => {
    const snapshot: ActivityExecutionInspectionValueSnapshot = {
      evidenceId: "evidence-1",
      name: "Result",
      subject: "ActivityOutput",
      captureMode: "DiagnosticSnapshot",
      captureState: "diagnosticSnapshotCaptured",
      state: "captured",
      type: { typeName: "Result" },
      capturedAt: "2026-10-18T10:00:00Z",
      payload: null,
      snapshot: {
        kind: "object",
        typeName: "Result",
        properties: [{ name: "value", value: { kind: "number", value: 7 } }],
        truncated: false
      },
      captureReason: "Diagnostic snapshot captured.",
      isSensitive: false,
      accessState: "visible",
      metadata: {}
    };

    const markup = renderToStaticMarkup(<RuntimeValueEvidenceContent snapshot={snapshot} />);
    const template = document.createElement("template");
    template.innerHTML = markup;

    expect(template.content.querySelector<HTMLDetailsElement>(".wf-runtime-snapshot-node")?.open).toBe(true);
    expect(template.content.textContent).toContain("7");
  });
});
