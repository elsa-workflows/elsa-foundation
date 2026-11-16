import type { ReactNode } from "react";
import type { Node, XYPosition } from "@xyflow/react";
import type { ActivityPaletteGroup } from "../workflow-editor/editorTypes";

/**
 * A designer-owned structural element, not an activity catalog entry. A designer supplies the
 * elements supported by its current scope, their notation and creation logic. Its document adapter
 * persists the created node's data. Neither the shared palette nor its drag handling knows BPMN (or
 * any future element family, such as Flowchart annotations).
 */
export interface GraphPaletteElement {
  /** Namespaced identity, unique among the current designer's contributions. */
  id: string;
  label: string;
  category: string;
  description?: string;
  keywords?: readonly string[];
  icon: ReactNode;
  createNode(position: XYPosition): Node;
}

export const graphElementDragDataType = "application/elsa-graph-element";
export const emptyGraphElements: readonly GraphPaletteElement[] = [];

/** Merge contributions into real categories; searching applies equally to every element family. */
export function mergeGraphPaletteGroups(
  activityGroups: ActivityPaletteGroup[],
  elements: readonly GraphPaletteElement[],
  search: string
) {
  const groups = new Map(activityGroups.map(group => [group.category, { ...group, elements: [] as GraphPaletteElement[] }]));
  const term = search.trim().toLocaleLowerCase();
  for (const element of elements) {
    if (term && ![element.label, element.category, element.description ?? "", ...(element.keywords ?? [])]
      .some(value => value.toLocaleLowerCase().includes(term))) continue;
    const group = groups.get(element.category) ?? { category: element.category, activities: [], elements: [] };
    group.elements.push(element);
    groups.set(element.category, group);
  }
  return [...groups.values()].sort((a, b) => a.category.localeCompare(b.category));
}
