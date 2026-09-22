import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type {
  StudioActivityDefinitionImplementationEditorProps,
  StudioExpressionDescriptor,
  StudioExpressionEditorContribution
} from "@elsa-workflows/studio-sdk";
import {
  ActivityGraphImplementationEditor,
  ActivityGraphPublicInterfaceEditor,
  reconcileGraphLayout
} from "../ActivityGraphImplementationEditor";
import { inputReferenceContribution } from "../inputReferenceContribution";
import { flowchartStructureKind } from "../flowchartStartNode";
import { bpmnStructureKind } from "../bpmn/bpmnTypes";
import { activityDragDataType } from "../workflow-editor/constants";
import type { ActivityCatalogItem } from "../workflowTypes";

vi.mock("../api/activityDesign", () => ({
  useWorkflowActivities: () => ({
    data: { activities: catalogItems },
    isPending: false,
    isError: false
  })
}));
vi.mock("../api/expressions", () => ({
  listExpressionDescriptors: async () => expressionDescriptors,
  listConversionProfiles: async () => [],
  listVariableTypeDescriptors: async () => []
}));

let catalogItems: ActivityCatalogItem[] = [catalogItem()];
let expressionDescriptors: StudioExpressionDescriptor[] = [];
const mounted: Array<{ root: Root; container: HTMLDivElement }> = [];

afterEach(() => {
  for (const item of mounted.splice(0)) {
    flushSync(() => item.root.unmount());
    item.container.remove();
  }
  catalogItems = [catalogItem()];
  expressionDescriptors = [];
});

describe("ActivityGraphImplementationEditor boundary outcome mappings", () => {
  it("adds a stable root-outcome mapping and preserves existing mappings", () => {
    const onChange = vi.fn();
    const rendered = renderEditor({
      value: implementationValue({
        outcomeMappings: [{ sourceOutcomeReferenceKey: "approved", boundaryOutcomeReferenceKey: "approved-boundary" }]
      }),
      onChange
    });

    const source = rendered.container.querySelector<HTMLSelectElement>("select[aria-label='Root outcome reference key']")!;
    const boundary = rendered.container.querySelector<HTMLSelectElement>("select[aria-label='Boundary outcome reference key']")!;
    expect([...source.options].map(option => ({ value: option.value, disabled: option.disabled }))).toContainEqual({ value: "rejected", disabled: false });
    expect([...source.options].map(option => option.value)).not.toContain("unstable-name-only");
    expect([...boundary.options].map(option => ({ value: option.value, disabled: option.disabled }))).toContainEqual({ value: "declined-boundary", disabled: false });

    change(source, "rejected");
    change(boundary, "declined-boundary");
    click(buttonByText(rendered.container, "Add mapping"));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      payload: expect.objectContaining({
        outcomeMappings: [
          { sourceOutcomeReferenceKey: "approved", boundaryOutcomeReferenceKey: "approved-boundary" },
          { sourceOutcomeReferenceKey: "rejected", boundaryOutcomeReferenceKey: "declined-boundary" }
        ]
      })
    }));
  });

  it("renders mapping controls as read-only when the draft is locked", () => {
    const rendered = renderEditor({
      readOnly: true,
      value: implementationValue({
        outcomeMappings: [{ sourceOutcomeReferenceKey: "approved", boundaryOutcomeReferenceKey: "approved-boundary" }]
      })
    });

    expect(rendered.container.querySelector<HTMLSelectElement>("select[aria-label='Root outcome reference key']")?.disabled).toBe(true);
    expect(rendered.container.querySelector<HTMLSelectElement>("select[aria-label='Boundary outcome reference key']")?.disabled).toBe(true);
    expect(buttonByText(rendered.container, "Add mapping").disabled).toBe(true);
    expect(buttonByText(rendered.container, "Remove mapping").disabled).toBe(true);
  });

  it("keeps schema 1 free of schema-2 mapping controls", () => {
    const rendered = renderEditor({ providerSchemaVersion: "1" });

    expect(rendered.container.textContent).not.toContain("Boundary outcome mappings");
  });
});

