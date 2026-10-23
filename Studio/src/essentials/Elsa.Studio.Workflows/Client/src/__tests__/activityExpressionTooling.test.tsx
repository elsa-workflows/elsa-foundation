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
          data-session-scope={context.editorSessionScope}
          value={String(value ?? "")}
          onBlur={context.onBlur}
          onFocus={context.onFocus}
          onChange={event => onChange(event.target.value)}
        />
        <output data-testid="authoring-state">{context.authoringContext?.state ?? "none"}</output>
        <output data-testid="authoring-data">{context.authoringContext?.data ? "present" : "none"}</output>
        <output data-testid="validation-state">{context.validation?.state ?? "none"}</output>
        <output data-testid="validation-data">{context.validation?.data ? "present" : "none"}</output>
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

function readyAuthoringContext(): StudioExpressionToolingResult<StudioExpressionAuthoringContext> {
  return {
    state: "ready",
    contractVersion: 1,
    expressionType: "JavaScript",
    contextVersion: "context-1",
    data: {
      version: "context-1",
      workflowInputs: [],
      visibleVariables: [],
      visibleActivityOutputs: []
    }
  };
}

function incompatibleAuthoringContext(): StudioExpressionToolingResult<StudioExpressionAuthoringContext> {
  return { state: "incompatible", contractVersion: 1, expressionType: "JavaScript" };
}

function incompatibleValidation(): StudioExpressionToolingResult<StudioExpressionValidationResult> {
  return { state: "incompatible", contractVersion: 1, expressionType: "JavaScript" };
}

