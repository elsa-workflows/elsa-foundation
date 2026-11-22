import { readWrappedInput } from "./activityProperties";
import { getChildSlots } from "./workflowAdapter";
import type { ActivityCatalogItem, ActivityNode } from "./workflowTypes";

export function getActivityGraphOutputSetters(root: ActivityNode, catalog: Map<string, ActivityCatalogItem>) {
  const setters: { nodeId: string; outputName: string | null }[] = [];
  const visited = new Set<string>();
  const visit = (node: ActivityNode) => {
    if (visited.has(node.nodeId)) return;
    visited.add(node.nodeId);
    if (node.intrinsic?.kind.toLowerCase() === "setoutput") {
      const name = readWrappedInput(node, { name: "Name", referenceKey: "name", typeName: "String" });
      setters.push({ nodeId: node.nodeId, outputName: name.expression.type === "Literal" && typeof name.expression.value === "string" ? name.expression.value : null });
    }
    for (const slot of getChildSlots(node, catalog)) for (const child of slot.activities) visit(child);
  };
  visit(root);
  return setters;
}