describe("ActivityGraphImplementationEditor shared designer", () => {
  it("uses the shared workspace, treats the root as scope owner, and persists undo through onChange", () => {
    catalogItems = [flowchartCatalogItem(), leafCatalogItem()];
    const onChange = vi.fn();
    const rendered = renderDesigner({
      value: flowchartImplementationValue(),
      onChange
    });

    expect(rendered.container.querySelector("[data-graph-authoring-resource='activity-definition-graph']")).not.toBeNull();
    expect(rendered.container.querySelector("[data-graph-root-location]")?.textContent).toContain("Flowchart");
    expect(rendered.container.textContent).not.toContain("Inputs (JSON array)");
    expect(rendered.container.querySelector("[data-graph-node-id='root']")).toBeNull();
    expect(buttonByText(rendered.container, "Collapse activities panel", "aria-label")).toBeTruthy();
    expect(buttonByText(rendered.container, "Maximize activities panel", "aria-label")).toBeTruthy();
    expect(buttonByText(rendered.container, "Collapse inspector panel", "aria-label")).toBeTruthy();
    expect(buttonByText(rendered.container, "Maximize inspector panel", "aria-label")).toBeTruthy();
    expect(rendered.container.querySelector("[aria-label='Resize activities panel']")).toBeTruthy();
    expect(rendered.container.querySelector("[aria-label='Resize inspector panel']")).toBeTruthy();
    expect(buttonByText(rendered.container, "Auto-layout Activity Graph", "aria-label")).toBeTruthy();
    expect(rendered.container.querySelector("[aria-label='Activity inspector sections']")?.textContent).toContain("Variables");

    click(buttonByText(rendered.container, "Primitives1"));
    click(buttonByText(rendered.container, "Write line"));
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({
      payload: expect.objectContaining({
        rootActivity: expect.objectContaining({
          structure: expect.objectContaining({
            payload: expect.objectContaining({
              activities: [expect.objectContaining({ activityVersionId: "write-line-v1" })]
            })
          })
        })
      })
    }));
    expect(rendered.container.querySelector("[aria-label='Activity inspector sections']")?.textContent).toContain("Inputs");
    expect(rendered.container.querySelector("[aria-label='Activity inspector sections']")?.textContent).toContain("Outputs");
    expect(rendered.container.querySelector("[aria-label='Activity inspector sections']")?.textContent).toContain("Details");
    expect(rendered.container.querySelector("[aria-label='Activity inspector sections']")?.textContent).toContain("Version");

    click(buttonByText(rendered.container, "Undo Activity Graph edit", "aria-label"));
    expect(onChange).toHaveBeenLastCalledWith(flowchartImplementationValue());
  });

  it("places public contract inputs in the graph node expression scope", async () => {
    expressionDescriptors = [{ type: "Input", displayName: "Input", editingMode: "reference" }];
    catalogItems = [flowchartCatalogItem(), inputLeafCatalogItem()];
    const rendered = renderDesigner({
      value: {
        payload: {
          rootActivity: {
            nodeId: "root",
            activityVersionId: "flowchart-v1",
            inputs: [],
            outputs: [],
            structure: {
              kind: "Flowchart",
              schemaVersion: "1.0.0",
              payload: {
                activities: [{
                  nodeId: "write-line-1",
                  activityVersionId: "write-line-v1",
                  inputs: [],
                  outputs: [],
                  structure: null
                }],
                connections: [],
                startNodeId: null,
                nodeMetadata: {},
                connectionMetadata: {}
              }
            }
          },
          variables: [],
          outputMappings: [],
          outcomeMappings: []
        },
        layout: []
      },
      contract: {
        contractSchemaVersion: "1",
        inputs: [{
          referenceKey: "customer-id",
          name: "CustomerId",
          displayName: "Customer ID",
          type: { alias: "String", collectionKind: "Single" }
        }],
        outputs: [],
        outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }]
      },
      expressionEditors: [inputReferenceContribution]
    });

    click(rendered.container.querySelector("[data-graph-node-id='write-line-1']")!);

    await vi.waitFor(() => {
      const picker = rendered.container.querySelector<HTMLSelectElement>("select[aria-label='Input reference']");
      expect([...picker!.options].map(option => option.textContent)).toContainEqual(expect.stringContaining("Customer ID"));
    });
  });

  it("keeps the shared inspector read-only when the Activity Definition host is locked", () => {
    catalogItems = [flowchartCatalogItem()];
    const rendered = renderDesigner({ readOnly: true });

    expect(rendered.container.querySelector<HTMLFieldSetElement>(".wf-inspector-tab-panels")?.disabled).toBe(true);
  });

  // The whole --wf-* token layer is scoped to this class. Without it every shared node/palette/
  // inspector rule is invalid at computed-value time and the designer renders as unstyled boxes.
  it("stamps the module token scope on the shared workspace", () => {
    catalogItems = [flowchartCatalogItem()];
    const rendered = renderDesigner();

    const workspace = rendered.container.querySelector("[data-graph-authoring-resource='activity-definition-graph']");
    expect(workspace?.classList.contains("wf-tokens")).toBe(true);
  });

  it("navigates scopes through the shared breadcrumb rather than a host-local copy", () => {
    catalogItems = [flowchartCatalogItem(), leafCatalogItem()];
    const rendered = renderDesigner();

    expect(rendered.container.querySelector(".wf-breadcrumb")).not.toBeNull();
    expect(rendered.container.querySelector(".ad-graph-breadcrumb")).toBeNull();
  });

  it("offers the shared empty-canvas picker and places the chosen activity", () => {
    catalogItems = [flowchartCatalogItem(), leafCatalogItem()];
    const onChange = vi.fn();
    const rendered = renderDesigner({ value: flowchartImplementationValue(flowchartStructureKind), onChange });

    click(buttonByText(rendered.container, "Add activity"));
    const option = [...rendered.container.querySelectorAll<HTMLButtonElement>(".wf-connect-menu [role='option']")]
      .find(candidate => candidate.querySelector("strong")?.textContent === "Write line");
    click(option!);

    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({
      payload: expect.objectContaining({
        rootActivity: expect.objectContaining({
          structure: expect.objectContaining({
            payload: expect.objectContaining({
              activities: [expect.objectContaining({ activityVersionId: "write-line-v1" })]
            })
          })
        })
      })
    }));
  });

  it("honours the cursor position when an activity is dropped onto the canvas", () => {
    catalogItems = [flowchartCatalogItem(), leafCatalogItem()];
    const onChange = vi.fn();
    const rendered = renderDesigner({ value: flowchartImplementationValue(flowchartStructureKind), onChange });

    const canvas = rendered.container.querySelector<HTMLElement>(".wf-canvas")!;
    stubRect(canvas, { left: 100, top: 100, right: 900, bottom: 700, width: 800, height: 600 });
    const dataTransfer = dragDataTransfer();
    dataTransfer.setData(activityDragDataType, "write-line-v1");
    dispatchDragEvent(canvas, "drop", { dataTransfer, clientX: 360, clientY: 300 });

    const layout = onChange.mock.calls.at(-1)?.[0].layout as { nodeId: string; x: number; y: number }[];
    expect(layout).toHaveLength(1);
    // The grid fallback would put the first node at (80, 80); a positioned drop must not land there.
    expect({ x: layout[0].x, y: layout[0].y }).not.toEqual({ x: 80, y: 80 });
  });
});

