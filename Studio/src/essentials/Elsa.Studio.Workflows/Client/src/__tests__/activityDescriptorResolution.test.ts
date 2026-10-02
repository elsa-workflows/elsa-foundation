import { describe, expect, it } from "vitest";
import { indexActivityDescriptors, resolveActivityDescriptor } from "../workflow-editor/editorHelpers";
import { toActivityDescriptor } from "../workflow-editor/useWorkflowEditorData";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";

// Two versions of one package-loaded activity in the catalog: release 1.1.0 of its package added an input.
const text = { referenceKey: "Text", name: "Text", type: "String" };
const tags = { referenceKey: "Tags", name: "Tags", type: "String" };
const version = (activityVersionId: string, versionNumber: string, inputs: unknown[]): ActivityCatalogItem => ({
  activityVersionId,
  activityTypeKey: "Elsa.Samples.Nuplane.Notes.Activities.AddNote",
  version: versionNumber,
  category: "Notes",
  displayName: "Add note",
  executionType: "Action",
  inputs,
  outputs: []
});
const catalog = [version("add-note-1.0.0", "1.0.0", [text]), version("add-note-1.1.0", "1.1.0", [text, tags])];
const node = (activityVersionId: string) => ({ nodeId: "node", activityVersionId, inputs: [], outputs: [] }) as unknown as ActivityNode;

function resolve(activityVersionId: string) {
  const catalogByVersion = new Map(catalog.map(item => [item.activityVersionId, item]));
  const descriptorsByType = indexActivityDescriptors(catalog.map(toActivityDescriptor));
  const descriptorsByVersion = new Map(catalog.map(item => [item.activityVersionId, toActivityDescriptor(item)]));
  return resolveActivityDescriptor(node(activityVersionId), catalogByVersion, descriptorsByType, descriptorsByVersion);
}

describe("activity descriptor resolution", () => {
  it("inspects a node with the inputs of the exact version it is pinned to", () => {
    expect(resolve("add-note-1.1.0")?.inputs).toEqual([text, tags]);
    expect(resolve("add-note-1.0.0")?.inputs).toEqual([text]);
  });
});