function renderPanel(
  node: ActivityNode,
  expressionTooling: StudioExpressionToolingClient,
  expressionEditorSessionScope = "workflow-editor-1",
  workflowState: WorkflowDefinitionState = { inputs: [], variables: [], rootActivity: node }
) {
  flushSync(() => root.render(<ActivityPropertiesPanel
    draftId="draft-1"
    expressionEditorSessionScope={expressionEditorSessionScope}
    expressionTooling={expressionTooling}
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

function editorOutput(testId: string) {
  return container.querySelector<HTMLOutputElement>(`output[data-testid="${testId}"]`)?.textContent;
}

describe("expression tooling validation presentation", () => {
  it("validates a supported-empty context immediately when the editor loses focus", async () => {
    const validate = vi.fn(async () => validation(0, "empty-context diagnostic"));
    const tooling = {
      getAuthoringContext: vi.fn(async () => ({
        state: "supported-empty",
        contractVersion: 1,
        expressionType: "JavaScript",
        contextVersion: "context-1",
        data: {
          version: "context-1",
          capabilities: { semanticValidation: true },
          workflowInputs: [],
          visibleVariables: [],
          visibleActivityOutputs: []
        }
      })),
      validate
    } as unknown as StudioExpressionToolingClient;
    const node = activity("invalid");

    flushSync(() => root.render(<ActivityPropertiesPanel
      draftId="draft-1"
      expressionEditorSessionScope="workflow-editor-1"
      expressionTooling={tooling}
      workflowState={{ inputs: [], variables: [], rootActivity: node }}
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

    const input = container.querySelector<HTMLInputElement>("[aria-label='Expression source']")!;
    input.focus();
    input.blur();

    expect(input.dataset.sessionScope).toBe("workflow-editor-1");
    await vi.waitFor(() => expect(validate).toHaveBeenCalledOnce());
    expect(vi.getTimerCount()).toBe(0);
    await vi.waitFor(() => expect(editorOutput("validation")).toBe("empty-context diagnostic"));
  });

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
    await vi.waitFor(() => expect(editorOutput("validation")).toBe("old revision"));

    render(activity("second"));
    await vi.advanceTimersByTimeAsync(0);
    expect(editorOutput("validation")).toBe("none");
    expect(validate).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(180);
    await vi.waitFor(() => expect(pendingValidations).toHaveLength(2));
    pendingValidations[1]!.resolve(validation(1, "latest revision"));
    await vi.waitFor(() => expect(editorOutput("validation")).toBe("latest revision"));
    expect(validate.mock.calls.map(([document]) => ({ source: document.source, sourceVersion: document.sourceVersion }))).toEqual([
      { source: "first", sourceVersion: 0 },
      { source: "second", sourceVersion: 1 }
    ]);
  });

  it("retains only an incompatible authoring state through rapid edits to the same expression", async () => {
    const tooling = {
      getAuthoringContext: vi.fn(async () => incompatibleAuthoringContext()),
      validate: vi.fn()
    } as unknown as StudioExpressionToolingClient;

    renderPanel(activity("first"), tooling);
    container.querySelector<HTMLInputElement>("[aria-label='Expression source']")!.focus();
    await vi.advanceTimersByTimeAsync(180);
    await vi.waitFor(() => expect(tooling.getAuthoringContext).toHaveBeenCalledOnce());
    await vi.waitFor(() => expect(editorOutput("authoring-state")).toBe("incompatible"));
    expect(editorOutput("authoring-data")).toBe("none");

    renderPanel(activity("second"), tooling);
    await vi.advanceTimersByTimeAsync(0);
    expect(editorOutput("authoring-state")).toBe("incompatible");
    expect(editorOutput("authoring-data")).toBe("none");

    renderPanel(activity("third"), tooling);
    await vi.advanceTimersByTimeAsync(0);
    expect(editorOutput("authoring-state")).toBe("incompatible");
    expect(editorOutput("authoring-data")).toBe("none");
    expect(tooling.validate).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(180);
    await vi.waitFor(() => expect(tooling.getAuthoringContext).toHaveBeenCalledTimes(2));
    expect(editorOutput("authoring-state")).toBe("incompatible");
    expect(editorOutput("authoring-data")).toBe("none");
  });

  it("keeps a validation failure visible without its diagnostics until fresh validation replaces it", async () => {
    const pendingValidations: Array<{ resolve(value: StudioExpressionToolingResult<StudioExpressionValidationResult>): void }> = [];
    let validationRequestCount = 0;
    const tooling = {
      getAuthoringContext: vi.fn(async () => readyAuthoringContext()),
      validate: vi.fn((
        _document: StudioExpressionDocument,
        _authoringContext: StudioExpressionAuthoringContext,
        _signal?: AbortSignal
      ) => {
        validationRequestCount++;
        return validationRequestCount === 1
          ? Promise.resolve(incompatibleValidation())
          : new Promise<StudioExpressionToolingResult<StudioExpressionValidationResult>>(resolve => pendingValidations.push({ resolve }));
      })
    } as unknown as StudioExpressionToolingClient;

    renderPanel(activity("first"), tooling);
    container.querySelector<HTMLInputElement>("[aria-label='Expression source']")!.focus();
    await vi.advanceTimersByTimeAsync(180);
    await vi.waitFor(() => expect(tooling.validate).toHaveBeenCalledOnce());
    await vi.waitFor(() => expect(editorOutput("authoring-state")).toBe("ready"));
    expect(editorOutput("authoring-data")).toBe("present");
    await vi.waitFor(() => expect(editorOutput("validation-state")).toBe("incompatible"));
    expect(editorOutput("validation-data")).toBe("none");

    renderPanel(activity("second"), tooling);
    await vi.advanceTimersByTimeAsync(0);
    expect(editorOutput("authoring-state")).toBe("ready");
    expect(editorOutput("authoring-data")).toBe("none");
    expect(editorOutput("validation-state")).toBe("incompatible");
    expect(editorOutput("validation-data")).toBe("none");
    expect(editorOutput("validation")).toBe("none");

    await vi.advanceTimersByTimeAsync(180);
    await vi.waitFor(() => expect(tooling.validate).toHaveBeenCalledTimes(2));
    expect(editorOutput("authoring-state")).toBe("ready");
    await vi.waitFor(() => expect(editorOutput("authoring-data")).toBe("present"));
    expect(editorOutput("authoring-data")).toBe("present");
    expect(editorOutput("validation-state")).toBe("incompatible");
    expect(editorOutput("validation-data")).toBe("none");

    pendingValidations[0]!.resolve(validation(1, "fresh revision diagnostic"));
    await vi.waitFor(() => expect(editorOutput("validation")).toBe("fresh revision diagnostic"));
    expect(editorOutput("validation-state")).toBe("ready");
    expect(editorOutput("validation-data")).toBe("present");
  });

  it("clears retained status on scope and authorization changes and ignores a canceled context response", async () => {
    const pendingContexts: Array<{
      signal?: AbortSignal;
      resolve(value: StudioExpressionToolingResult<StudioExpressionAuthoringContext>): void;
    }> = [];
    let contextRequestCount = 0;
    const tooling = {
      getAuthoringContext: vi.fn((
        _document: StudioExpressionDocument,
        _state: unknown,
        signal?: AbortSignal
      ) => {
        contextRequestCount++;
        return contextRequestCount === 1
          ? Promise.resolve(readyAuthoringContext())
          : new Promise<StudioExpressionToolingResult<StudioExpressionAuthoringContext>>(resolve => pendingContexts.push({ resolve, signal }));
      }),
      validate: vi.fn(async () => incompatibleValidation())
    } as unknown as StudioExpressionToolingClient;

    const firstNode = activity("first");
    const stableWorkflowState: WorkflowDefinitionState = { inputs: [], variables: [], rootActivity: firstNode };
    renderPanel(firstNode, tooling, "workflow-editor-1", stableWorkflowState);
    container.querySelector<HTMLInputElement>("[aria-label='Expression source']")!.focus();
    await vi.advanceTimersByTimeAsync(180);
    await vi.waitFor(() => expect(tooling.getAuthoringContext).toHaveBeenCalledOnce());
    await vi.waitFor(() => expect(editorOutput("validation-state")).toBe("incompatible"));

    renderPanel(firstNode, tooling, "workflow-editor-2", stableWorkflowState);
    await vi.advanceTimersByTimeAsync(0);
    expect(editorOutput("authoring-state")).toBe("none");
    expect(editorOutput("authoring-data")).toBe("none");
    expect(editorOutput("validation-state")).toBe("none");
    expect(editorOutput("validation-data")).toBe("none");

    renderPanel(activity("second"), tooling, "workflow-editor-2");
    await vi.advanceTimersByTimeAsync(180);
    await vi.waitFor(() => expect(tooling.getAuthoringContext).toHaveBeenCalledTimes(2));
    expect(pendingContexts).toHaveLength(1);

    flushSync(() => window.dispatchEvent(new Event("elsa:auth-session-ended")));
    expect(editorOutput("authoring-state")).toBe("none");
    expect(editorOutput("validation-state")).toBe("none");
    expect(pendingContexts[0]!.signal?.aborted).toBe(true);

    pendingContexts[0]!.resolve(readyAuthoringContext());
    await Promise.resolve();
    flushSync(() => {});
    expect(editorOutput("authoring-state")).toBe("none");
    expect(editorOutput("authoring-data")).toBe("none");
    expect(editorOutput("validation-state")).toBe("none");
    expect(tooling.validate).toHaveBeenCalledOnce();
  });

  it("confirms a restored editor scope only after an injected client returns a fresh authorized context", async () => {
    const restored = vi.fn();
    window.addEventListener("elsa:expression-tooling-authorization-restored", restored);
    const tooling = {
      getAuthoringContext: vi.fn(async () => ({
        state: "ready",
        contractVersion: 1,
        expressionType: "JavaScript",
        contextVersion: "context-1",
        data: { version: "context-1", workflowInputs: [], visibleVariables: [], visibleActivityOutputs: [] }
      })),
      validate: vi.fn(async () => validation(0, "authorized"))
    } as unknown as StudioExpressionToolingClient;
    const node = activity("return secret;");

    flushSync(() => root.render(<ActivityPropertiesPanel
      draftId="draft-1"
      expressionEditorSessionScope="workflow-editor-injected"
      expressionTooling={tooling}
      workflowState={{ inputs: [], variables: [], rootActivity: node }}
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
    container.querySelector<HTMLInputElement>("[aria-label='Expression source']")!.focus();
    await vi.advanceTimersByTimeAsync(180);
    await vi.waitFor(() => expect(tooling.getAuthoringContext).toHaveBeenCalledOnce());

    flushSync(() => {
      window.dispatchEvent(new Event("elsa:auth-session-ended"));
      window.dispatchEvent(new Event("elsa:auth-session-started"));
    });
    expect(restored).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(180);
    await vi.waitFor(() => expect(restored).toHaveBeenCalledOnce());
    expect((restored.mock.calls[0]?.[0] as CustomEvent).detail).toEqual({
      scope: "workflow-editor-injected"
    });
    window.removeEventListener("elsa:expression-tooling-authorization-restored", restored);
  });
});
