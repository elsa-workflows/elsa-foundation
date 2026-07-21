import { Boxes, GitBranch, Layers3, ListTree, Play, Terminal, Zap } from "lucide-react";
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

/**
 * Splits an activity-definition version string into a compact label and its full exact form.
 * CLR activities carry a behavioral-hash build suffix (e.g. `1.0.0+892535311ec5…`); the short form
 * drops that suffix so version chips stay compact and never crowd out the activity name, while the
 * full string is preserved for tooltips / aria labels so the exact version is still discoverable.
 */
export function formatActivityVersion(version: string): { short: string; full: string; hasHash: boolean } {
  const full = version.trim();
  const plusIndex = full.indexOf("+");
  const short = plusIndex >= 0 ? full.slice(0, plusIndex) : full;
  return { short, full, hasHash: plusIndex >= 0 && short.length < full.length };
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
    case "reusable":
      return <Layers3 size={15} />;
    default:
      return <Boxes size={15} />;
  }
}