describe("ActivityGraphImplementationEditor BPMN slots", () => {
  // A BPMN scope renders from its process ELEMENTS, so an activity placed without a bound element has
  // no representation on the canvas and is dropped by the next syncBpmnCanvasToScope.
  it("binds a palette placement to a BPMN element", () => {
    catalogItems = [bpmnCatalogItem(), leafCatalogItem()];
    const onChange = vi.fn();
    const rendered = renderDesigner({ value: bpmnImplementationValue(), onChange });

    click(buttonByText(rendered.container, "Primitives1"));
    click(buttonByText(rendered.container, "Write line"));

    const payload = lastPayload(onChange);
    expect(payload.activities).toHaveLength(1);
    const placed = payload.elements.find(element => element.elementId !== "start");
    expect(placed?.elementType).toBe("task");
    expect(placed?.childNodeId).toBe(payload.activities[0].nodeId);
  });

  it("renders the existing BPMN elements rather than the slot activities", () => {
    catalogItems = [bpmnCatalogItem(), leafCatalogItem()];
    const rendered = renderDesigner({
      value: bpmnImplementationValue([
        { elementId: "start", elementType: "startEvent" },
        { elementId: "task-1", elementType: "task", childNodeId: "write-line-1" }
      ], [{ nodeId: "write-line-1", activityVersionId: "write-line-v1", inputs: [], outputs: [], structure: null }])
    });

    expect(rendered.container.querySelector("[data-graph-node-id='task-1']")).not.toBeNull();
    expect(rendered.container.querySelector("[data-graph-node-id='write-line-1']")).toBeNull();
  });

  it("drops the layout records of a deleted BPMN element and its bound activity", () => {
    catalogItems = [bpmnCatalogItem(), leafCatalogItem()];
    const onChange = vi.fn();
    const rendered = renderDesigner({
      value: {
        ...bpmnImplementationValue([
          { elementId: "start", elementType: "startEvent" },
          { elementId: "task-1", elementType: "task", childNodeId: "write-line-1" }
        ], [{ nodeId: "write-line-1", activityVersionId: "write-line-v1", inputs: [], outputs: [], structure: null }]),
        layout: [
          { nodeId: "start", data: { x: 0, y: 0 } },
          { nodeId: "task-1", data: { x: 200, y: 100 } },
          { nodeId: "write-line-1", data: { x: 200, y: 100 } }
        ]
      },
      onChange
    });

    click(rendered.container.querySelector("[data-graph-node-id='task-1']")!);
    confirmOnce();
    click(buttonByText(rendered.container, "Remove"));

    const layout = onChange.mock.calls.at(-1)?.[0].layout as Array<{ nodeId: string }>;
    expect(layout.map(record => record.nodeId)).toEqual(["start"]);
  });

  it("inspects the activity a selected BPMN element binds", () => {
    catalogItems = [bpmnCatalogItem(), leafCatalogItem()];
    const rendered = renderDesigner({
      value: bpmnImplementationValue([
        { elementId: "start", elementType: "startEvent" },
        { elementId: "task-1", elementType: "task", childNodeId: "write-line-1" }
      ], [{ nodeId: "write-line-1", activityVersionId: "write-line-v1", inputs: [], outputs: [], structure: null }])
    });

    click(rendered.container.querySelector("[data-graph-node-id='task-1']")!);

    expect(rendered.container.querySelector("[aria-label='Activity inspector sections']")?.textContent).toContain("Version");
    expect(buttonByText(rendered.container, "Move activity left", "aria-label").disabled).toBe(true);
  });

  // Without the shape palette the host can only bind activities to tasks, which ADR-0019 says is not a
  // BPMN process at all — events and gateways are what make the scope one.
  it("stamps a pure shape from the BPMN palette as an unbound element", () => {
    catalogItems = [bpmnCatalogItem(), leafCatalogItem()];
    const onChange = vi.fn();
    const rendered = renderDesigner({ value: bpmnImplementationValue(), onChange });

    click(buttonByText(rendered.container, "Exclusive gateway"));

    const payload = lastPayload(onChange);
    const gateway = payload.elements.find(element => element.elementType === "exclusiveGateway");
    expect(gateway).toBeDefined();
    expect(gateway?.childNodeId).toBeUndefined();
    // A shape binds nothing, so it must not invent a slot activity for syncBpmnCanvasToScope to keep.
    expect(payload.activities).toHaveLength(0);
    expect(payload.elements.map(element => element.elementId)).toContain("start");
  });

  // Slot badges are rendered by the shared BpmnElementNode, so this host gets subprocess navigation the
  // moment it emits bpmnElement nodes. The badge addresses the BOUND ACTIVITY node id; this host's
  // slotNavigation looks the owner up among the scope slot's activities, so an element id would find
  // nothing and silently no-op.
  it("descends into a bound subprocess from its slot badge", () => {
    catalogItems = [bpmnCatalogItem(), leafCatalogItem()];
    const rendered = renderDesigner({
      value: bpmnImplementationValue(
        [{ elementId: "sub-1", elementType: "subProcess", childNodeId: "nested-process" }],
        [{
          nodeId: "nested-process",
          activityVersionId: "bpmn-v1",
          inputs: [],
          outputs: [],
          structure: {
            kind: bpmnStructureKind,
            schemaVersion: "1.0.0",
            payload: {
              elements: [{ elementId: "inner-task", elementType: "task", childNodeId: "write-line-1" }],
              sequenceFlows: [],
              activities: [{ nodeId: "write-line-1", activityVersionId: "write-line-v1", inputs: [], outputs: [], structure: null }]
            }
          }
        }]
      )
    });

    // The parent scope shows the subprocess element, not the nested process.
    expect(rendered.container.querySelector("[data-graph-node-id='sub-1']")).not.toBeNull();
    expect(rendered.container.querySelector("[data-graph-node-id='inner-task']")).toBeNull();

    const badge = rendered.container.querySelector<HTMLButtonElement>(".wf-node-slot-badge");
    expect(badge).not.toBeNull();
    click(badge!);

    // The nested scope is itself a BPMN scope, so it renders from its own elements.
    expect(rendered.container.querySelector("[data-graph-node-id='inner-task']")).not.toBeNull();
    expect(rendered.container.querySelector("[data-graph-node-id='sub-1']")).toBeNull();
  });

  it("offers BPMN shapes only in a BPMN scope, and never on a locked draft", () => {
    catalogItems = [bpmnCatalogItem(), flowchartCatalogItem(), leafCatalogItem()];

    const flowchart = renderDesigner({ value: flowchartImplementationValue(flowchartStructureKind) });
    expect(flowchart.container.querySelector("[aria-label='BPMN shapes']")).toBeNull();

    const locked = renderDesigner({ value: bpmnImplementationValue(), readOnly: true });
    expect(buttonByText(locked.container, "Exclusive gateway").disabled).toBe(true);
  });

  it("inspects a pure structure element instead of falling back to the scope owner", () => {
    catalogItems = [bpmnCatalogItem(), leafCatalogItem()];
    const onChange = vi.fn();
    const rendered = renderDesigner({
      value: bpmnImplementationValue([{ elementId: "gateway-1", elementType: "exclusiveGateway" }]),
      onChange
    });

    click(rendered.container.querySelector("[data-graph-node-id='gateway-1']")!);

    // The activity inspector does not apply: the element binds no activity to inspect.
    expect(rendered.container.querySelector("[aria-label='Activity inspector sections']")).toBeNull();
    setInput(rendered.container.querySelector<HTMLInputElement>(".wf-inspector-content input[type='text']")!, "Approved?");

    expect(lastPayload(onChange).elements).toEqual([
      expect.objectContaining({ elementId: "gateway-1", name: "Approved?" })
    ]);
  });

  it("edits an outbound sequence flow's condition and default from the element inspector", () => {
    catalogItems = [bpmnCatalogItem(), leafCatalogItem()];
    const onChange = vi.fn();
    const rendered = renderDesigner({
      value: bpmnImplementationValue(
        [
          { elementId: "gateway-1", elementType: "exclusiveGateway" },
          { elementId: "end-1", elementType: "endEvent" }
        ],
        [],
        [{ flowId: "flow-1", sourceRef: "gateway-1", targetRef: "end-1" }]
      ),
      onChange
    });

    click(rendered.container.querySelector("[data-graph-node-id='gateway-1']")!);
    setInput(rendered.container.querySelector<HTMLInputElement>("input[aria-label='Condition outcome for flow to End event']")!, "Approved");

    expect(lastPayload(onChange).sequenceFlows).toEqual([
      expect.objectContaining({ flowId: "flow-1", conditionOutcome: "Approved" })
    ]);

    click(rendered.container.querySelector<HTMLInputElement>("input[aria-label='Default flow to End event']")!);

    const payload = lastPayload(onChange);
    // A default flow carries no condition, and the gateway names the flow it falls back to.
    expect(payload.sequenceFlows).toEqual([
      expect.objectContaining({ flowId: "flow-1", isDefault: true, conditionOutcome: null })
    ]);
    expect(payload.elements.find(element => element.elementId === "gateway-1")?.defaultFlowId).toBe("flow-1");
  });

  it("removes a pure structure element along with its sequence flows", () => {
    catalogItems = [bpmnCatalogItem(), leafCatalogItem()];
    const onChange = vi.fn();
    const rendered = renderDesigner({
      value: {
        ...bpmnImplementationValue(
          [
            { elementId: "start", elementType: "startEvent" },
            { elementId: "gateway-1", elementType: "exclusiveGateway" }
          ],
          [],
          [{ flowId: "flow-1", sourceRef: "start", targetRef: "gateway-1" }]
        ),
        layout: [
          { nodeId: "start", data: { x: 0, y: 0 } },
          { nodeId: "gateway-1", data: { x: 200, y: 0 } }
        ]
      },
      onChange
    });

    click(rendered.container.querySelector("[data-graph-node-id='gateway-1']")!);
    confirmOnce();
    click(buttonByText(rendered.container, "Remove"));

    const payload = lastPayload(onChange);
    expect(payload.elements.map(element => element.elementId)).toEqual(["start"]);
    expect(payload.sequenceFlows).toEqual([]);
    const layout = onChange.mock.calls.at(-1)?.[0].layout as Array<{ nodeId: string }>;
    expect(layout.map(record => record.nodeId)).toEqual(["start"]);
  });
});

