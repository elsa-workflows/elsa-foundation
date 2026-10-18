import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { NodeProps } from "@xyflow/react";
import { HttpEndpointResponseWarning } from "../HttpEndpointResponseWarning";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { findHttpEndpointResponseWarning } from "../httpEndpointResponseGuidance";
import { getHttpEndpointResponseMode, withHttpEndpointResponseMode } from "../httpEndpointResponseMode";
import { sequenceStructureKind } from "../workflowAdapter";
import { WorkflowActivityNode } from "../workflow-editor/graph";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";

vi.mock("@xyflow/react", async importOriginal => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return { ...actual, Handle: () => null };
});

const endpointCatalog = catalogItem("endpoint", "Elsa.Activities.Http.Activities.HttpEndpoint", "HTTP Endpoint", [
  {
    name: "ResponseMode",
    referenceKey: "ResponseMode",
    typeName: "Elsa.Activities.Http.ResponseMode",
    isWrapped: true,
    defaultSyntax: "Literal",
    defaultValue: "Sync",
    uiSpecifications: {
      options: [
        { label: "Wait for workflow response", value: "Sync" },
        { label: "Accept and run in background", value: "Async" }
      ]
    }
  }
]);
const responseCatalog = catalogItem("response", "Elsa.Activities.Http.Activities.WriteHttpResponse", "Write HTTP Response");
const sequenceCatalog = catalogItem("sequence", "Elsa.Activities.Sequence.Activities.Sequence", "Sequence");

function catalogItem(versionId: string, activityTypeKey: string, displayName: string, inputs: unknown[] = []): ActivityCatalogItem {
  return {
    activityVersionId: versionId,
    activityTypeKey,
    version: "1.0.0",
    category: "HTTP",
    displayName,
    executionType: "Action",
    inputs,
    outputs: []
  };
}

function node(nodeId: string, activityVersionId: string, props: Record<string, unknown> = {}): ActivityNode {
  return { nodeId, activityVersionId, inputs: [], outputs: [], ...props } as ActivityNode;
}

function endpoint(mode: string, expressionType = "Literal") {
  return node("endpoint-node", endpointCatalog.activityVersionId, {
    ResponseMode: {
      typeName: "Elsa.Activities.Http.ResponseMode",
      expression: { type: expressionType, value: mode },
      conversion: { mode: "keep-me" }
    }
  });
}

function sequence(children: ActivityNode[]): ActivityNode {
  return node("sequence-node", sequenceCatalog.activityVersionId, {
    structure: {
      kind: sequenceStructureKind,
      schemaVersion: "1.0.0",
      payload: { activities: children }
    }
  });
}

const catalog = [endpointCatalog, responseCatalog, sequenceCatalog];

