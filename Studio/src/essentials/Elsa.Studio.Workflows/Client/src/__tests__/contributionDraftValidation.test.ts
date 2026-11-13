import { describe, expect, it } from "vitest";
import {
  collectContributionDraftValidation,
  contributionDraftValidationCollectors,
  contributionDraftValidationSignature
} from "../contributionDraftValidation";
import type { ActivityNode } from "../workflowTypes";
import { flowchartStructureKind } from "../flowchartStartNode";

function node(nodeId: string, extra: Record<string, unknown> = {}): ActivityNode {
  return { nodeId, activityVersionId: `${nodeId}-v`, inputs: [], outputs: [], ...extra };
}

describe("contributionDraftValidation", () => {
  it("walks nested flowchart activities and maps collector issues onto ValidationError", async () => {
    const child = node("eval-1", { Decision: { expression: { type: "Literal", value: "approval@v1" } } });
    const root = node("root", {
      structure: {
        kind: flowchartStructureKind,
        schemaVersion: "1",
        payload: { activities: [child] }
      }
    });
    const errors = await collectContributionDraftValidation(root, [
      async activity => typeof activity === "object" && activity !== null && "Decision" in activity
        ? [{ type: "Decision/InputMembership", message: "notes is not in this table.", path: "eval-1/inputs/Inputs" }]
        : []
    ]);
    expect(errors).toEqual([{
      type: "Decision/InputMembership",
      code: "Decision/InputMembership",
      message: "notes is not in this table.",
      path: "eval-1/inputs/Inputs",
      severity: "Error"
    }]);
    expect(contributionDraftValidationSignature(root)).toContain("eval-1");
    expect(contributionDraftValidationCollectors(
      [{ id: "decisions.version", supports: () => true, component: () => null, collectDraftValidation: async () => [] }],
      []
    )).toHaveLength(1);
  });

  it("invokes a Decision collector only on catalog nodes its editor supports", async () => {
    const seen: string[] = [];
    const collectors = contributionDraftValidationCollectors(
      [{
        id: "decisions.version",
        supports: descriptor => descriptor.uiHint === "decision-version",
        component: () => null,
        collectDraftValidation: async activity => {
          const nodeId = (activity as { nodeId: string }).nodeId;
          seen.push(nodeId);
          return [{ type: "Decision/Hit", message: "evaluate", path: `${nodeId}/inputs/Decision` }];
        }
      }],
      []
    );
    const evaluate = node("eval-1", {
      Decision: { expression: { type: "Literal", value: "approval@latest" } }
    });
    const notify = node("notify-1", {
      Decision: { expression: { type: "Literal", value: "ops@example.com" } }
    });
    const root = node("root", {
      structure: {
        kind: flowchartStructureKind,
        schemaVersion: "1",
        payload: { activities: [evaluate, notify] }
      }
    });
    const catalog = new Map([
      ["eval-1-v", {
        activityVersionId: "eval-1-v",
        activityTypeKey: "Elsa.Decisions.Workflows.Runtime.Activities.EvaluateDecision",
        version: "1.0.0",
        category: "Decisions",
        displayName: "Evaluate Decision",
        executionType: "Action",
        inputs: [
          { name: "Decision", referenceKey: "Decision", typeName: "System.String", uiHint: "decision-version" },
          { name: "Inputs", referenceKey: "Inputs", typeName: "Elsa.Decisions.Core.DecisionValue" }
        ],
        outputs: []
      }],
      ["notify-1-v", {
        activityVersionId: "notify-1-v",
        activityTypeKey: "Contoso.Notify",
        version: "1.0.0",
        category: "Custom",
        displayName: "Notify",
        executionType: "Action",
        inputs: [{ name: "Decision", referenceKey: "Decision", typeName: "System.String" }],
        outputs: []
      }]
    ]);
    const errors = await collectContributionDraftValidation(root, collectors, catalog);
    expect(seen).toEqual(["eval-1"]);
    expect(errors.map(error => error.path)).toEqual(["eval-1/inputs/Decision"]);
  });
});