describe("reconcileGraphLayout", () => {
  // Every commitRoot path — an inspector edit above all — rebuilds the layout through this function.
  // A BPMN scope positions its elements, so dropping records it does not recognise would scatter the
  // process back to the default grid on the next property change.
  it("keeps BPMN element positions alongside activity positions", () => {
    const root = {
      nodeId: "root",
      activityVersionId: "bpmn-v1",
      inputs: [],
      outputs: [],
      structure: {
        kind: bpmnStructureKind,
        schemaVersion: "1.0.0",
        payload: {
          elements: [
            { elementId: "start", elementType: "startEvent" },
            { elementId: "task-1", elementType: "task", childNodeId: "write-line-1" }
          ],
          sequenceFlows: [],
          activities: [{ nodeId: "write-line-1", activityVersionId: "write-line-v1", inputs: [], outputs: [], structure: null }]
        }
      }
    } as unknown as Parameters<typeof reconcileGraphLayout>[0];

    const reconciled = reconcileGraphLayout(
      root,
      new Map([["bpmn-v1", bpmnCatalogItem()], ["write-line-v1", leafCatalogItem()]] as never),
      [
        { nodeId: "start", x: 10, y: 20 },
        { nodeId: "task-1", x: 300, y: 400 }
      ]
    );

    expect(reconciled.find(record => record.nodeId === "task-1")).toMatchObject({ x: 300, y: 400 });
    expect(reconciled.find(record => record.nodeId === "start")).toMatchObject({ x: 10, y: 20 });
  });

  it("drops records for activities that are no longer in the graph", () => {
    const root = {
      nodeId: "root",
      activityVersionId: "flowchart-v1",
      inputs: [],
      outputs: [],
      structure: {
        kind: flowchartStructureKind,
        schemaVersion: "1.0.0",
        payload: { activities: [], connections: [], startNodeId: null, nodeMetadata: {}, connectionMetadata: {} }
      }
    } as unknown as Parameters<typeof reconcileGraphLayout>[0];

    expect(reconcileGraphLayout(root, new Map(), [{ nodeId: "gone", x: 1, y: 2 }])).toEqual([]);
  });
});