describe("HTTP endpoint response guidance", () => {
  it("uses the endpoint contract default and preserves wrapped input metadata when switching to Sync", () => {
    const missingMode = node("endpoint-node", endpointCatalog.activityVersionId);
    expect(getHttpEndpointResponseMode(missingMode, endpointCatalog)).toBe("Sync");

    const asyncEndpoint = endpoint("Async");
    const updated = withHttpEndpointResponseMode(asyncEndpoint, endpointCatalog, "Sync");
    expect(updated.ResponseMode).toEqual({
      typeName: "Elsa.Activities.Http.ResponseMode",
      expression: { type: "Literal", value: "Sync" },
      conversion: { mode: "keep-me" }
    });
  });

  it("reads and updates a plain enum descriptor through imported canonical and expanded inputs", () => {
    const plainEnumCatalog = catalogItem("endpoint", endpointCatalog.activityTypeKey, "HTTP Endpoint", [
      {
        ...(endpointCatalog.inputs[0] as Record<string, unknown>),
        isWrapped: false
      }
    ]);
    const wireEndpoint = node("endpoint-node", plainEnumCatalog.activityVersionId, {
      inputs: [{
        referenceKey: "ResponseMode",
        value: { expressionType: "Literal", value: "Async" },
        conversion: { profile: "preserve-me" },
        autoEvaluate: true,
        evaluatorType: "preserve-me-too"
      }]
    });

    expect(getHttpEndpointResponseMode(wireEndpoint, plainEnumCatalog)).toBe("Async");

    const expandedEndpoint = expandStateFromWire({ rootActivity: wireEndpoint }).rootActivity!;
    expect(getHttpEndpointResponseMode(expandedEndpoint, plainEnumCatalog)).toBe("Async");
    const updatedEndpoint = withHttpEndpointResponseMode(expandedEndpoint, plainEnumCatalog, "Sync");
    const savedEndpoint = canonicalizeStateForWire({ rootActivity: updatedEndpoint }).rootActivity!;
    expect(savedEndpoint.inputs).toEqual([{
      referenceKey: "ResponseMode",
      value: { expressionType: "Literal", value: "Sync" },
      conversion: { profile: "preserve-me" },
      autoEvaluate: true,
      evaluatorType: "preserve-me-too"
    }]);

    const updatedCanonicalEndpoint = withHttpEndpointResponseMode(wireEndpoint, plainEnumCatalog, "Sync");
    const savedCanonicalEndpoint = canonicalizeStateForWire({ rootActivity: updatedCanonicalEndpoint }).rootActivity!;
    expect(savedCanonicalEndpoint.inputs).toEqual(savedEndpoint.inputs);
  });

  it("warns only when an async endpoint can reach WriteHttpResponse in its Sequence", () => {
    const asyncEndpoint = endpoint("Async");
    const writer = node("writer-node", responseCatalog.activityVersionId);
    const warning = findHttpEndpointResponseWarning(sequence([asyncEndpoint, writer]), asyncEndpoint.nodeId, catalog);
    expect(warning).toEqual({ endpoint: asyncEndpoint, responseActivity: writer });

    expect(findHttpEndpointResponseWarning(sequence([writer, asyncEndpoint]), asyncEndpoint.nodeId, catalog)).toBeNull();
    expect(findHttpEndpointResponseWarning(sequence([endpoint("Sync"), writer]), "endpoint-node", catalog)).toBeNull();
  });

  it("follows the actual Flowchart connection graph and leaves unresolved modes alone", () => {
    const asyncEndpoint = endpoint("Async");
    const writer = node("writer-node", responseCatalog.activityVersionId);
    const other = node("other-node", sequenceCatalog.activityVersionId);
    const connected = node("flowchart", "flowchart", {
      structure: {
        kind: "elsa.flowchart.structure",
        schemaVersion: "1.0.0",
        payload: {
          activities: [asyncEndpoint, other, writer],
          startNodeId: asyncEndpoint.nodeId,
          connections: [{
            id: "endpoint-to-writer",
            source: { nodeId: asyncEndpoint.nodeId, port: "Done" },
            target: { nodeId: writer.nodeId }
          }]
        }
      }
    });
    const flowchartCatalog = catalogItem("flowchart", "Elsa.Activities.Flowchart.Activities.Flowchart", "Flowchart");
    const flowchartResult = findHttpEndpointResponseWarning(connected, asyncEndpoint.nodeId, [...catalog, flowchartCatalog]);
    expect(flowchartResult?.responseActivity.nodeId).toBe(writer.nodeId);

    const disconnected = {
      ...connected,
      structure: {
        ...connected.structure!,
        payload: { ...connected.structure!.payload, connections: [] }
      }
    };
    expect(findHttpEndpointResponseWarning(disconnected, asyncEndpoint.nodeId, [...catalog, flowchartCatalog])).toBeNull();

    const expressionEndpoint = endpoint("Async", "JavaScript");
    expect(getHttpEndpointResponseMode(expressionEndpoint, endpointCatalog)).toBeNull();
    expect(findHttpEndpointResponseWarning(sequence([expressionEndpoint, writer]), expressionEndpoint.nodeId, catalog)).toBeNull();
  });

  it("shows the non-blocking correction action beside a matching endpoint", () => {
    const asyncEndpoint = endpoint("Async");
    const onUseSync = vi.fn();
    const host = document.createElement("div");
    document.body.appendChild(host);
    const root: Root = createRoot(host);
    flushSync(() => root.render(
      <HttpEndpointResponseWarning
        rootActivity={sequence([asyncEndpoint, node("writer-node", responseCatalog.activityVersionId)])}
        endpointNodeId={asyncEndpoint.nodeId}
        catalog={catalog}
        onUseSync={onUseSync}
      />
    ));

    expect(host.querySelector('[role="note"]')?.textContent).toContain("This response will be stored; it won’t be returned to the caller.");
    flushSync(() => host.querySelector("button")?.click());
    expect(onUseSync).toHaveBeenCalledOnce();

    flushSync(() => root.unmount());
    host.remove();
  });

  it("renders the effective response behavior on the endpoint canvas node", () => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const root: Root = createRoot(host);
    flushSync(() => root.render(
      <WorkflowActivityNode {...({
        id: "endpoint-node",
        selected: false,
        data: {
          label: "HTTP Endpoint",
          activityVersionId: endpointCatalog.activityVersionId,
          activityTypeKey: endpointCatalog.activityTypeKey,
          childSlots: [],
          acceptsInbound: true,
          sourcePorts: [],
          suppressFlowPorts: true,
          httpEndpointResponseMode: "Async"
        }
      } as unknown as NodeProps)} />
    ));

    expect(host.querySelector('[aria-label="HTTP endpoint behavior: Runs in background"]')?.textContent).toBe("Runs in background");
    flushSync(() => root.unmount());
    host.remove();
  });
});

afterEach(() => {
  document.body.innerHTML = "";
});
