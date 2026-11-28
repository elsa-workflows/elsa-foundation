import { describe, expect, it } from "vitest";
import type { StudioWorkflowDesignerPanelContribution } from "@elsa-workflows/studio-sdk";
import { filterGraphAuthoringContributions } from "../graph-authoring/graphAuthoringContributions";

const component = () => null;

describe("graph authoring contribution resource scope", () => {
  const contributions: StudioWorkflowDesignerPanelContribution[] = [
    { id: "legacy", title: "Legacy", side: "left", component },
    {
      id: "workflow",
      title: "Workflow",
      side: "left",
      supportedResourceKinds: ["workflow-definition"],
      component
    },
    {
      id: "activity",
      title: "Activity",
      side: "right",
      supportedResourceKinds: ["activity-definition-graph"],
      component
    },
    {
      id: "shared",
      title: "Shared",
      side: "right",
      supportedResourceKinds: ["workflow-definition", "activity-definition-graph"],
      component
    }
  ];

  it("keeps legacy panels workflow-only and includes explicitly shared panels", () => {
    expect(filterGraphAuthoringContributions(contributions, "workflow-definition").map(item => item.id))
      .toEqual(["legacy", "workflow", "shared"]);
  });

  it("requires explicit activity-definition-graph support", () => {
    expect(filterGraphAuthoringContributions(contributions, "activity-definition-graph").map(item => item.id))
      .toEqual(["activity", "shared"]);
  });
});