describe("ActivityGraphImplementationEditor boundary output mappings", () => {
  it("requires a mapping for required outputs and adds one through a shared expression editor", async () => {
    expressionDescriptors = [{ type: "Input", displayName: "Public input", editingMode: "reference" }];
    const onChange = vi.fn();
    const rendered = renderEditor({
      contract: {
        contractSchemaVersion: "1",
        inputs: [{
          referenceKey: "customer-id",
          name: "CustomerId",
          displayName: "Customer ID",
          type: { alias: "String", collectionKind: "Single" }
        }],
        outputs: [{
          referenceKey: "result",
          name: "Result",
          displayName: "Result",
          type: { alias: "String", collectionKind: "Single" },
          isRequired: true
        }],
        outcomes: [{ referenceKey: "approved-boundary", name: "Approved", isEmitted: true }]
      },
      expressionEditors: [inputExpressionEditor],
      onChange
    });

    expect(rendered.container.textContent).toContain("A required public output needs exactly one boundary expression.");
    await vi.waitFor(() => expect(buttonByText(rendered.container, "Add expression").disabled).toBe(false));
    click(buttonByText(rendered.container, "Add expression"));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      payload: expect.objectContaining({
        outputMappings: [{
          outputReferenceKey: "result",
          source: {
            syntax: "Input",
            value: { referenceKey: "customer-id" }
          }
        }]
      })
    }));
  });

  it("allows an optional output to remain unmapped", () => {
    const rendered = renderEditor({
      contract: {
        contractSchemaVersion: "1",
        inputs: [],
        outputs: [{
          referenceKey: "note",
          name: "Note",
          type: { alias: "String", collectionKind: "Single" },
          isRequired: false
        }],
        outcomes: [{ referenceKey: "approved-boundary", name: "Approved", isEmitted: true }]
      }
    });

    expect(rendered.container.textContent).toContain("No boundary expression is configured.");
    expect(rendered.container.textContent).not.toContain("A required public output needs exactly one boundary expression.");
  });
});

