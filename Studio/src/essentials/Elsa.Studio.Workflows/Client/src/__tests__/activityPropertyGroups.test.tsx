import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, onTestFinished, vi } from "vitest";
import type {
  StudioActivityDescriptor,
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioExpressionAuthoringContext,
  StudioExpressionDocument,
  StudioExpressionDescriptor,
  StudioExpressionEditorContribution,
  StudioExpressionToolingClient,
  StudioExpressionToolingResult,
  StudioExpressionValidationResult
} from "@elsa-workflows/studio-sdk";
import { builtInPropertyEditors } from "../../../../../apps/Elsa.Studio.Web/Client/src/app/propertyEditors";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { createObjectExpressionEditorContribution } from "../objectExpressionEditor";
import type { ActivityNode, WorkflowDefinitionState } from "../workflowTypes";

let active: { root: Root; container: HTMLElement } | null = null;
// Replaces the rendered activity from outside the panel, as an undo or selecting another activity does.
const noRenderedPanel = () => {
  throw new Error("No panel is rendered.");
};
let replaceActivity: (next: ActivityNode) => void = noRenderedPanel;
let replaceDraftId: (next: string) => void = noRenderedPanel;
let replaceEditors: (next: StudioActivityPropertyEditorContribution[]) => void = noRenderedPanel;

const backendExpressionDescriptors: StudioExpressionDescriptor[] = [
  { type: "Literal", displayName: "Literal", editingMode: "literal" },
  { type: "JavaScript", displayName: "JavaScript", editingMode: "text" },
  { type: "Liquid", displayName: "Liquid", editingMode: "text" },
  { type: "Object", displayName: "Object", editingMode: "structured" },
  { type: "Variable", displayName: "Variable", editingMode: "reference" },
  { type: "Input", displayName: "Input", editingMode: "reference" }
];

function unmountActivePanel() {
  if (!active) return;
  const mounted = active;
  flushSync(() => mounted.root.unmount());
  mounted.container.remove();
  active = null;
}

afterEach(() => {
  replaceActivity = noRenderedPanel;
  replaceDraftId = noRenderedPanel;
  replaceEditors = noRenderedPanel;
  unmountActivePanel();
});

function renderPanel(
  inputs: StudioActivityInputDescriptor[],
  options: {
    customProperties?: Record<string, unknown>;
    activity?: ActivityNode;
    editors?: StudioActivityPropertyEditorContribution[];
    expressionEditors?: StudioExpressionEditorContribution[];
    expressionTooling?: StudioExpressionToolingClient;
    workflowState?: WorkflowDefinitionState;
    expressionDescriptors?: StudioExpressionDescriptor[];
    expressionDescriptorStatus?: "loading" | "ready" | "failed";
    draftId?: string;
    strictMode?: boolean;
    onRetryDescriptors?(): void;
    onChange?(activity: ActivityNode): void;
    updateActivity?: boolean;
  } = {}
) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const descriptor: StudioActivityDescriptor = {
    typeName: "TestActivity",
    inputs,
    outputs: [],
    ports: [],
    customProperties: options.customProperties
  };
  const initialActivity = options.activity ?? activity();

  function Harness() {
    const [currentActivity, setCurrentActivity] = React.useState(initialActivity);
    const [currentDraftId, setCurrentDraftId] = React.useState(options.draftId);
    const [currentEditors, setCurrentEditors] = React.useState(options.editors ?? []);
    replaceActivity = setCurrentActivity;
    replaceDraftId = setCurrentDraftId;
    replaceEditors = setCurrentEditors;
    const handleChange = (nextActivity: ActivityNode) => {
      options.onChange?.(nextActivity);
      if (options.updateActivity !== false) setCurrentActivity(nextActivity);
    };
    const panel = (
      <ActivityPropertiesPanel
        draftId={currentDraftId}
        activity={currentActivity}
        descriptor={descriptor}
        editors={currentEditors}
        expressionEditors={options.expressionEditors ?? []}
        expressionTooling={options.expressionTooling}
        workflowState={options.workflowState}
        expressionDescriptors={options.expressionDescriptors ?? backendExpressionDescriptors}
        expressionDescriptorStatus={options.expressionDescriptorStatus ?? "ready"}
        onRetryDescriptors={options.onRetryDescriptors}
        descriptorStatus="ready"
        visibleVariables={[]}
        scopeStatus="ready"
        onChange={handleChange}
      />
    );
    return options.strictMode ? <React.StrictMode>{panel}</React.StrictMode> : panel;
  }

  flushSync(() => root.render(<Harness />));
  active = { root, container };
  return container;
}

function activity(overrides: Record<string, unknown> = {}): ActivityNode {
  return {
    nodeId: "node-1",
    activityVersionId: "activity-1",
    inputs: [],
    outputs: [],
    ...overrides
  };
}

function input(name: string, overrides: Partial<StudioActivityInputDescriptor> = {}): StudioActivityInputDescriptor {
  return { name, displayName: name, typeName: "System.String", isWrapped: false, ...overrides };
}

function propertyLabels(container: HTMLElement) {
  return [...container.querySelectorAll(".wf-property-row-header label")].map(label => label.textContent);
}

