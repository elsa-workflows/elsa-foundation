import { Boxes, GitBranch, ListTree, Play, Terminal, Zap } from "lucide-react";
import type { WorkflowNodeData } from "./workflowAdapter";

/** Shared presentational formatters used across the workflow editor and instance views. */

export function formatDate(value?: string | null) {
  if (!value) return "";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

export function formatDuration(start?: string | null, end?: string | null) {
  if (!start || !end) return "";

  const startTime = Date.parse(start);
  const endTime = Date.parse(end);
  if (Number.isNaN(startTime) || Number.isNaN(endTime) || endTime < startTime) return "";

  const totalSeconds = Math.round((endTime - startTime) / 1000);
  if (totalSeconds < 60) return `${totalSeconds}s`;

  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (minutes < 60) return seconds ? `${minutes}m ${seconds}s` : `${minutes}m`;

  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;
  return remainingMinutes ? `${hours}h ${remainingMinutes}m` : `${hours}h`;
}

export function shortTypeName(key: string | null | undefined) {
  return key?.split(".").filter(Boolean).at(-1);
}

export function renderActivityIcon(icon: WorkflowNodeData["icon"]) {
  switch (icon) {
    case "flowchart":
      return <GitBranch size={15} />;
    case "sequence":
      return <ListTree size={15} />;
    case "terminal":
      return <Terminal size={15} />;
    case "runtime":
      return <Play size={15} />;
    case "trigger":
      return <Zap size={15} />;
    default:
      return <Boxes size={15} />;
  }
}