const inputExpressionEditor: StudioExpressionEditorContribution = {
  id: "test.input",
  supports: context => context.syntax === "Input",
  surfaces: {
    inline: ({ value, onChange, disabled }) => <button type="button" disabled={disabled} onClick={() => onChange({ referenceKey: "customer-id" })}>{JSON.stringify(value)}</button>
  },
  createDefaultValue: () => ({ referenceKey: "customer-id" })
};

function renderEditor(overrides: Partial<StudioActivityDefinitionImplementationEditorProps> = {}) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  const props: StudioActivityDefinitionImplementationEditorProps = {
    context: {} as StudioActivityDefinitionImplementationEditorProps["context"],
    definitionId: "definition-1",
    draftId: "draft-1",
    revision: 1,
    providerKey: "elsa.activity-graph",
    providerSchemaVersion: "2",
    manifestFingerprint: "sha256:test",
    contract: {
      contractSchemaVersion: "1",
      outcomes: [
        { referenceKey: "approved-boundary", name: "Approved", isEmitted: true },
        { referenceKey: "declined-boundary", name: "Declined", isEmitted: true },
        { referenceKey: "draft-boundary", name: "Draft", isEmitted: false }
      ]
    },
    value: implementationValue(),
    readOnly: false,
    onChange: () => {},
    ...overrides
  };
  flushSync(() => root.render(<ActivityGraphPublicInterfaceEditor {...props} />));
  const rendered = { root, container };
  mounted.push(rendered);
  return rendered;
}

