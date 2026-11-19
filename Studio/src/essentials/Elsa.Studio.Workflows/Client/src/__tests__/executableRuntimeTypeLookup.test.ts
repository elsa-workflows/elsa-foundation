import { describe, expect, it } from "vitest";
import { buildExecutableActivityGraph, isGhostFact } from "../executableGraph";
import type { ActivityCatalogItem } from "../workflowTypes";

// A CLR activity whose catalog key is its CLR full name but whose compiled nodes carry the declared runtime type.
const catalogItem = (overrides: Partial<ActivityCatalogItem> = {}): ActivityCatalogItem => ({
  activityVersionId: "httpendpoint-v1",
  activityTypeKey: "Elsa.Activities.Http.Activities.HttpEndpoint",
  version: "1.1.0",
  category: "Http",
  displayName: "Http Endpoint",
  executionType: "Action",
  inputs: [],
  outputs: [],
  ...overrides
});

const endpointNode = (activityType: string) => ({
  executableNodeId: "endpoint",
  authoredActivityId: "endpoint",
  activityType,
  activityTypeVersion: "1.1.0"
});

describe("executable lookup by declared runtime type", () => {
  it("resolves a node stamped with the declared runtime type to its catalog row", () => {
    const graph = buildExecutableActivityGraph(
      endpointNode("Elsa.HttpEndpoint") as never,
      [catalogItem({ runtimeActivityType: "Elsa.HttpEndpoint" })]
    );

    expect(graph.root.activityVersionId).toBe("httpendpoint-v1");
    expect(isGhostFact(graph.factsByNodeId.get("endpoint"))).toBe(false);
  });

  it("keeps the node a ghost when the catalog row declares no runtime type", () => {
    const graph = buildExecutableActivityGraph(
      endpointNode("Elsa.HttpEndpoint") as never,
      [catalogItem()]
    );

    expect(isGhostFact(graph.factsByNodeId.get("endpoint"))).toBe(true);
  });

  it("still resolves a node stamped with the catalog key itself", () => {
    const graph = buildExecutableActivityGraph(
      endpointNode("Elsa.Activities.Http.Activities.HttpEndpoint") as never,
      [catalogItem({ runtimeActivityType: "Elsa.HttpEndpoint" })]
    );

    expect(graph.root.activityVersionId).toBe("httpendpoint-v1");
  });
});
