import type { ActivityCatalogItem } from "./workflowTypes";

/**
 * Shared activity-icon resolution for the designer palette, canvas nodes, and the run-view
 * timeline. One mapping: catalog metadata first, then type, then category. Do not add a second
 * palette- or node-only table.
 *
 * `icon` is the historical kind token. `iconName` is a lucide name from the descriptor (design
 * spec: when set and mapped, render that lucide icon). An unknown `icon` / `iconName` is ignored
 * so type → category → trigger → generic still run; `Boxes` is only the last resort.
 */

export const knownWorkflowNodeIcons = [
  "activity",
  "flowchart",
  "sequence",
  "terminal",
  "runtime",
  "trigger",
  "reusable",
  "branch",
  "fork",
  "join",
  "loop",
  "timer",
  "clock",
  "scripting",
  "email",
  "http",
  "variable",
  "start",
  "finish",
  "event",
  "database",
  "bpmn",
  "fault",
  "correlate"
] as const;

export type KnownWorkflowNodeIcon = (typeof knownWorkflowNodeIcons)[number];
export type WorkflowNodeIcon = KnownWorkflowNodeIcon | (string & {});

export const activityIconTones = ["terminal", "composite", "runtime", "trigger", "success", "danger", "accent"] as const;
export type ActivityIconTone = (typeof activityIconTones)[number];

export const knownLucideExportNames = [
  "Activity",
  "AlertTriangle",
  "Boxes",
  "Braces",
  "Clock",
  "Code2",
  "Database",
  "FlagTriangleRight",
  "GitBranch",
  "GitFork",
  "GitMerge",
  "Globe",
  "Layers3",
  "ListTree",
  "Mail",
  "Play",
  "Repeat",
  "Square",
  "Terminal",
  "Timer",
  "Variable",
  "Workflow",
  "Zap"
] as const;

const knownIconSet = new Set<string>(knownWorkflowNodeIcons);
const knownLucideSet = new Set<string>(knownLucideExportNames);

export function isKnownLucideExportName(name: string): boolean {
  return knownLucideSet.has(name);
}

const typeIconBySuffix: Record<string, KnownWorkflowNodeIcon> = {
  writeline: "terminal",
  writelines: "terminal",
  readline: "terminal",
  if: "branch",
  flowdecision: "branch",
  bpmndecision: "branch",
  switch: "fork",
  flowswitch: "fork",
  fork: "fork",
  parallel: "fork",
  flowjoin: "join",
  foreach: "loop",
  while: "loop",
  for: "loop",
  do: "loop",
  timer: "timer",
  delay: "clock",
  cron: "clock",
  setvariable: "variable",
  setoutput: "variable",
  runjavascript: "scripting",
  runcsharp: "scripting",
  runpython: "scripting",
  sendemail: "email",
  httpendpoint: "http",
  sendhttprequest: "http",
  writehttpresponse: "http",
  event: "event",
  publishevent: "event",
  start: "start",
  finish: "finish",
  fault: "fault",
  flowchart: "flowchart",
  sequence: "sequence",
  bpmnprocess: "bpmn",
  correlate: "correlate",
  dispatchworkflow: "flowchart",
  graphactivity: "flowchart"
};

const categoryIconByName: Record<string, KnownWorkflowNodeIcon> = {
  http: "http",
  scheduling: "timer",
  branching: "branch",
  "control flow": "branch",
  controlflow: "branch",
  looping: "loop",
  scripting: "scripting",
  storage: "database",
  email: "email",
  console: "terminal",
  primitives: "variable",
  workflows: "flowchart",
  composition: "flowchart",
  flow: "join",
  flowchart: "flowchart",
  sequence: "sequence",
  bpmn: "bpmn",
  runtime: "runtime"
};