function renderDesigner(overrides: Partial<StudioActivityDefinitionImplementationEditorProps> = {}) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  const props: StudioActivityDefinitionImplementationEditorProps = {
    context: {
      baseUrl: "test://activity-graph",
      http: {
        getJson: vi.fn(async () => ({ items: [] }))
      }
    } as unknown as StudioActivityDefinitionImplementationEditorProps["context"],
    definitionId: "definition-1",
    draftId: "draft-1",
    revision: 1,
    providerKey: "elsa.activity-graph",
    providerSchemaVersion: "2",
    manifestFingerprint: "sha256:test",
    contract: {
      contractSchemaVersion: "1",
      inputs: [],
      outputs: [],
      outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }]
    },
    propertyEditors: [],
    expressionEditors: [],
    graphAuthoringPanels: [],
    historyResetKey: "draft-1:active",
    value: flowchartImplementationValue(),
    readOnly: false,
    onChange: () => {},
    ...overrides
  };
  flushSync(() => root.render(<ActivityGraphImplementationEditor {...props} />));
  const rendered = { root, container };
  mounted.push(rendered);
  return rendered;
}

function implementationValue(payload: Record<string, unknown> = {}) {
  return {
    payload: {
      rootActivity: {
        nodeId: "root",
        activityVersionId: "decision-v1",
        inputs: [],
        outputs: [],
        structure: null
      },
      variables: [],
      outputMappings: [],
      outcomeMappings: [],
      ...payload
    },
    layout: []
  };
}

function catalogItem() {
  return {
    activityVersionId: "decision-v1",
    activityTypeKey: "acme.decision",
    version: "1.0.0",
    category: "Tests",
    displayName: "Decision",
    executionType: "Action",
    inputs: [],
    outputs: [],
    ports: [
      { type: "outcome", referenceKey: "approved", displayName: "Approved" },
      { type: "outcome", referenceKey: "rejected", displayName: "Rejected" },
      { type: "flow", name: "unstable-name-only", displayName: "Unstable" }
    ]
  };
}

function flowchartCatalogItem() {
  return {
    activityVersionId: "flowchart-v1",
    activityTypeKey: "Elsa.Flowchart",
    version: "1.0.0",
    category: "Composition",
    displayName: "Flowchart",
    executionType: "Action",
    inputs: [],
    outputs: [],
    ports: []
  };
}

function leafCatalogItem() {
  return {
    activityVersionId: "write-line-v1",
    activityTypeKey: "Elsa.WriteLine",
    version: "1.0.0",
    category: "Primitives",
    displayName: "Write line",
    executionType: "Action",
    inputs: [],
    outputs: [],
    ports: []
  };
}

