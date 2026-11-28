import { resolveActivityIcon, resolveActivityIconFallback, resolveActivityIconTone } from "../activityIcons";
import type { WorkflowNodeData } from "../workflowAdapter";

/** Shared icon, fallback and tone for workflow activity nodes across canvas surfaces. */
export function resolveNodeActivityVisual(nodeData: WorkflowNodeData) {
  const source = {
    activityTypeKey: nodeData.activityTypeKey ?? "",
    displayName: nodeData.label,
    category: nodeData.category ?? "",
    executionType: nodeData.executionType ?? "",
    icon: nodeData.catalogIcon,
    iconName: nodeData.catalogIconName,
    activityDefinitionId: nodeData.activityDefinitionId
  };
  return {
    icon: nodeData.icon ?? resolveActivityIcon(source),
    fallback: resolveActivityIconFallback(source),
    tone: nodeData.iconTone ?? resolveActivityIconTone(source)
  };
}