const categoryToneByName: Record<string, ActivityIconTone> = {
  console: "terminal",
  primitives: "terminal",
  branching: "composite",
  "control flow": "composite",
  controlflow: "composite",
  flow: "composite",
  flowchart: "composite",
  sequence: "composite",
  composition: "composite",
  workflows: "composite",
  bpmn: "composite",
  http: "composite",
  looping: "trigger",
  scripting: "success",
  email: "trigger",
  scheduling: "runtime",
  runtime: "runtime",
  storage: "composite"
};

/** Fields the shared resolver reads. Catalog items and canvas node snapshots both qualify. */
export type ActivityIconSource = Pick<ActivityCatalogItem, "activityTypeKey" | "displayName" | "category" | "executionType"> &
  Partial<Pick<ActivityCatalogItem, "icon" | "iconName" | "activityDefinitionId">>;

export function resolveActivityIcon(activity: ActivityIconSource | undefined): WorkflowNodeIcon {
  if (!activity) return "activity";
  if (activity.activityDefinitionId) return "reusable";

  const configured = normalizeConfiguredIcon(activity.icon) ?? normalizeConfiguredIcon(activity.iconName);
  if (configured) return configured;

  const typeIcon = iconFromTypeKey(activity.activityTypeKey, activity.displayName);
  if (typeIcon) return typeIcon;

  const categoryIcon = iconFromCategory(activity.category);
  if (categoryIcon) return categoryIcon;

  if ((activity.executionType ?? "").toLowerCase() === "trigger") return "trigger";
  return "activity";
}

export function resolveActivityIconTone(activity: ActivityIconSource | undefined): ActivityIconTone {
  if (!activity) return "accent";

  const typeKey = lastTypeSegment(activity.activityTypeKey);
  const displayName = (activity.displayName ?? "").toLowerCase();
  if (typeKey === "fault" || displayName === "fault") return "danger";
  if (typeKey === "finish" || displayName === "finish") return "danger";
  if ((activity.executionType ?? "").toLowerCase() === "trigger") return "trigger";

  const category = normalizeCategory(activity.category);
  return categoryToneByName[category] ?? "accent";
}

function normalizeConfiguredIcon(icon: string | null | undefined): WorkflowNodeIcon | null {
  if (!icon) return null;
  const trimmed = icon.trim();
  if (!trimmed) return null;

  const kind = trimmed.toLowerCase();
  if (knownIconSet.has(kind)) return kind as KnownWorkflowNodeIcon;

  const lucideName = toLucideExportName(trimmed);
  if (lucideName && isKnownLucideExportName(lucideName)) return lucideName;
  return null;
}

function iconFromTypeKey(typeKey: string | undefined, displayName: string | undefined): KnownWorkflowNodeIcon | null {
  const suffix = lastTypeSegment(typeKey);
  if (suffix && typeIconBySuffix[suffix]) return typeIconBySuffix[suffix];

  const name = (displayName ?? "").trim().toLowerCase();
  if (name === "flowchart") return "flowchart";
  if (name === "sequence") return "sequence";
  if (name.includes("write line")) return "terminal";
  return null;
}

function iconFromCategory(category: string | null | undefined): KnownWorkflowNodeIcon | null {
  const key = normalizeCategory(category);
  return key ? categoryIconByName[key] ?? null : null;
}

function lastTypeSegment(typeKey: string | undefined): string {
  return (typeKey?.split(".").filter(Boolean).at(-1) ?? "").toLowerCase();
}

function normalizeCategory(category: string | null | undefined): string {
  return (category ?? "").trim().toLowerCase().replace(/[_-]+/g, " ");
}

/** Accepts lucide export names (`GitBranch`) and kebab/space forms (`git-branch`). */
export function toLucideExportName(value: string): string | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  if (/^[A-Z][A-Za-z0-9]*$/.test(trimmed)) return trimmed;
  const pascal = trimmed
    .split(/[-_\s]+/)
    .filter(Boolean)
    .map(part => part.charAt(0).toUpperCase() + part.slice(1))
    .join("");
  return pascal || null;
}