function inputLeafCatalogItem() {
  return {
    ...leafCatalogItem(),
    inputs: [{
      name: "Message",
      typeName: "System.String",
      displayName: "Message",
      description: "Message to write.",
      isBrowsable: true,
      isWrapped: true,
      defaultSyntax: "Input"
    }]
  };
}

// `kind` defaults to the plain label used by the older fixtures (which resolves to a generic slot);
// pass `flowchartStructureKind` when the test needs the real flowchart editing model.
function flowchartImplementationValue(kind = "Flowchart") {
  return {
    payload: {
      rootActivity: {
        nodeId: "root",
        activityVersionId: "flowchart-v1",
        inputs: [],
        outputs: [],
        structure: {
          kind,
          schemaVersion: "1.0.0",
          payload: {
            activities: [],
            connections: [],
            startNodeId: null,
            nodeMetadata: {},
            connectionMetadata: {}
          }
        }
      },
      variables: [],
      outputMappings: [],
      outcomeMappings: []
    },
    layout: []
  };
}

// deleteSelected guards on window.confirm; approve exactly one prompt.
function confirmOnce() {
  const original = window.confirm;
  window.confirm = () => {
    window.confirm = original;
    return true;
  };
}

function bpmnCatalogItem() {
  return {
    activityVersionId: "bpmn-v1",
    activityTypeKey: "Elsa.BpmnProcess",
    version: "1.0.0",
    category: "Composition",
    displayName: "BPMN Process",
    executionType: "Action",
    inputs: [],
    outputs: [],
    ports: []
  };
}

function bpmnImplementationValue(
  elements: Array<Record<string, unknown>> = [{ elementId: "start", elementType: "startEvent" }],
  activities: Array<Record<string, unknown>> = [],
  sequenceFlows: Array<Record<string, unknown>> = []
) {
  return {
    payload: {
      rootActivity: {
        nodeId: "root",
        activityVersionId: "bpmn-v1",
        inputs: [],
        outputs: [],
        structure: {
          kind: bpmnStructureKind,
          schemaVersion: "1.0.0",
          payload: { elements, sequenceFlows, activities }
        }
      },
      variables: [],
      outputMappings: [],
      outcomeMappings: []
    },
    layout: []
  };
}

function lastPayload(onChange: ReturnType<typeof vi.fn>) {
  const structure = onChange.mock.calls.at(-1)?.[0].payload.rootActivity.structure.payload;
  return {
    elements: (structure.elements ?? []) as Array<{ elementId: string; elementType: string; name?: string | null; childNodeId?: string; defaultFlowId?: string | null }>,
    activities: (structure.activities ?? []) as Array<{ nodeId: string }>,
    sequenceFlows: (structure.sequenceFlows ?? []) as Array<{ flowId: string; conditionOutcome?: string | null; isDefault?: boolean }>
  };
}

function setInput(element: HTMLInputElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

function dragDataTransfer() {
  const values = new Map<string, string>();
  return {
    effectAllowed: "all",
    dropEffect: "none",
    setData: (type: string, value: string) => values.set(type, value),
    getData: (type: string) => values.get(type) ?? "",
    clearData: (type?: string) => {
      if (type) values.delete(type);
      else values.clear();
    }
  };
}

function dispatchDragEvent(
  element: Element,
  type: string,
  options: { dataTransfer: ReturnType<typeof dragDataTransfer>; clientX: number; clientY: number }
) {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperties(event, {
    dataTransfer: { value: options.dataTransfer },
    clientX: { value: options.clientX },
    clientY: { value: options.clientY }
  });
  flushSync(() => element.dispatchEvent(event));
}

function stubRect(element: HTMLElement, rect: Partial<DOMRect>) {
  element.getBoundingClientRect = () => ({
    x: rect.left ?? 0,
    y: rect.top ?? 0,
    left: rect.left ?? 0,
    top: rect.top ?? 0,
    right: rect.right ?? 0,
    bottom: rect.bottom ?? 0,
    width: rect.width ?? 0,
    height: rect.height ?? 0,
    toJSON: () => ({})
  } as DOMRect);
}

function buttonByText(container: HTMLElement, text: string, attribute?: string) {
  const button = [...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate =>
    attribute ? candidate.getAttribute(attribute) === text : candidate.textContent?.trim() === text);
  if (!button) throw new Error(`Button not found: ${text}`);
  return button;
}

function click(element: Element) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function change(element: HTMLSelectElement, value: string) {
  flushSync(() => {
    element.value = value;
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
}
