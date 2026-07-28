import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type {
  StudioActivityDescriptor,
  StudioExpressionEditorContribution,
  StudioExpressionAuthoringContext,
  StudioExpressionDocument,
  StudioExpressionToolingClient,
  StudioExpressionToolingResult,
  StudioExpressionValidationResult
} from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import type { ActivityNode, WorkflowDefinitionState } from "../workflowTypes";

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.useFakeTimers();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  vi.useRealTimers();
});

const descriptor: StudioActivityDescriptor = {
  typeName: "WriteLine",
  inputs: [{ name: "Text", typeName: "System.String", isWrapped: true }],
  outputs: [],
  ports: []
};

const expressionEditor: StudioExpressionEditorContribution = {
  id: "test.javascript-validation",
  supports: context => context.syntax === "JavaScript",
  surfaces: {
    inline: ({ value, context, onChange }) => (
      <div>
        <input
          aria-label="Expression source"
          value={String(value ?? "")}
          onFocus={context.onFocus}
          onChange={event => onChange(event.target.value)}
        />
        <output data-testid="validation">{context.validation?.data?.diagnostics[0]?.message ?? "none"}</output>
      </div>
    )
  }
};

function activity(source: string): ActivityNode {
  return {
    nodeId: "activity-1",
    activityVersionId: "WriteLine-v1",
    inputs: [],
    outputs: [],
    text: { typeName: "System.String", expression: { type: "JavaScript", value: source } }
  };
}

function validation(sourceVersion: number, message: string): StudioExpressionToolingResult<StudioExpressionValidationResult> {
  return {
    state: "ready",
    contractVersion: 1,
    expressionType: "JavaScript",
    contextVersion: "context-1",
    data: {
      documentId: "unused",
      sourceVersion,
      contextVersion: "context-1",
      diagnostics: [{
        severity: "error",
        message,
        range: { start: { line: 1, column: 1 }, end: { line: 1, column: 2 } },
        documentId: "unused",
        sourceVersion,
        contextVersion: "context-1"
      }]
    }
  };
}

describe("expression tooling validation presentation", () => {
  it("immediately hides the prior revision while the next debounced validation is pending and then uses only the latest result", async () => {
    const pendingValidations: Array<{ resolve(value: StudioExpressionToolingResult<StudioExpressionValidationResult>): void }> = [];
    const validate = vi.fn((
      _document: StudioExpressionDocument,
      _authoringContext: StudioExpressionAuthoringContext,
      _signal?: AbortSignal
    ) => new Promise<StudioExpressionToolingResult<StudioExpressionValidationResult>>(resolve => pendingValidations.push({ resolve })));
    const tooling = {
      getAuthoringContext: vi.fn(async () => ({
        state: "ready",
        contractVersion: 1,
        expressionType: "JavaScript",
        contextVersion: "context-1",
        data: { version: "context-1", workflowInputs: [], visibleVariables: [], visibleActivityOutputs: [] }
      })),
      validate
    } as unknown as StudioExpressionToolingClient;

    function render(node: ActivityNode) {
      const workflowState: WorkflowDefinitionState = { inputs: [], variables: [], rootActivity: node };
      flushSync(() => root.render(<ActivityPropertiesPanel
          draftId="draft-1"
          expressionTooling={tooling}
          workflowState={workflowState}
          activity={node}
          descriptor={descriptor}
          editors={[]}
          expressionEditors={[expressionEditor]}
          expressionDescriptors={[{ type: "JavaScript", displayName: "JavaScript", editingMode: "text" }]}
          expressionDescriptorStatus="ready"
          descriptorStatus="ready"
          visibleVariables={[]}
          scopeStatus="ready"
          onChange={vi.fn()}
        />));
    }

    render(activity("first"));
    container.querySelector<HTMLInputElement>("[aria-label='Expression source']")!.focus();
    await vi.advanceTimersByTimeAsync(180);
    await vi.waitFor(() => expect(pendingValidations).toHaveLength(1));
    pendingValidations[0]!.resolve(validation(0, "old revision"));
    await vi.waitFor(() => expect(container.querySelector("output")?.textContent).toBe("old revision"));

    render(activity("second"));
    await vi.advanceTimersByTimeAsync(0);
    expect(container.querySelector("output")?.textContent).toBe("none");
    expect(validate).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(180);
    await vi.waitFor(() => expect(pendingValidations).toHaveLength(2));
    pendingValidations[1]!.resolve(validation(1, "latest revision"));
    await vi.waitFor(() => expect(container.querySelector("output")?.textContent).toBe("latest revision"));
    expect(validate.mock.calls.map(([document]) => ({ source: document.source, sourceVersion: document.sourceVersion }))).toEqual([
      { source: "first", sourceVersion: 0 },
      { source: "second", sourceVersion: 1 }
    ]);
  });
});
