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
 * Trims `text` and, when it exceeds `max` characters, clips it to a single-character ellipsis so long
 * node summaries / expressions never blow out a node's width. The full string is expected to remain
 * available elsewhere (e.g. a tooltip), so the clip is purely presentational.
 */
export function truncate(text: string, max = 40) {
  const value = text.trim();
  if (value.length <= max) return value;
  return `${value.slice(0, Math.max(0, max - 1)).trimEnd()}…`;
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

/**
 * Humanizes the last CLR/type-key segment for presentation, e.g.
 * `Elsa.Activities.Http.Activities.SendHttpRequest` → "Send Http Request".
 * Splits PascalCase/camelCase runs (preserving acronym boundaries) and normalizes separators.
 */
export function humanizeTypeName(key: string | null | undefined) {
  const short = shortTypeName(key);
  if (!short) return "";
  return short
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

/**
 * Resolves the title shown for an Activity Definition row/header. When the backend supplies a distinct
 * display name (e.g. server-side humanized), it is preferred verbatim. When the display name is missing
 * or identical to the type key (source-owned activities whose display name is the raw CLR type), a
 * client-side humanized short name is used so the row never shows the full CLR type twice.
 */
export function resolveActivityDefinitionTitle(displayName: string | null | undefined, typeKey: string | null | undefined) {
  const name = displayName?.trim();
  const key = typeKey?.trim();
  if (name && name !== key) return name;
  return humanizeTypeName(key) || name || key || "Unnamed activity";
}

/**
 * Formats an Activity Definition timestamp. Missing, unparseable, or pre-2000 sentinel values
 * (e.g. `DateTime.MinValue`, which serializes as `0001-01-01`) render as an em-dash rather than a
 * misleading "01/01/1". Otherwise the locale date is shown.
 */
export function formatActivityDefinitionDate(value?: string | null) {
  if (!value) return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime()) || date.getFullYear() < 2000) return "—";
  return date.toLocaleDateString();
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