describe("activity property organization", () => {
  it("does not invent expression types when the backend returns none", () => {
    const container = renderPanel([
      input("Message", { isWrapped: true, defaultSyntax: "Literal" })
    ], {
      expressionDescriptors: [],
      activity: activity({
        message: { typeName: "System.String", expression: { type: "Literal", value: "hello" } }
      })
    });

    expect(container.textContent).toContain("No expression types are available.");
    expect(container.textContent).toContain("No editor is available for Literal");
    expect(container.textContent).not.toContain("JavaScript");
    expect(container.textContent).not.toContain("Liquid");
  });

  it("reports loading and failed expression metadata without replacing a preserved backend snapshot", () => {
    const retry = vi.fn();
    const loading = renderPanel([input("Message", { isWrapped: true })], {
      expressionDescriptorStatus: "loading",
      expressionDescriptors: []
    });
    expect(loading.textContent).toContain("Loading expression types...");

    unmountActivePanel();

    const failed = renderPanel([input("Message", { isWrapped: true })], {
      expressionDescriptorStatus: "failed",
      expressionDescriptors: [{ type: "Literal", displayName: "Literal", editingMode: "literal" }],
      onRetryDescriptors: retry
    });
    expect(failed.textContent).toContain("Expression types could not be refreshed. Using the last loaded metadata.");
    flushSync(() => failed.querySelector<HTMLButtonElement>("button.wf-expression-descriptors-retry")!.click());
    expect(retry).toHaveBeenCalledOnce();
  });

  it("orders properties by input order, keeping delivery order for ties (never alphabetical)", () => {
    const container = renderPanel([
      input("Last", { order: 20 }),
      input("Beta", { order: 10 }),
      input("Alpha", { order: 10 })
    ]);

    // Ties keep catalog delivery order (Beta before Alpha), not an alphabetical re-sort (issue #452).
    expect(propertyLabels(container)).toEqual(["Beta", "Alpha", "Last"]);
  });

  it("preserves catalog delivery order when the backend supplies no explicit order (issue #452)", () => {
    const container = renderPanel([
      input("Url"),
      input("Content"),
      input("Method")
    ]);

    // The primary field (Url) is delivered first and must stay first — alphabetical sorting pushed it last.
    expect(propertyLabels(container)).toEqual(["Url", "Content", "Method"]);
  });

  it("uses descriptor metadata to order and label category groups", () => {
    const container = renderPanel([
      input("RetryPolicy", { category: "Advanced" }),
      input("Path", { category: "Simple" })
    ], {
      customProperties: {
        propertyGroups: [
          { category: " ", name: "Advanced", label: " ", displayName: "Advanced settings", order: 20 },
          { category: "Simple", label: "Essentials", order: 10 }
        ]
      }
    });

    expect([...container.querySelectorAll(".wf-property-group h4")].map(heading => heading.textContent))
      .toEqual(["Essentials", "Advanced settings"]);
    expect(propertyLabels(container)).toEqual(["Path", "RetryPolicy"]);
  });

  it("orders unconfigured groups by their first property order", () => {
    const container = renderPanel([
      input("RequestSizeLimit", { category: "Advanced", order: 110 }),
      input("Path", { category: "Simple", order: 10 }),
      input("Policy", { category: "Advanced", order: 100 })
    ]);

    expect([...container.querySelectorAll(".wf-property-group h4")].map(heading => heading.textContent))
      .toEqual(["Simple", "Advanced"]);
    expect(propertyLabels(container)).toEqual(["Path", "Policy", "RequestSizeLimit"]);
  });

  it("keeps unconfigured and uncategorized properties visible in deterministic fallback groups", () => {
    const container = renderPanel([
      input("ZebraValue", { category: "Zebra", order: 200 }),
      input("GeneralValue", { category: null, order: 10 }),
      input("AdvancedValue", { category: "Advanced" })
    ], {
      customProperties: {
        propertyGroups: [{ category: "Advanced", label: "Advanced settings", order: 100 }]
      }
    });

    expect([...container.querySelectorAll(".wf-property-group h4")].map(heading => heading.textContent))
      .toEqual(["General", "Advanced settings", "Zebra"]);
    expect(propertyLabels(container)).toEqual(["GeneralValue", "AdvancedValue", "ZebraValue"]);
  });

  it("shows primary HTTP request inputs directly and keeps one More options section mounted", () => {
    const onChange = vi.fn();
    const container = renderPanel([
      input("Url", { tier: "essential", isRequired: true }),
      input("Method", { tier: "essential", defaultValue: "GET" }),
      input("Content", { tier: "essential" }),
      input("Connection", { tier: "essential", isRequired: false }),
      input("ContentType", { tier: "optional", defaultValue: "text/plain" }),
      input("RequestHeaders", { tier: "optional" }),
      input("ExpectedStatusCodes", { tier: "optional" }),
      input("Timeout", { tier: "optional" })
    ], { onChange });

    const advanced = container.querySelector<HTMLElement>(".studio-advanced-section");
    expect(advanced).not.toBeNull();
    expect(advanced!.querySelector(".studio-disclosure-fold-label")?.textContent).toBe("More options");
    expect(advanced!.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("false");
    expect([...container.querySelectorAll(".wf-properties > .wf-property-group .wf-property-row-header label")]
      .map(label => label.textContent)).toEqual(["Url", "Method", "Content", "Connection"]);
    expect([...advanced!.querySelectorAll(".wf-property-row-header label")]
      .map(label => label.textContent)).toEqual(["ContentType", "RequestHeaders", "ExpectedStatusCodes", "Timeout"]);
    expect(advanced!.querySelectorAll(".wf-property-row")).toHaveLength(4);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("keeps legacy, unknown, setting, case-mismatched, and required optional inputs directly visible", () => {
    const container = renderPanel([
      input("Legacy"),
      input("NullTier", { tier: null }),
      input("UnknownTier", { tier: "experimental" }),
      input("SettingTier", { tier: "setting" }),
      input("CaseMismatch", { tier: "Optional" }),
      input("RequiredOptional", { tier: "optional", isRequired: true }),
      input("Optional", { tier: "optional", isRequired: false })
    ]);

    const advanced = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    expect([...container.querySelectorAll(".wf-properties > .wf-property-group .wf-property-row-header label")]
      .map(label => label.textContent)).toEqual([
      "Legacy", "NullTier", "UnknownTier", "SettingTier", "CaseMismatch", "RequiredOptional"
    ]);
    expect([...advanced.querySelectorAll(".wf-property-row-header label")].map(label => label.textContent))
      .toEqual(["Optional"]);
  });

  it("keeps optional fields visible without an error badge when an editor cannot report private errors", async () => {
    const unknownEditor: StudioActivityPropertyEditorContribution = {
      id: "test.unreported-editor",
      supports: descriptor => descriptor.name === "UnknownEditor",
      component: ({ descriptor, value, onChange }) => (
        <input aria-label={descriptor.displayName ?? descriptor.name} value={String(value ?? "")} onChange={event => onChange(event.target.value)} />
      )
    };
    const container = renderPanel([input("UnknownEditor", { tier: "optional" })], { editors: [unknownEditor] });

    await nextFrame();
    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    expect(disclosure.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("true");
    expect(disclosure.querySelector(".studio-status-pill")).toBeNull();
  });

  it("keeps the actual mounted option editor visible when dynamic options change editor selection", async () => {
    const capableFallback: StudioActivityPropertyEditorContribution = {
      id: "test.capable-option-fallback",
      supportsValidationReporting: true,
      supports: (descriptor, context) => descriptor.name === "DynamicEditor" &&
        descriptor.uiSpecifications?.options === undefined && context.readOnly !== true,
      component: ({ descriptor }) => <input aria-label={descriptor.name} value="capable fallback" readOnly />
    };
    const unreportedOptionEditor: StudioActivityPropertyEditorContribution = {
      id: "test.unreported-option-editor",
      supports: (descriptor, context) => descriptor.name === "DynamicEditor" &&
        Array.isArray(descriptor.uiSpecifications?.options) && context.readOnly === true,
      component: ({ descriptor }) => <input aria-label={descriptor.name} value="unreported actual editor" readOnly />
    };
    const container = renderPanel([input("DynamicEditor", {
      tier: "optional",
      uiSpecifications: { optionsProvider: { key: "catalog.dynamic-editor", dependsOn: [] } }
    })], { editors: [capableFallback, unreportedOptionEditor] });

    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    await nextFrame();
    expect(disclosure.querySelector<HTMLInputElement>("input[aria-label='DynamicEditor']")?.value).toBe("unreported actual editor");
    expect(disclosure.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("true");
    expect(disclosure.querySelector(".studio-status-pill")).toBeNull();
  });

  it("keeps the row and editor draft mounted when validation-reporting capability changes", async () => {
    const DraftEditor: StudioActivityPropertyEditorContribution["component"] = ({ descriptor }) => {
      const [draft, setDraft] = React.useState("initial draft");
      return <input aria-label={descriptor.name} value={draft} onChange={event => setDraft(event.target.value)} />;
    };
    const reportingEditor: StudioActivityPropertyEditorContribution = {
      id: "test.capability-change",
      supportsValidationReporting: true,
      supports: descriptor => descriptor.name === "OptionalDraft",
      component: DraftEditor
    };
    const container = renderPanel([input("OptionalDraft", { tier: "optional" })], { editors: [reportingEditor] });
    const row = container.querySelector<HTMLElement>(".wf-property-row")!;
    const editorInput = container.querySelector<HTMLInputElement>("input[aria-label='OptionalDraft']")!;
    changeTextInput(editorInput, "unfinished private draft");

    const { supportsValidationReporting: _supportsValidationReporting, ...unreportedEditor } = reportingEditor;
    flushSync(() => replaceEditors([unreportedEditor]));

    await nextFrame();
    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    expect(container.querySelector(".wf-property-row")).toBe(row);
    expect(disclosure.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("true");
    expect(container.querySelector<HTMLInputElement>("input[aria-label='OptionalDraft']")?.value).toBe("unfinished private draft");
    expect(disclosure.querySelector(".studio-status-pill")).toBeNull();
  });

  it("keeps a selected inline expression editor directly visible without assuming diagnostics are complete", async () => {
    const expressionEditor: StudioExpressionEditorContribution = {
      id: "test.unreported-expression-editor",
      supports: context => context.syntax === "Liquid",
      surfaces: { inline: EchoExpressionEditor }
    };
    const container = renderPanel([input("OptionalExpression", {
      isWrapped: true,
      defaultSyntax: "Liquid",
      tier: "optional"
    })], { expressionEditors: [expressionEditor] });

    await nextFrame();
    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    expect(disclosure.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("true");
    expect(disclosure.querySelector(".studio-status-pill")).toBeNull();
  });

  it("distinguishes an authored default from a synthesized default and recognizes opaque top-level and wire keys", () => {
    const authored = {
      typeName: "System.String",
      expression: { type: "Literal", value: "text/plain" },
      conversion: { mode: "force" },
      argumentExtras: { future: { keep: true } }
    };
    const descriptor = input("ContentType", {
      referenceKey: "http-content-type",
      isWrapped: true,
      defaultSyntax: "Literal",
      defaultValue: "text/plain",
      tier: "optional"
    });
    const authoredContainer = renderPanel([descriptor], {
      activity: activity({ "http-content-type": authored })
    });
    const authoredDisclosure = authoredContainer.querySelector<HTMLElement>(".studio-advanced-section")!;
    expect(authoredDisclosure.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("true");
    expect(authoredDisclosure.querySelector(".studio-disclosure-fold-body")?.hasAttribute("hidden")).toBe(false);

    unmountActivePanel();

    const synthesizedContainer = renderPanel([descriptor]);
    const synthesizedDisclosure = synthesizedContainer.querySelector<HTMLElement>(".studio-advanced-section")!;
    expect(synthesizedDisclosure.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("false");

    unmountActivePanel();

    const wireContainer = renderPanel([input("RequestHeaders", {
      referenceKey: "opaque-header-key",
      isWrapped: true,
      tier: "optional"
    })], {
      activity: activity({ inputs: [{
        referenceKey: "opaque-header-key",
        value: { expressionType: "Literal", value: { Accept: "application/json" } },
        conversion: { mode: "force" },
        futureArgumentField: "keep"
      }] })
    });
    const wireDisclosure = wireContainer.querySelector<HTMLElement>(".studio-advanced-section")!;
    expect(wireDisclosure.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("true");
  });

  it("opens optional fields for host errors, but not warnings, and drops a stale activity error", async () => {
    const editor: StudioExpressionEditorContribution = {
      id: "test.disclosure-diagnostics",
      supports: context => context.syntax === "Literal",
      surfaces: {},
      diagnostics: context => (context.activity as ActivityNode).nodeId === "node-1"
        ? [{ severity: "error", message: "Host-owned validation error" }]
        : [{ severity: "warning", message: "Host-owned warning" }]
    };
    const container = renderPanel([input("Timeout", {
      isWrapped: true,
      defaultSyntax: "Literal",
      tier: "optional"
    })], { expressionEditors: [editor] });
    await nextFrame();

    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    expect(container.textContent).toContain("Host-owned validation error");
    expect(disclosure.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("true");

    flushSync(() => replaceActivity(activity({ nodeId: "node-2" })));
    await nextFrame();
    expect(disclosure.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("false");
    expect(container.textContent).toContain("Host-owned warning");
  });

  it("opens the Error badge for current semantic validation and clears it when source changes", async () => {
    const validate = vi.fn(async (document: StudioExpressionDocument): Promise<StudioExpressionToolingResult<StudioExpressionValidationResult>> => {
      const diagnostics = document.source === "invalid"
        ? [{
          severity: "error" as const,
          message: "Current semantic error",
          documentId: document.id,
          sourceVersion: document.sourceVersion,
          contextVersion: "context-current"
        }]
        : [];
      return {
        state: diagnostics.length ? "ready" : "supported-empty",
        contractVersion: 1,
        expressionType: "JavaScript",
        contextVersion: "context-current",
        data: {
          documentId: document.id,
          sourceVersion: document.sourceVersion,
          contextVersion: "context-current",
          diagnostics
        }
      };
    });
    const expressionTooling = {
      getAuthoringContext: async (): Promise<StudioExpressionToolingResult<StudioExpressionAuthoringContext>> => ({
        state: "ready",
        contractVersion: 1,
        expressionType: "JavaScript",
        contextVersion: "context-current",
        data: { version: "context-current", workflowInputs: [], visibleVariables: [], visibleActivityOutputs: [] }
      }),
      validate
    } as unknown as StudioExpressionToolingClient;
    const workflowState = {} as WorkflowDefinitionState;
    const expressionEditor: StudioExpressionEditorContribution = {
      id: "test.semantic-validation-expression",
      supports: context => context.syntax === "JavaScript",
      surfaces: {
        inline: ({ context, value, onChange }) => (
          <>
            <input
              aria-label="Semantic source"
              value={String(value ?? "")}
              onFocus={context.onFocus}
              onBlur={context.onBlur}
              onChange={event => onChange(event.target.value)}
            />
          <output data-testid="semantic-validation-state">{context.validation?.state ?? "none"}</output>
          <output data-testid="semantic-validation-error">{context.validation?.data?.diagnostics.find(diagnostic => diagnostic.severity === "error")?.message ?? "none"}</output>
          </>
        )
      }
    };
    const container = renderPanel([input("OptionalSemantic", {
      tier: "optional",
      isWrapped: true,
      defaultSyntax: "JavaScript"
    })], {
      activity: activity({ optionalSemantic: {
        typeName: "System.String",
        expression: { type: "JavaScript", value: "invalid" }
      } }),
      expressionTooling,
      workflowState,
      expressionEditors: [expressionEditor]
    });
    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    const source = container.querySelector<HTMLInputElement>("input[aria-label='Semantic source']")!;
    flushSync(() => source.focus());
    await vi.waitFor(() => expect(container.querySelector("[data-testid='semantic-validation-error']")?.textContent).toBe("Current semantic error"));
    expect(validate).toHaveBeenCalled();
    expect(validate.mock.calls[0]?.[0].source).toBe("invalid");
    expect(disclosure.querySelector(".studio-status-pill")?.getAttribute("data-tone")).toBe("danger");

    changeTextInput(source, "valid");
    flushSync(() => source.blur());
    await vi.waitFor(() => {
      expect(validate.mock.calls.at(-1)?.[0].source).toBe("valid");
      expect(disclosure.querySelector(".studio-status-pill")?.getAttribute("data-tone")).toBe("accent");
    });
  });

  it("keeps a private TimeSpan validation error discoverable and clears it after recovery", async () => {
    const container = renderPanel([input("Timeout", {
      typeName: "System.TimeSpan",
      defaultValue: "00:00:30",
      tier: "optional"
    })], { editors: builtInPropertyEditors });
    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    const trigger = disclosure.querySelector<HTMLButtonElement>(".studio-disclosure-fold-trigger")!;
    expect(trigger.getAttribute("aria-expanded")).toBe("false");

    flushSync(() => trigger.click());
    const timeout = container.querySelector<HTMLInputElement>("input[aria-label='Timeout']")!;
    changeTextInput(timeout, "not a duration");
    await nextFrame();
    expect(timeout.getAttribute("aria-invalid")).toBe("true");

    flushSync(() => trigger.click());
    await nextFrame();
    expect(trigger.getAttribute("aria-expanded")).toBe("true");

    changeTextInput(timeout, "00:01:15");
    await nextFrame();
    expect(timeout.hasAttribute("aria-invalid")).toBe(false);
    expect(disclosure.querySelector(".studio-status-pill")?.getAttribute("data-tone")).toBe("accent");
  });

  it("reports an initial private validation error under StrictMode", async () => {
    const initialErrorEditor: StudioActivityPropertyEditorContribution = {
      id: "test.strict-mode-initial-error",
      supportsValidationReporting: true,
      supports: descriptor => descriptor.name === "StrictError",
      component: function InitialErrorEditor({ descriptor, onValidationChange }) {
        React.useEffect(() => {
          onValidationChange?.(true);
        }, [onValidationChange]);
        return <input aria-label={descriptor.name} />;
      }
    };
    const container = renderPanel([input("StrictError", { tier: "optional" })], {
      editors: [initialErrorEditor],
      strictMode: true
    });
    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    await nextFrame();

    expect(disclosure.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("true");
    expect(disclosure.querySelector(".studio-status-pill")?.getAttribute("data-tone")).toBe("danger");
  });

  it("ignores stale private editor callbacks after activity, draft, and editor scope changes", async () => {
    const staleCallbacks: Array<(hasError: boolean) => void> = [];
    const reportingEditor = (id: string): StudioActivityPropertyEditorContribution => ({
      id,
      supportsValidationReporting: true,
      supports: descriptor => descriptor.name === "ReportedOptional",
      component: ({ onValidationChange }) => {
        if (onValidationChange) staleCallbacks.push(onValidationChange);
        return <button type="button" onClick={() => onValidationChange?.(true)}>Report local error</button>;
      }
    });
    const container = renderPanel([input("ReportedOptional", { tier: "optional" })], {
      editors: [reportingEditor("test.reporting-editor.v1")],
      draftId: "draft-1"
    });
    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    const reportError = () => {
      if (disclosure.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded") !== "true") {
        flushSync(() => disclosure.querySelector<HTMLButtonElement>(".studio-disclosure-fold-trigger")!.click());
      }
      const button = [...container.querySelectorAll<HTMLButtonElement>("button")]
        .find(candidate => candidate.textContent?.includes("Report local error"))!;
      flushSync(() => button.click());
    };
    const hasError = () => disclosure.querySelector(".studio-status-pill")?.getAttribute("data-tone") === "danger";

    reportError();
    await nextFrame();
    expect(hasError()).toBe(true);
    const beforeActivityChange = staleCallbacks.at(-1)!;

    flushSync(() => replaceActivity(activity({ nodeId: "node-2" })));
    await nextFrame();
    expect(hasError()).toBe(false);
    flushSync(() => beforeActivityChange(true));
    expect(hasError()).toBe(false);

    reportError();
    await nextFrame();
    expect(hasError()).toBe(true);
    const beforeRevisionChange = staleCallbacks.at(-1)!;

    flushSync(() => replaceActivity(activity({ nodeId: "node-2", activityVersionId: "activity-v2" })));
    await nextFrame();
    expect(hasError()).toBe(false);
    flushSync(() => beforeRevisionChange(true));
    expect(hasError()).toBe(false);

    reportError();
    await nextFrame();
    expect(hasError()).toBe(true);
    const beforeDraftChange = staleCallbacks.at(-1)!;

    flushSync(() => replaceDraftId("draft-2"));
    await nextFrame();
    expect(hasError()).toBe(false);
    flushSync(() => beforeDraftChange(true));
    expect(hasError()).toBe(false);

    reportError();
    await nextFrame();
    expect(hasError()).toBe(true);
    const beforeEditorChange = staleCallbacks.at(-1)!;

    flushSync(() => replaceEditors([{
      id: "test.reporting-editor.v2",
      supportsValidationReporting: true,
      supports: descriptor => descriptor.name === "ReportedOptional",
      component: () => <input aria-label="ReportedOptional" />
    }]));
    await nextFrame();
    expect(hasError()).toBe(false);
    flushSync(() => beforeEditorChange(true));
    expect(hasError()).toBe(false);
  });

  it("aggregates numeric collection editor errors by mounted item and releases removed items", async () => {
    const statusCodes = {
      typeName: "System.Int32",
      expression: { type: "Literal", value: [200, 204] }
    };
    const container = renderPanel([input("ExpectedStatusCodes", {
      typeName: "System.Int32",
      collectionKind: "List",
      isWrapped: true,
      tier: "optional"
    })], {
      editors: builtInPropertyEditors,
      activity: activity({ expectedStatusCodes: statusCodes })
    });
    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    const tone = () => disclosure.querySelector(".studio-status-pill")?.getAttribute("data-tone");
    const entries = () => [...container.querySelectorAll<HTMLInputElement>(".wf-collection-editor input[type='text']")];

    changeTextInput(entries()[0]!, "invalid first");
    changeTextInput(entries()[1]!, "invalid second");
    await nextFrame();
    expect(tone()).toBe("danger");

    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Remove ExpectedStatusCodes item 1']")!.click());
    await nextFrame();
    expect(tone()).toBe("danger");
    expect(entries()).toHaveLength(1);

    changeTextInput(entries()[0]!, "205");
    await nextFrame();
    expect(tone()).toBe("accent");
    expect(entries()[0]!.hasAttribute("aria-invalid")).toBe(false);

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-collection-add")!.click());
    changeTextInput(entries()[0]!, "invalid before reorder");
    await nextFrame();
    expect(tone()).toBe("danger");
    flushSync(() => clickButton(container, "Move ExpectedStatusCodes item 1 down"));
    await nextFrame();
    expect(entries()[1]!.value).toBe("invalid before reorder");
    expect(tone()).toBe("danger");

    flushSync(() => clickButton(container, "Remove ExpectedStatusCodes item 2"));
    await nextFrame();
    expect(entries()).toHaveLength(1);
    expect(tone()).toBe("accent");
  });

  it("aggregates dictionary value errors independently and clears a removed dictionary row", async () => {
    const statusCodes = {
      typeName: "System.Int32",
      expression: { type: "Literal", value: { first: 200, second: 204 } }
    };
    const container = renderPanel([input("ExpectedStatusCodeMap", {
      typeName: "System.Int32",
      collectionKind: "Dictionary",
      isWrapped: true,
      tier: "optional"
    })], {
      editors: builtInPropertyEditors,
      activity: activity({ expectedStatusCodeMap: statusCodes })
    });
    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    const tone = () => disclosure.querySelector(".studio-status-pill")?.getAttribute("data-tone");
    const entries = () => [...container.querySelectorAll<HTMLInputElement>(".wf-dictionary-cell.value input[type='text']")];

    changeTextInput(entries()[0]!, "invalid first");
    changeTextInput(entries()[1]!, "invalid second");
    await nextFrame();
    expect(tone()).toBe("danger");

    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Remove first']")!.click());
    await nextFrame();
    expect(tone()).toBe("danger");
    expect(entries()).toHaveLength(1);

    changeTextInput(entries()[0]!, "205");
    await nextFrame();
    expect(tone()).toBe("accent");
    expect(entries()[0]!.hasAttribute("aria-invalid")).toBe(false);
  });

  it("keeps a new dictionary's blank-key error visible after opening and closing its expanded editor", async () => {
    const container = renderPanel([input("Headers", {
      typeName: "System.String",
      collectionKind: "Dictionary",
      isWrapped: true,
      tier: "optional"
    })]);
    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-dictionary-actions button[data-dictionary-add]")!.click());
    await nextFrame();

    const trigger = disclosure.querySelector<HTMLButtonElement>(".studio-disclosure-fold-trigger")!;
    expect(container.textContent).toContain("Key cannot be blank.");
    expect(trigger.getAttribute("aria-expanded")).toBe("true");
    expect(disclosure.querySelector(".studio-status-pill")?.getAttribute("data-tone")).toBe("danger");

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-dictionary-open-expanded")!.click());
    await nextFrame();
    expect(container.querySelector("button[aria-label='Close Headers editor']")).not.toBeNull();

    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Close Headers editor']")!.click());
    await nextFrame();
    expect(trigger.getAttribute("aria-expanded")).toBe("true");
    expect(disclosure.querySelector(".studio-status-pill")?.getAttribute("data-tone")).toBe("danger");
    expect(container.querySelector<HTMLInputElement>(".wf-dictionary-cell.key input")?.getAttribute("aria-invalid")).toBe("true");
  });

  it("does not let a valid simultaneous dictionary editor instance clear its sibling error", async () => {
    const callbacks = new Map<string, (hasError: boolean) => void>();
    const ReportingValueEditor: StudioActivityPropertyEditorContribution["component"] = ({ descriptor, onValidationChange }) => {
      const instanceId = React.useId();
      React.useEffect(() => {
        if (!onValidationChange) return;
        callbacks.set(instanceId, onValidationChange);
        return () => { callbacks.delete(instanceId); };
      }, [instanceId, onValidationChange]);
      return <input aria-label={descriptor.name} data-validation-instance={instanceId} />;
    };
    const reportingEditor: StudioActivityPropertyEditorContribution = {
      id: "test.reporting-dictionary-value",
      supportsValidationReporting: true,
      supports: (descriptor, context) => descriptor.name === "Headers.Value" && context.scope === "element",
      component: ReportingValueEditor
    };
    const container = renderPanel([input("Headers", {
      typeName: "System.String",
      collectionKind: "Dictionary",
      isWrapped: true,
      tier: "optional"
    })], {
      editors: [reportingEditor],
      activity: activity({ headers: {
        typeName: "System.String",
        expression: { type: "Literal", value: { Accept: "application/json" } }
      } })
    });
    const disclosure = container.querySelector<HTMLElement>(".studio-advanced-section")!;
    await nextFrame();
    expect(callbacks.size).toBe(1);

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-dictionary-open-expanded")!.click());
    await nextFrame();
    expect(callbacks.size).toBe(2);
    expect(container.querySelectorAll(".wf-dictionary-editor input[data-validation-instance]")).toHaveLength(2);

    const [first, second] = [...callbacks.values()];
    first!(true);
    await nextFrame();
    expect(disclosure.querySelector(".studio-status-pill")?.getAttribute("data-tone")).toBe("danger");

    second!(false);
    await nextFrame();
    expect(disclosure.querySelector(".studio-status-pill")?.getAttribute("data-tone")).toBe("danger");
  });

  it("changes HTTP method without clearing body, authored defaults, conversions, or unknown extras", () => {
    const contentType = {
      typeName: "System.String",
      expression: { type: "Literal", value: "text/plain" },
      conversion: { mode: "profile", profile: { id: "body-conversion", version: "1" } },
      argumentExtras: { unknown: { preserve: true } }
    };
    const headers = {
      typeName: "System.String",
      expression: { type: "JavaScript", value: "headers()" },
      argumentExtras: { source: "foreign-template" }
    };
    const changes: ActivityNode[] = [];
    const initial = activity({
      method: "GET",
      content: { typeName: "System.String", expression: { type: "Literal", value: "request body" } },
      contentType,
      requestHeaders: headers,
      unrelatedExtra: { preserve: true }
    });
    const container = renderPanel([
      input("Method", { isWrapped: false, tier: "essential", defaultValue: "GET" }),
      input("Content", { isWrapped: true, tier: "essential" }),
      input("ContentType", { isWrapped: true, tier: "optional", defaultValue: "text/plain" }),
      input("RequestHeaders", { isWrapped: true, tier: "optional" })
    ], { activity: initial, onChange: next => changes.push(next) });

    const methodRow = [...container.querySelectorAll<HTMLElement>(".wf-property-row")]
      .find(row => row.querySelector(".wf-property-row-header label")?.textContent === "Method")!;
    const methodInput = methodRow.querySelector<HTMLInputElement>("input")!;
    for (const method of ["HEAD", "POST", "GET"]) {
      changeTextInput(methodInput, method);
      expect(changes.at(-1)).toEqual({ ...initial, method });
    }

    expect(changes.at(-1)?.content).toEqual(initial.content);
    expect(changes.at(-1)?.contentType).toEqual(contentType);
    expect(changes.at(-1)?.requestHeaders).toEqual(headers);
    expect(changes.at(-1)?.unrelatedExtra).toEqual({ preserve: true });
    expect(container.querySelector(".studio-advanced-section .wf-property-row")).not.toBeNull();
  });

  it("places the syntax picker inline with the checkbox contribution", () => {
    const checkboxEditor: StudioActivityPropertyEditorContribution = {
      id: "studio.property.checkbox",
      supports: descriptor => descriptor.typeName === "System.Boolean",
      component: ({ value, disabled, onChange }) => (
        <input
          type="checkbox"
          checked={value === true}
          disabled={disabled}
          onChange={event => onChange(event.target.checked)}
        />
      )
    };
    const container = renderPanel([
      input("CanStartWorkflow", { typeName: "System.Boolean", isWrapped: true, defaultSyntax: "Literal" })
    ], {
      editors: [checkboxEditor],
      activity: activity({
        canStartWorkflow: { typeName: "System.Boolean", expression: { type: "Literal", value: false } }
      })
    });

    const expressionField = container.querySelector(".wf-expression-field");
    expect(expressionField?.classList.contains("wf-expression-field--toggle")).toBe(true);
    expect(expressionField?.querySelector("input[type='checkbox']")).not.toBeNull();
    expect(expressionField?.querySelector(".wf-syntax-picker.inline")).not.toBeNull();
    expect(container.querySelector(".wf-property-row > .wf-syntax-picker:not(.inline)")).toBeNull();
  });

  it("uses inline syntax chrome for URI inputs and a compact toolbar for dictionaries", () => {
    const container = renderPanel([
      input("Url", { typeName: "System.Uri", isWrapped: true }),
      input("RequestHeaders", { collectionKind: "Dictionary", isWrapped: true })
    ], {
      activity: activity({
        url: { typeName: "System.Uri", expression: { type: "Literal", value: "https://example.com" } },
        requestHeaders: { typeName: "System.String", expression: { type: "Literal", value: {} } }
      })
    });

    const rows = [...container.querySelectorAll<HTMLElement>(".wf-property-row")];
    expect(rows[0]?.querySelector(".wf-expression-field .wf-syntax-picker.inline")).not.toBeNull();
    expect(rows[0]?.querySelector(":scope > .wf-syntax-picker:not(.inline)")).toBeNull();
    expect(rows[1]?.querySelector(".wf-dictionary-expression-toolbar .wf-syntax-picker.inline")).not.toBeNull();
    expect(rows[1]?.querySelector(":scope > .wf-syntax-picker:not(.inline)")).toBeNull();
  });

  it("uses normal inline chrome for non-literal Boolean expressions", () => {
    const checkboxEditor: StudioActivityPropertyEditorContribution = {
      id: "studio.property.checkbox",
      supports: descriptor => descriptor.typeName === "System.Boolean",
      component: ({ value, disabled, onChange }) => (
        <input
          type="checkbox"
          checked={value === true}
          disabled={disabled}
          onChange={event => onChange(event.target.checked)}
        />
      )
    };
    const javaScriptEditor: StudioExpressionEditorContribution = {
      id: "javascript.inline",
      supports: context => context.syntax === "JavaScript",
      surfaces: { inline: ({ value }) => <textarea aria-label="JavaScript expression" value={String(value ?? "")} readOnly /> }
    };
    const container = renderPanel([
      input("JavaScriptValue", { typeName: "System.Boolean", isWrapped: true }),
      input("VariableValue", { typeName: "System.Boolean", isWrapped: true })
    ], {
      editors: [checkboxEditor],
      expressionEditors: [javaScriptEditor],
      activity: activity({
        javaScriptValue: { typeName: "System.Boolean", expression: { type: "JavaScript", value: "input.enabled" } },
        variableValue: { typeName: "System.Boolean", expression: { type: "Variable", value: null } }
      })
    });

    const fields = [...container.querySelectorAll(".wf-expression-field")];
    expect(fields).toHaveLength(1);
    expect(fields.every(field => !field.classList.contains("wf-expression-field--toggle"))).toBe(true);
    expect(fields[0]?.querySelector("textarea[aria-label='JavaScript expression']")).not.toBeNull();
    const referenceRow = container.querySelector(".wf-syntax-picker-trigger.compact[aria-label^='VariableValue expression syntax']")?.closest(".wf-property-row");
    expect(referenceRow?.querySelector(".wf-expression-field")).toBeNull();
    expect(referenceRow?.textContent).toContain("current value is preserved and read-only");
  });

  it("does not steal focus for pre-existing text expressions and still expands regardless of property type", () => {
    const container = renderPanel([
      input("Condition", { typeName: "System.Boolean", isWrapped: true }),
      input("Template", { isWrapped: true })
    ], {
      expressionDescriptors: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "Python", displayName: "Python", editingMode: "text" }
      ],
      activity: activity({
        condition: { typeName: "System.Boolean", expression: { type: "Python", value: "value and" } },
        template: { typeName: "System.String", expression: { type: "Python", value: "message" } }
      })
    });

    const inlineEditors = [...container.querySelectorAll<HTMLInputElement>("input[aria-label$=' expression']")];
    expect(inlineEditors.map(editor => editor.value)).toEqual(["value and", "message"]);
    expect(inlineEditors).not.toContain(document.activeElement);
    expect(container.querySelector("input[type='checkbox']")).toBeNull();

    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Open expanded Condition editor']")?.click());
    const expanded = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Condition expanded value']");
    expect(expanded?.value).toBe("value and");
    expect(document.activeElement).toBe(expanded);
  });

  it("keeps one stable expression document across compact and expanded contributed surfaces", () => {
    const contexts: Array<React.ComponentProps<
      NonNullable<StudioExpressionEditorContribution["surfaces"]["inline"]>
    >["context"]> = [];
    const contribution: StudioExpressionEditorContribution = {
      id: "javascript.rich",
      supports: context => context.syntax === "JavaScript",
      surfaces: {
        inline: props => {
          contexts.push(props.context);
          return <button type="button" aria-label="Expand from editor" onClick={props.onExpand}>Compact editor</button>;
        },
        expanded: props => {
          contexts.push(props.context);
          return <div aria-label="Expanded rich editor">{String(props.value)}</div>;
        }
      }
    };
    const container = renderPanel([input("Template", { isWrapped: true })], {
      draftId: "draft-42",
      expressionEditors: [contribution],
      activity: activity({
        template: { typeName: "System.String", expression: { type: "JavaScript", value: "input.name" } }
      })
    });

    const inlineContext = contexts.at(-1)!;
    expect(inlineContext).toMatchObject({ syntax: "JavaScript", surface: "inline" });
    expect(inlineContext.document).toMatchObject({
      draftId: "draft-42",
      activityId: "node-1",
      propertyKey: "Template",
      expressionType: "JavaScript",
      source: "input.name",
      sourceVersion: 0
    });

    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Expand from editor']")!.click());

    expect(container.querySelector("[aria-label='Expanded rich editor']")).not.toBeNull();
    expect(container.querySelector("[aria-label='Expand from editor']")).toBeNull();
    const expandedContext = contexts.at(-1)!;
    expect(expandedContext.surface).toBe("expanded");
    expect(expandedContext.document?.id).toBe(inlineContext.document?.id);
    expect(expandedContext.document?.source).toBe(inlineContext.document?.source);
  });

  it("edits only the source of a portable expression on inline and expanded surfaces", () => {
    const definition = {
      language: "JavaScript",
      source: "args.response.status",
      resultType: { alias: "String", collectionKind: "Single" },
      parameters: {
        response: {
          kind: "ActivityResult",
          isOptional: true,
          producerNodeId: "customer-lookup",
          projectionKey: "status"
        }
      },
      options: { strict: true },
      capabilityProfile: "binding-pure-v1",
      metadata: { origin: "seeded-workflow" }
    };
    const changes: ActivityNode[] = [];
    const container = renderPanel([input("Template", { isWrapped: true })], {
      onChange: next => changes.push(next),
      activity: activity({
        template: { typeName: "System.String", expression: { type: "JavaScript", value: definition } }
      })
    });

    const inline = container.querySelector<HTMLInputElement>("input[aria-label='Template expression']")!;
    expect(inline.value).toBe(definition.source);
    changeTextInput(inline, "args.response.message");
    expect((changes.at(-1)?.template as { expression: { value: unknown } }).expression.value)
      .toEqual({ ...definition, source: "args.response.message" });

    clickButton(container, "Open expanded Template editor");
    const expanded = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Template expanded value']")!;
    expect(expanded.value).toBe("args.response.message");
    changeTextArea(expanded, "args.response.code");
    expect((changes.at(-1)?.template as { expression: { value: unknown } }).expression.value)
      .toEqual({ ...definition, source: "args.response.code" });
  });

  // Regression: ticking a checklist box saved fine, but autosave promoted the structured Literal to an
  // "Object" expression on the wire and nothing reversed it, so the row reopened with the picker reading
  // "Object" and the checklist replaced by the raw JSON summary.
  it("reopens a saved checklist input as Literal with its checklist intact, not the Object JSON summary", () => {
    const checklistEditor: StudioActivityPropertyEditorContribution = {
      id: "test.multiselect",
      supports: (_descriptor, editorContext) => editorContext.scope === "collection",
      component: ({ value }) => <div className="test-checklist">{(value as unknown[]).join(",")}</div>
    };
    const container = renderPanel([
      input("SupportedMethods", { typeName: "System.String", collectionKind: "List", isWrapped: true })
    ], {
      editors: [checklistEditor],
      expressionEditors: [createObjectExpressionEditorContribution(() => [checklistEditor])],
      // Exactly what a save round-trip leaves behind: expressionType "Object", value a JSON string.
      activity: activity({
        supportedMethods: { typeName: "", expression: { type: "Object", value: '["GET"]' } }
      })
    });

    expect(container.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.title).toBe("Literal");
    expect(container.querySelector(".test-checklist")?.textContent).toBe("GET");
    expect(container.textContent).not.toContain("Open the expanded editor to edit JSON.");
  });

  it("focuses the replacement textbox when selecting a text expression mode", () => {
    const container = renderPanel([
      input("Template", { isWrapped: true })
    ], {
      activity: activity({
        template: { typeName: "System.String", expression: { type: "Literal", value: "Hello" } }
      })
    });

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
    const liquidOption = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")]
      .find(option => option.textContent === "Liquid");
    flushSync(() => liquidOption?.click());

    const replacement = container.querySelector<HTMLInputElement>("input[aria-label='Template expression']");
    expect(replacement?.value).toBe("Hello");
    expect(document.activeElement).toBe(replacement);
  });

  it("preserves a primitive Literal as visible text when switching to a text expression", () => {
    const changes: ActivityNode[] = [];
    const container = renderPanel([
      input("Count", { typeName: "System.Int32", isWrapped: true })
    ], {
      onChange: next => changes.push(next),
      activity: activity({
        count: { typeName: "System.Int32", expression: { type: "Literal", value: 42 } }
      })
    });

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
    const javaScriptOption = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")]
      .find(option => option.textContent === "JavaScript");
    flushSync(() => javaScriptOption?.click());

    expect(changes).toHaveLength(1);
    expect(changes[0]?.count).toEqual({
      typeName: "System.Int32",
      expression: { type: "JavaScript", value: "42" }
    });
  });

  it("preserves rapid edits to separate inputs while the parent draft update is pending", () => {
    const changes: ActivityNode[] = [];
    const container = renderPanel([
      input("A", { referenceKey: "a", typeName: "System.Int32" }),
      input("B", { referenceKey: "b", typeName: "System.Int32" })
    ], {
      activity: activity({ a: "", b: "" }),
      onChange: next => changes.push(next),
      updateActivity: false
    });
    const [inputA, inputB] = container.querySelectorAll<HTMLInputElement>(".wf-property-row input");

    changeTextInput(inputA!, "7");
    changeTextInput(inputB!, "5");

    expect(changes).toHaveLength(2);
    expect(changes[1]).toMatchObject({ a: "7", b: "5" });
  });

  it("prompts before replacing an incompatible value and mutates syntax and value atomically", async () => {
    const changes: ActivityNode[] = [];
    const checkboxEditor: StudioActivityPropertyEditorContribution = {
      id: "studio.property.checkbox",
      supports: descriptor => descriptor.typeName === "System.Boolean",
      component: ({ value, disabled, onChange }) => (
        <input type="checkbox" checked={value === true} disabled={disabled} onChange={event => onChange(event.target.checked)} />
      )
    };
    const container = renderPanel([
      input("Condition", { typeName: "System.Boolean", isWrapped: true })
    ], {
      editors: [checkboxEditor],
      onChange: next => changes.push(next),
      activity: activity({
        condition: { typeName: "System.Boolean", expression: { type: "JavaScript", value: "input.enabled" } }
      })
    });

    openAndSelect(container, "Literal");
    expect(changes).toHaveLength(0);
    expect(container.querySelector<HTMLInputElement>("input[aria-label='Condition expression']")?.value).toBe("input.enabled");
    const confirmation = container.querySelector<HTMLElement>("[role='alertdialog']")!;
    const cancel = [...confirmation.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Cancel")!;
    await nextFrame();
    expect(document.activeElement).toBe(cancel);

    flushSync(() => cancel.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    await nextFrame();
    expect(changes).toHaveLength(0);
    expect(container.querySelector("[role='alertdialog']")).toBeNull();
    expect(document.activeElement).toBe(container.querySelector(".wf-syntax-picker-trigger"));

    openAndSelect(container, "Literal");
    const replace = [...container.querySelectorAll<HTMLButtonElement>("[role='alertdialog'] button")]
      .find(button => button.textContent === "Replace value")!;
    flushSync(() => replace.click());
    await nextFrame();

    expect(changes).toHaveLength(1);
    expect(changes[0]?.condition).toEqual({
      typeName: "System.Boolean",
      expression: { type: "Literal", value: false }
    });
    expect(document.activeElement).toBe(container.querySelector("input[type='checkbox']"));
  });

  it("uses the admitted inline Contribution's default when an empty value changes to reference mode", async () => {
    const changes: ActivityNode[] = [];
    const factoryOnly: StudioExpressionEditorContribution = {
      id: "factory-only",
      order: 1,
      supports: context => context.syntax === "Secret",
      surfaces: {},
      createDefaultValue: () => ({ name: "wrong-provider" })
    };
    const admitted: StudioExpressionEditorContribution = {
      id: "secret.inline",
      order: 2,
      supports: context => context.syntax === "Secret",
      surfaces: { inline: TestReferenceEditor },
      createDefaultValue: () => ({ name: "new-secret" })
    };
    const container = renderPanel([input("Credential", { isWrapped: true })], {
      expressionDescriptors: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "Secret", displayName: "Secret", editingMode: "reference" }
      ],
      expressionEditors: [factoryOnly, admitted],
      onChange: next => changes.push(next),
      activity: activity({
        credential: { typeName: "System.String", expression: { type: "Literal", value: "" } }
      })
    });

    openAndSelect(container, "Secret");
    await nextFrame();

    expect(container.querySelector("[role='alertdialog']")).toBeNull();
    expect(changes).toHaveLength(1);
    expect(changes[0]?.credential).toEqual({
      typeName: "System.String",
      expression: { type: "Secret", value: { name: "new-secret" } }
    });
    expect(document.activeElement).toBe(container.querySelector("input[aria-label='Secret reference']"));
  });

  it("disables structured and reference modes without an inline Contribution and default factory", () => {
    const container = renderPanel([input("Source", { isWrapped: true })], {
      expressionDescriptors: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "Record", displayName: "Record", editingMode: "structured" },
        { type: "Variable", displayName: "Variable", editingMode: "reference" }
      ],
      expressionEditors: [{
        id: "record.inline-without-default",
        supports: context => context.syntax === "Record",
        surfaces: { inline: TestReferenceEditor }
      }]
    });

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
    const options = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")];
    const record = options.find(option => option.textContent?.includes("Record"));
    const variable = options.find(option => option.textContent?.includes("Variable"));
    expect(record?.disabled).toBe(true);
    expect(record?.textContent).toContain("default value factory");
    expect(variable?.disabled).toBe(true);
    expect(variable?.textContent).toContain("inline editor Contribution");
  });

  it("keeps the Object collection Contribution selectable from text mode and defaults it after confirmation", () => {
    const changes: ActivityNode[] = [];
    const collectionType = "System.Collections.Generic.ICollection`1[System.String]";
    const container = renderPanel([input("Items", { typeName: collectionType, isWrapped: true })], {
      expressionDescriptors: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "JavaScript", displayName: "JavaScript", editingMode: "text" },
        { type: "Object", displayName: "Object", editingMode: "structured" }
      ],
      expressionEditors: [createObjectExpressionEditorContribution(() => [])],
      onChange: next => changes.push(next),
      activity: activity({
        items: { typeName: collectionType, expression: { type: "JavaScript", value: "input.items" } }
      })
    });

    openAndSelect(container, "Object");
    expect(container.querySelector("[role='alertdialog']")).not.toBeNull();
    flushSync(() => [...container.querySelectorAll<HTMLButtonElement>("[role='alertdialog'] button")]
      .find(button => button.textContent === "Replace value")?.click());

    expect(changes).toHaveLength(1);
    expect(changes[0]?.items).toEqual({
      typeName: collectionType,
      expression: { type: "Object", value: [] }
    });
    expect(container.querySelector(".wf-collection-editor")).not.toBeNull();
  });

  it("keeps contribution diagnostics visible when text mode uses the generic inline fallback", () => {
    const liquidEditor: StudioExpressionEditorContribution = {
      id: "liquid.expanded",
      supports: context => context.syntax === "Liquid",
      surfaces: { expanded: () => <div>Enhanced Liquid editor</div> },
      diagnostics: (_context, value) => String(value).endsWith("{")
        ? [{ severity: "warning", code: "LIQUID_DRAFT", message: "Expression is incomplete." }]
        : []
    };
    const container = renderPanel([
      input("Template", { isWrapped: true })
    ], {
      expressionEditors: [liquidEditor],
      activity: activity({
        template: { typeName: "System.String", expression: { type: "Liquid", value: "{{ order.{" } }
      })
    });

    expect(container.querySelector<HTMLInputElement>("input[aria-label='Template expression']")?.value).toBe("{{ order.{");
    expect(container.querySelector(".wf-expression-editor-diagnostic.warning")?.textContent)
      .toContain("Expression is incomplete.");
  });

  it("uses text mode instead of a collection repeater for collection-typed expressions", () => {
    const container = renderPanel([
      input("Items", { typeName: "System.Collections.Generic.ICollection`1[System.String]", isWrapped: true })
    ], {
      activity: activity({
        items: { typeName: "System.Collections.Generic.ICollection`1[System.String]", expression: { type: "JavaScript", value: "input.items" } }
      })
    });

    expect(container.querySelector<HTMLInputElement>("input[aria-label='Items expression']")?.value).toBe("input.items");
    expect(container.querySelector(".wf-collection-editor")).toBeNull();
    expect(container.querySelector("button[aria-label='Open expanded Items editor']")).not.toBeNull();
  });

  it("requires an owning Contribution for arbitrary structured syntaxes while Object owns collection authoring", () => {
    const changes: ActivityNode[] = [];
    const collectionType = "System.Collections.Generic.ICollection`1[System.String]";
    const container = renderPanel([
      input("RecordItems", { typeName: collectionType, isWrapped: true }),
      input("ObjectItems", { typeName: collectionType, isWrapped: true })
    ], {
      expressionDescriptors: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "Object", displayName: "Object", editingMode: "structured" },
        { type: "Record", displayName: "Record", editingMode: "structured" }
      ],
      expressionEditors: [createObjectExpressionEditorContribution(() => [])],
      onChange: next => changes.push(next),
      activity: activity({
        recordItems: { typeName: collectionType, expression: { type: "Record", value: ["one"] } },
        objectItems: { typeName: collectionType, expression: { type: "Object", value: ["one"] } }
      })
    });

    expect(container.querySelectorAll(".wf-collection-editor")).toHaveLength(1);
    expect(container.textContent).toContain("No editor is available for Record.");
    expect(container.textContent).toContain("current value is preserved and read-only");
    expect(container.textContent).not.toContain("No editor is available for Object.");
    const recordRow = [...container.querySelectorAll<HTMLElement>(".wf-property-row")]
      .find(row => row.textContent?.includes("RecordItems"))!;
    flushSync(() => recordRow.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
    const recordOption = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")]
      .find(option => option.textContent?.includes("Record"));
    expect(recordOption?.getAttribute("aria-selected")).toBe("true");
    expect(recordOption?.disabled).toBe(true);
    expect(changes).toHaveLength(0);
  });

  it("shows a compact scalar Object summary and preserves invalid JSON across expanded-editor reopening", () => {
    const changes: ActivityNode[] = [];
    const container = renderPanel([input("Payload", { typeName: "System.Object", isWrapped: true })], {
      expressionDescriptors: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "Object", displayName: "Object", editingMode: "structured" }
      ],
      expressionEditors: [createObjectExpressionEditorContribution(() => [])],
      onChange: next => changes.push(next),
      activity: activity({
        payload: { typeName: "System.Object", expression: { type: "Object", value: { first: 1, second: 2 } } }
      })
    });

    expect(container.querySelector(".wf-object-expression-summary")?.textContent).toContain("2 properties");
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Open expanded Payload editor']")?.click());

    const expanded = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Payload JSON value']");
    expect(expanded?.value).toContain('"first": 1');
    expect(container.querySelector("[role='dialog']")).not.toBeNull();

    changeTextArea(expanded!, '{"first":');
    expect(container.querySelector(".studio-code-editor-diagnostics")?.textContent).toContain("Invalid JSON");
    expect(changes).toHaveLength(0);
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Close Payload editor']")?.click());
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Open expanded Payload editor']")?.click());

    expect(container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Payload JSON value']")?.value).toBe('{"first":');
    expect(container.querySelector(".studio-code-editor-diagnostics")?.textContent).toContain("Invalid JSON");
  });
});

const secretDescriptor: StudioExpressionDescriptor = { type: "Secret", displayName: "Secret", editingMode: "reference" };
const secretPicker: StudioExpressionEditorContribution = {
  id: "secret.picker",
  supports: context => context.syntax === "Secret",
  surfaces: { inline: SecretPickerStub },
  createDefaultValue: () => null
};

function clickButton(scope: ParentNode, label: string) {
  const button = scope.querySelector<HTMLButtonElement>(`button[aria-label='${label}']`);
  expect(button, `button "${label}"`).not.toBeNull();
  flushSync(() => button!.click());
}

describe("secret-only inputs", () => {
  const literalEditor: StudioActivityPropertyEditorContribution = {
    id: "studio.property.singleline",
    supports: () => true,
    component: ({ value, onChange }) => <input type="text" aria-label="Literal editor" value={String(value ?? "")} onChange={event => onChange(event.target.value)} />
  };
  const authorization = input("Authorization", { isWrapped: true, isSensitive: true, isCredential: true, defaultSyntax: "Literal" });
  const legacyLiteral = "plain-old-words";

  const renderSecretOnly = (options: Parameters<typeof renderPanel>[1] = {}, overrides: Partial<StudioActivityInputDescriptor> = {}) => renderPanel([{ ...authorization, ...overrides }, input("Url", { isWrapped: true })], {
    editors: [literalEditor],
    expressionEditors: [secretPicker],
    expressionDescriptors: [...backendExpressionDescriptors, secretDescriptor],
    ...options
  });
  const rowOf = (container: HTMLElement, label: string) =>
    [...container.querySelectorAll<HTMLElement>(".wf-property-row")].find(row => row.querySelector("label")?.textContent === label)!;
  const syntaxOptions = (row: HTMLElement) => {
    flushSync(() => row.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
    return [...document.querySelectorAll("[role='option']")].map(option => option.textContent);
  };

  it("defaults to the secret picker ahead of the descriptor's default syntax and offers no literal editor", () => {
    const row = rowOf(renderSecretOnly(), "Authorization");

    expect(row.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.title).toBe("Secret");
    expect(row.querySelector("[aria-label='Secret picker']")).not.toBeNull();
    expect(row.querySelector("[aria-label='Literal editor']")).toBeNull();
    expect(row.querySelector("input[type='text'], textarea")).toBeNull();
  });

  it("offers only the Secret syntax in the picker, while other inputs keep every syntax", () => {
    const container = renderSecretOnly();

    expect(syntaxOptions(rowOf(container, "Authorization"))).toEqual(["Secret"]);
    flushSync(() => document.body.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(syntaxOptions(rowOf(container, "Url"))).toEqual(expect.arrayContaining(["Literal", "JavaScript", "Secret"]));
    expect(rowOf(container, "Url").querySelector("[aria-label='Literal editor']")).not.toBeNull();
  });

  // Reference and multiline rows use the property heading; inline and dictionary layouts keep their own controls.
  const layouts: Array<[string, string, Partial<StudioActivityInputDescriptor>, StudioExpressionDescriptor["editingMode"]]> = [
    ["reference", ".wf-syntax-picker-trigger.compact", {}, "reference"],
    ["inline", ".wf-expression-field .wf-syntax-picker-trigger", {}, "literal"],
    ["multiline heading", ".wf-syntax-picker-trigger.compact", { uiHint: "multiline" }, "reference"],
    ["dictionary", ".wf-dictionary-expression-toolbar .wf-syntax-picker-trigger", { typeName: "System.Collections.Generic.IDictionary`2[System.String,System.String]" }, "literal"]
  ];

  it.each(layouts)("offers only the Secret syntax in the %s layout", (_layout, picker, overrides, secretMode) => {
    const container = renderSecretOnly({ expressionDescriptors: [...backendExpressionDescriptors, { ...secretDescriptor, editingMode: secretMode }] }, overrides);
    const row = rowOf(container, "Authorization");

    expect(row.querySelectorAll(".wf-syntax-picker-trigger")).toHaveLength(1);
    expect(row.querySelector(picker)).not.toBeNull();
    if (_layout === "reference" || _layout === "multiline heading") {
      expect(row.querySelector(".wf-property-row-header-meta > .wf-syntax-picker.compact")).not.toBeNull();
      expect(row.querySelector(":scope > .wf-syntax-picker")).toBeNull();
    }
    expect(syntaxOptions(row)).toEqual(["Secret"]);
  });

  it("does not display a value stored before the rule as text", () => {
    const container = renderSecretOnly({
      activity: activity({ authorization: { typeName: "System.String", expression: { type: "Literal", value: legacyLiteral } } })
    });
    const row = rowOf(container, "Authorization");

    expect(container.innerHTML).not.toContain(legacyLiteral);
    expect(row.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.title).toBe("Secret");
    expect(row.querySelector("[aria-label='Literal editor']")).toBeNull();
    expect(row.querySelector("[aria-label='Secret picker']")?.getAttribute("data-value")).toBe("null");
  });

  it("shows the existing unavailable state, never a literal editor, when the Secret descriptor is absent", () => {
    const container = renderSecretOnly({ expressionEditors: [], expressionDescriptors: backendExpressionDescriptors });
    const row = rowOf(container, "Authorization");

    expect(row.textContent).toContain("No editor is available for Secret.");
    expect(row.querySelector("[aria-label='Literal editor']")).toBeNull();
    expect(row.querySelector("input[type='text'], textarea")).toBeNull();
  });

  it("keeps the secret picker, with its own states, when the backend lists no Secret syntax", () => {
    const row = rowOf(renderSecretOnly({ expressionDescriptors: backendExpressionDescriptors }), "Authorization");

    expect(row.querySelector("[aria-label='Secret picker']")).not.toBeNull();
    expect(row.querySelector("[aria-label='Literal editor']")).toBeNull();
  });

  it("shows the existing unavailable state when the Secret descriptor exists but no secret picker is registered", () => {
    const container = renderSecretOnly({ expressionEditors: [] });
    const row = rowOf(container, "Authorization");

    expect(row.textContent).toContain("No editor is available for Secret.");
    expect(row.querySelector("[aria-label='Literal editor']")).toBeNull();
  });

  it.each(["literal", "text"] as const)("never falls back to a literal, text or expanded editor when the Secret descriptor reports %s mode", mode => {
    const container = renderSecretOnly({
      expressionEditors: [],
      expressionDescriptors: [...backendExpressionDescriptors, { ...secretDescriptor, editingMode: mode }]
    });
    const row = rowOf(container, "Authorization");

    expect(row.textContent).toContain("No editor is available for Secret.");
    expect(row.querySelector("[aria-label='Literal editor']")).toBeNull();
    expect(row.querySelector("input[type='text'], textarea")).toBeNull();
    expect(row.querySelector("button[aria-label='Open expanded Authorization editor']")).toBeNull();
  });

  it("shows no literal editor for a secret-only input the backend marks unwrapped", () => {
    const container = renderPanel([input("Authorization", { isCredential: true })], { editors: [literalEditor] });

    expect(container.textContent).toContain("No editor is available for Secret.");
    expect(container.querySelector("[aria-label='Literal editor']")).toBeNull();
  });

  it("offers no conversion or expand control, writes a picked Secret under the Secret syntax and unbinds the input when it is cleared", () => {
    const changes: ActivityNode[] = [];
    const url = { typeName: "System.String", expression: { type: "Literal", value: "https://example.test" } };
    const container = renderSecretOnly({
      onChange: next => changes.push(next),
      activity: activity({
        authorization: { typeName: "System.String", expression: { type: "Literal", value: legacyLiteral }, conversion: { mode: "json" } },
        url
      })
    });
    const row = rowOf(container, "Authorization");

    // An authored conversion would otherwise show its chip, and the toggle shows on every wrapped input.
    expect(row.querySelector(".wf-conversion-toggle, .wf-conversion-chip, .wf-conversion-control")).toBeNull();
    expect(row.querySelector("button[aria-label='Open expanded Authorization editor']")).toBeNull();
    // A secret picker that asks to expand does not open the expanded editor, which carries its own conversion control.
    clickButton(row, "Expand secret");
    expect(document.querySelector("[role='dialog'], .wf-conversion-control")).toBeNull();
    expect(changes).toEqual([]);

    clickButton(row, "Pick secret");
    expect(changes).toHaveLength(1);
    expect(changes[0]?.authorization).toEqual({ typeName: "System.String", expression: { type: "Secret", value: { name: "tokens" } }, conversion: { mode: "json" } });

    clickButton(row, "Clear secret");
    expect(changes).toHaveLength(2);
    expect(changes[1]).not.toHaveProperty("authorization");
    expect(changes[1]?.url).toEqual(url);
    expect(JSON.stringify(changes[1])).not.toContain(legacyLiteral);
    expect(document.body.innerHTML).not.toContain(legacyLiteral);
  });
});

describe("property editor context", () => {
  it("gives a property editor the node id of the activity being edited", () => {
    const identityEditor: StudioActivityPropertyEditorContribution = {
      id: "test.identity",
      supports: () => true,
      component: ({ context }) => <span data-activity-id={context.activityId ?? ""} />
    };

    const container = renderPanel([input("Message")], { editors: [identityEditor] });

    expect(container.querySelector("[data-activity-id]")?.getAttribute("data-activity-id")).toBe("node-1");
  });
});

describe("masked inputs", () => {
  const storedValue = "stored-value-words";
  const maskedNotice = "This value is masked. It is preserved and read-only here.";
  // The panel asks nothing of the masked editor but its id; this one renders nothing of the value.
  const maskedEditor: StudioActivityPropertyEditorContribution = {
    id: "studio.property.password",
    supports: () => true,
    component: () => <span>Value set</span>
  };
  // Shows everything it is given, as any editor but the masked one would.
  const revealingEditor: StudioActivityPropertyEditorContribution = {
    id: "test.revealing",
    supports: () => true,
    component: ({ value }) => <input type="text" aria-label="Revealing editor" value={JSON.stringify(value)} readOnly />
  };
  // The real Object editor: its expanded surface prints the stored JSON.
  const objectEditor = createObjectExpressionEditorContribution(() => [revealingEditor]);
  const sensitive: Partial<StudioActivityInputDescriptor> = { isSensitive: true };
  const token = (type: string, value: unknown) => ({ typeName: "System.String", expression: { type, value } });
  const renderToken = (
    stored: ReturnType<typeof token>,
    overrides: Partial<StudioActivityInputDescriptor>,
    options: Parameters<typeof renderPanel>[1] = {}
  ) => {
    const changes: ActivityNode[] = [];
    const container = renderPanel([input("ApiToken", { isWrapped: true, ...overrides })], {
      editors: [maskedEditor],
      expressionEditors: [secretPicker, objectEditor],
      expressionDescriptors: [...backendExpressionDescriptors, secretDescriptor],
      onChange: next => changes.push(next),
      activity: activity({ apiToken: stored }),
      ...options
    });
    return { container, changes };
  };
  const switchToJavaScript = (overrides: Partial<StudioActivityInputDescriptor>, stored: unknown = storedValue) => {
    const rendered = renderToken(token("Literal", stored), overrides);
    openAndSelect(rendered.container, "JavaScript");
    return rendered;
  };
  const openExpandedEditor = (container: HTMLElement) => clickButton(container, "Open expanded ApiToken editor");
  const expandedEditor = () => document.querySelector<HTMLElement>("[role='dialog']");
  const expandAffordance = (container: HTMLElement) =>
    container.querySelector(".wf-expression-expand-button, .wf-property-expand-row, .wf-dictionary-open-expanded");
  const rowPicker = (container: HTMLElement) => container.querySelector(".wf-syntax-picker-trigger");
  const inlineSource = (container: HTMLElement) => container.querySelector<HTMLInputElement>("input[aria-label='ApiToken expression']")!.value;
  const replaceButton = () => [...document.querySelectorAll<HTMLButtonElement>("[role='alertdialog'] button")]
    .find(button => button.textContent === "Replace value") ?? null;
  const clearTextFields = () => document.querySelectorAll("textarea, input[type='text']");
  // A React-controlled field's value is not in innerHTML, so each field's value is checked as well.
  const isShown = (text: string) => document.body.innerHTML.includes(text)
    || [...document.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>("input, textarea")].some(field => field.value.includes(text));
  const expectNowhere = (text: string) => expect(isShown(text), `"${text}" is shown`).toBe(false);
  const masked: Array<[string, Partial<StudioActivityInputDescriptor>]> = [
    ["a sensitive input", sensitive],
    ["a password-hinted input", { uiHint: "password" }]
  ];
  // Masked inputs whose Object syntax holds the stored value itself: Object JSON is data deserialized into the input.
  const objectShapes = [
    { shape: "scalar", declared: sensitive, objectValue: { token: storedValue }, objectDefault: {}, literalDefault: "" },
    { shape: "POCO-typed", declared: { ...sensitive, typeName: "Contoso.Settings" }, objectValue: { token: storedValue }, objectDefault: {}, literalDefault: "" },
    { shape: "json-hinted", declared: { ...sensitive, uiHint: "json", typeName: "String", collectionKind: "List" }, objectValue: [storedValue], objectDefault: [], literalDefault: [] },
    { shape: "malformed-collection", declared: { ...sensitive, typeName: "String", collectionKind: "List" }, objectValue: { header: storedValue }, objectDefault: [], literalDefault: [] }
  ];

  it("never offers to expand a sensitive multiline input into a clear-text editor", () => {
    const { container } = renderToken(token("Literal", storedValue), { ...sensitive, uiHint: "multiline" });

    expect(container.textContent).toContain("Value set");
    expect(expandAffordance(container)).toBeNull();
  });

  it.each(masked)("does not carry the stored literal of %s into a text syntax, inline or expanded", (_label, overrides) => {
    const { container, changes } = switchToJavaScript(overrides);
    // Discarding a stored value asks first, like any other lossy switch.
    expect(changes).toEqual([]);
    flushSync(() => replaceButton()!.click());

    expect(changes).toHaveLength(1);
    expect(changes[0]?.apiToken).toEqual(token("JavaScript", ""));
    expect(inlineSource(container)).toBe("");
    expectNowhere(storedValue);

    openExpandedEditor(container);
    expect(container.querySelector<HTMLTextAreaElement>("textarea[aria-label='ApiToken expanded value']")!.value).toBe("");
    expectNowhere(storedValue);
  });

  it.each(masked)("switches %s with nothing stored straight to a text syntax", (_label, overrides) => {
    const { container, changes } = switchToJavaScript(overrides, "");

    expect(container.querySelector("[role='alertdialog']")).toBeNull();
    expect(changes[0]?.apiToken).toEqual(token("JavaScript", ""));
  });

  it("still carries an ordinary input's literal into a text syntax", () => {
    const { container, changes } = switchToJavaScript({});

    expect(container.querySelector("[role='alertdialog']")).toBeNull();
    expect(changes[0]?.apiToken).toEqual(token("JavaScript", storedValue));
    expect(inlineSource(container)).toBe(storedValue);
  });

  // Every syntax switch a masked input can make, from the row and from the expanded editor (which opens only over
  // a text syntax), with a registry whose only editor shows everything. The value stored under Literal or Object
  // must be kept, yet shown nowhere, and no clear-text field may be left editing it. An expression source or a
  // Secret Reference name may show; only those syntaxes offer the expanded editor.
  const surfaces = {
    row: { open: (_container: HTMLElement) => {}, scope: (container: HTMLElement): ParentNode => container },
    "expanded editor": { open: openExpandedEditor, scope: (_container: HTMLElement): ParentNode => expandedEditor()! }
  };
  type Step = {
    shape: string;
    declared: Partial<StudioActivityInputDescriptor>;
    surface: keyof typeof surfaces;
    from: string;
    authored: unknown;
    to: string;
    expandableBefore: boolean;
    shownBefore: boolean;
    asksFirst: boolean;
    storedAfter: unknown;
    expandableAfter: boolean;
    clearTextFieldsAfter: number;
  };
  const scalarSteps: Step[] = [
    { shape: "scalar", declared: sensitive, surface: "row", from: "Literal", authored: storedValue, to: "JavaScript", expandableBefore: false, shownBefore: false, asksFirst: true, storedAfter: "", expandableAfter: true, clearTextFieldsAfter: 1 },
    { shape: "scalar", declared: sensitive, surface: "row", from: "JavaScript", authored: storedValue, to: "Literal", expandableBefore: true, shownBefore: true, asksFirst: false, storedAfter: storedValue, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "row", from: "Literal", authored: storedValue, to: "Liquid", expandableBefore: false, shownBefore: false, asksFirst: true, storedAfter: "", expandableAfter: true, clearTextFieldsAfter: 1 },
    { shape: "scalar", declared: sensitive, surface: "row", from: "Liquid", authored: storedValue, to: "Literal", expandableBefore: true, shownBefore: true, asksFirst: false, storedAfter: storedValue, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "row", from: "Literal", authored: storedValue, to: "Secret", expandableBefore: false, shownBefore: false, asksFirst: true, storedAfter: null, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "row", from: "Secret", authored: { name: storedValue }, to: "Literal", expandableBefore: false, shownBefore: true, asksFirst: true, storedAfter: "", expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "expanded editor", from: "JavaScript", authored: storedValue, to: "Literal", expandableBefore: true, shownBefore: true, asksFirst: false, storedAfter: storedValue, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "expanded editor", from: "Liquid", authored: storedValue, to: "Literal", expandableBefore: true, shownBefore: true, asksFirst: false, storedAfter: storedValue, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "expanded editor", from: "JavaScript", authored: storedValue, to: "Secret", expandableBefore: true, shownBefore: true, asksFirst: true, storedAfter: null, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape: "scalar", declared: sensitive, surface: "expanded editor", from: "Liquid", authored: storedValue, to: "Secret", expandableBefore: true, shownBefore: true, asksFirst: true, storedAfter: null, expandableAfter: false, clearTextFieldsAfter: 0 }
  ];
  // A malformed collection's well-formed default reads back as Literal, so it never offers expansion.
  const objectSteps: Step[] = objectShapes.flatMap(({ shape, declared, objectValue, objectDefault, literalDefault }) => [
    { shape, declared, surface: "row", from: "Literal", authored: storedValue, to: "Object", expandableBefore: false, shownBefore: false, asksFirst: true, storedAfter: objectDefault, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape, declared, surface: "row", from: "Object", authored: objectValue, to: "Literal", expandableBefore: false, shownBefore: false, asksFirst: true, storedAfter: literalDefault, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape, declared, surface: "row", from: "Object", authored: objectValue, to: "JavaScript", expandableBefore: false, shownBefore: false, asksFirst: true, storedAfter: "", expandableAfter: true, clearTextFieldsAfter: 1 },
    { shape, declared, surface: "row", from: "JavaScript", authored: storedValue, to: "Object", expandableBefore: true, shownBefore: true, asksFirst: true, storedAfter: objectDefault, expandableAfter: false, clearTextFieldsAfter: 0 },
    { shape, declared, surface: "expanded editor", from: "JavaScript", authored: storedValue, to: "Object", expandableBefore: true, shownBefore: true, asksFirst: true, storedAfter: objectDefault, expandableAfter: false, clearTextFieldsAfter: 0 }
  ]);

  it.each([...scalarSteps, ...objectSteps])("$shape: after switching $from to $to from the $surface, the stored value is nowhere in sight and no clear-text field edits it", step => {
    const { container, changes } = renderToken(token(step.from, step.authored), step.declared, { editors: [revealingEditor] });
    expect(expandAffordance(container) !== null).toBe(step.expandableBefore);
    surfaces[step.surface].open(container);
    expect(isShown(storedValue)).toBe(step.shownBefore);

    openAndSelect(surfaces[step.surface].scope(container), step.to);
    expect(replaceButton() !== null).toBe(step.asksFirst);
    flushSync(() => replaceButton()?.click());

    expect(changes.at(-1)?.apiToken).toEqual(token(step.to, step.storedAfter));
    expectNowhere(storedValue);
    expect(expandedEditor()).toBeNull();
    expect(expandAffordance(container) !== null).toBe(step.expandableAfter);
    expect(clearTextFields()).toHaveLength(step.clearTextFieldsAfter);
  });

  it.each([
    { change: "an undo", nodeId: "node-1", shape: "scalar", declared: sensitive, lands: token("Literal", storedValue) },
    { change: "selecting another activity of the same type", nodeId: "node-2", shape: "scalar", declared: sensitive, lands: token("Literal", storedValue) },
    ...objectShapes.map(({ shape, declared, objectValue }) => ({ change: "an undo", nodeId: "node-1", shape, declared, lands: token("Object", objectValue) }))
  ])("closes the expanded editor when $change lands a $shape input on its stored $lands.expression.type value", ({ nodeId, declared, lands }) => {
    const { container } = renderToken(token("JavaScript", "author-code"), declared, { editors: [revealingEditor] });
    openExpandedEditor(container);
    expect(expandedEditor()).not.toBeNull();

    flushSync(() => replaceActivity(activity({ nodeId, apiToken: lands })));

    expectNowhere(storedValue);
    expect(expandedEditor()).toBeNull();
    expect(clearTextFields()).toHaveLength(0);
  });

  it("returns focus to the row when a switch inside the expanded editor closes it", async () => {
    const { container } = renderToken(token("JavaScript", "author-code"), sensitive);
    openExpandedEditor(container);

    openAndSelect(expandedEditor()!, "Literal");
    await nextFrame();

    expect(expandedEditor()).toBeNull();
    expect(document.activeElement).toBe(rowPicker(container));
  });

  it("returns focus to the row when an undo closes the expanded editor, and does not reopen it by itself", async () => {
    const { container } = renderToken(token("JavaScript", "author-code"), sensitive);
    openExpandedEditor(container);

    flushSync(() => replaceActivity(activity({ apiToken: token("Literal", storedValue) })));
    await nextFrame();
    expect(document.activeElement).toBe(rowPicker(container));

    openAndSelect(container, "JavaScript");
    flushSync(() => replaceButton()!.click());
    expect(container.querySelector<HTMLInputElement>("input[aria-label='ApiToken expression']")).not.toBeNull();
    expect(expandedEditor()).toBeNull();
  });

  it("moves focus from elsewhere in the row to its syntax picker when the expanded editor closes under it", async () => {
    const { container } = renderToken(token("JavaScript", "author-code"), sensitive);
    openExpandedEditor(container);
    // In the row but outside the expanded editor when the editor closes.
    container.querySelector<HTMLButtonElement>(".wf-conversion-toggle")!.focus();

    flushSync(() => replaceActivity(activity({ apiToken: token("Literal", storedValue) })));
    await nextFrame();

    expect(expandedEditor()).toBeNull();
    expect(document.activeElement).toBe(rowPicker(container));
  });

  it("leaves focus on another activity selected while the expanded editor is open", async () => {
    const canvasNode = document.body.appendChild(document.createElement("button"));
    onTestFinished(() => canvasNode.remove());
    const { container } = renderToken(token("JavaScript", "author-code"), sensitive);
    openExpandedEditor(container);
    canvasNode.focus();

    flushSync(() => replaceActivity(activity({ nodeId: "node-2", apiToken: token("Literal", storedValue) })));
    await nextFrame();

    expect(expandedEditor()).toBeNull();
    expect(document.activeElement).toBe(canvasNode);
  });

  // The expanded editor's open state follows the row's expandability for every input, masked or not.
  it("closes an ordinary input's expanded editor when a switch inside it leaves the row unexpandable", () => {
    const { container, changes } = renderToken(token("JavaScript", "author-code"), { uiHint: "dropdown" }, { editors: [revealingEditor] });
    openExpandedEditor(container);

    openAndSelect(expandedEditor()!, "Literal");

    expect(changes.at(-1)?.apiToken).toEqual(token("Literal", "author-code"));
    expect(expandedEditor()).toBeNull();
    expect(container.querySelector<HTMLInputElement>("input[aria-label='Revealing editor']")?.value).toBe('"author-code"');
  });

  it.each([
    { label: "a password-hinted number", declared: { uiHint: "password", typeName: "Int32" }, stored: token("Literal", 731904), shown: "731904", editors: [revealingEditor] },
    { label: "a sensitive text input another editor claims ahead of the masked one", declared: sensitive, stored: token("Literal", storedValue), shown: storedValue, editors: [revealingEditor, maskedEditor] },
    { label: "a sensitive list the masked editor claims", declared: { ...sensitive, typeName: "String", collectionKind: "List" }, stored: token("Literal", [storedValue]), shown: storedValue, editors: [maskedEditor, revealingEditor] },
    { label: "a sensitive dictionary the masked editor claims", declared: { ...sensitive, typeName: "String", collectionKind: "Dictionary" }, stored: token("Literal", { header: storedValue }), shown: storedValue, editors: [maskedEditor, revealingEditor] },
    { label: "a sensitive input under a syntax without a descriptor", declared: sensitive, stored: token("CSharp", storedValue), shown: storedValue, editors: [maskedEditor] },
    ...objectShapes.map(({ shape, declared, objectValue }) => ({ label: `a ${shape} input's Object value`, declared, stored: token("Object", objectValue), shown: storedValue, editors: [revealingEditor] }))
  ] as Array<{ label: string; declared: Partial<StudioActivityInputDescriptor>; stored: ReturnType<typeof token>; shown: string; editors: StudioActivityPropertyEditorContribution[] }>)(
    "masks the stored value of $label: the masked notice, no editor, no expanded editor and no write",
    ({ declared, stored, shown, editors }) => {
      const { container, changes } = renderToken(stored, declared, { editors });

      expectNowhere(shown);
      expect(container.textContent).toContain(maskedNotice);
      expect(container.textContent).not.toContain("Value set");
      expect(expandAffordance(container)).toBeNull();
      expect(changes).toEqual([]);
    }
  );

  const switchToJavaScriptAndReplace = (container: HTMLElement) => {
    openAndSelect(container, "JavaScript");
    expect(replaceButton()).not.toBeNull();
    flushSync(() => replaceButton()!.click());
  };

  const literalEditor: StudioExpressionEditorContribution = { id: "test.literal", supports: context => context.syntax === "Literal", surfaces: { inline: EchoExpressionEditor } };
  const withExpressionTypes = [...backendExpressionDescriptors, secretDescriptor];

  it.each([
    ["a Literal editor", literalEditor, withExpressionTypes, () => {}],
    ["a Literal editor while expression types are unavailable", literalEditor, [], () => {}],
    ["Literal diagnostics", { id: "test.diagnostics", supports: context => context.syntax === "Literal", surfaces: {}, diagnostics: (_context, value) => [{ message: String(value) }] }, withExpressionTypes, () => {}],
    ["a JavaScript default built from its document", { id: "test.javascript", supports: context => context.syntax === "JavaScript", surfaces: { inline: EchoExpressionEditor }, createDefaultValue: context => context.document?.source }, withExpressionTypes, switchToJavaScriptAndReplace]
  ] as Array<[string, StudioExpressionEditorContribution, StudioExpressionDescriptor[], (container: HTMLElement) => void]>)("gives nothing of a masked literal to %s", (_label, contribution, expressionDescriptors, act) => {
    const { container } = renderToken(token("Literal", storedValue), sensitive, { expressionEditors: [secretPicker, contribution], expressionDescriptors });

    act(container);

    expectNowhere(storedValue);
  });
});

function SecretPickerStub({ value, onChange, onExpand }: React.ComponentProps<NonNullable<StudioExpressionEditorContribution["surfaces"]["inline"]>>) {
  return (
    <div aria-label="Secret picker" data-value={JSON.stringify(value)}>
      <button type="button" aria-label="Pick secret" onClick={() => onChange({ name: "tokens" })} />
      <button type="button" aria-label="Clear secret" onClick={() => onChange(null)} />
      <button type="button" aria-label="Expand secret" onClick={() => onExpand?.()} />
    </div>
  );
}

function EchoExpressionEditor({ value }: React.ComponentProps<NonNullable<StudioExpressionEditorContribution["surfaces"]["inline"]>>) {
  return <output>{String(value)}</output>;
}

function TestReferenceEditor({ value, disabled, initialFocus }: React.ComponentProps<NonNullable<StudioExpressionEditorContribution["surfaces"]["inline"]>>) {
  const ref = React.useRef<HTMLInputElement>(null);
  React.useEffect(() => {
    if (initialFocus) ref.current?.focus();
  }, [initialFocus]);
  return <input ref={ref} aria-label="Secret reference" value={JSON.stringify(value)} disabled={disabled} readOnly />;
}

function openAndSelect(scope: ParentNode, label: string) {
  const trigger = scope.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger");
  expect(trigger, "syntax picker").not.toBeNull();
  flushSync(() => trigger!.click());
  const option = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")]
    .find(candidate => candidate.textContent === label);
  expect(option, `${label} option`).toBeDefined();
  flushSync(() => option!.click());
}

async function nextFrame() {
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
}

function changeTextArea(input: HTMLTextAreaElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")?.set?.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function changeTextInput(input: HTMLInputElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
