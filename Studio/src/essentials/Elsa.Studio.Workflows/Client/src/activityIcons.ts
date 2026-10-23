import type { ActivityCatalogItem } from "./workflowTypes";

/**
 * Shared activity-icon resolution for the designer palette, canvas nodes, and the run-view
 * timeline. One mapping: catalog metadata first, then type, then category. Do not add a second
 * palette- or node-only table.
 *
 * `icon` is the historical kind token. `iconName` takes precedence as the module-supplied designer icon: any
 * lucide export or kebab name, `asset:<package>/<path>` for a static web asset, or `ext:<name>`
 * for a component registered through `registerActivityIcons`. An unknown or unsafe value is
 * ignored so type → category → trigger → reusable/generic still run. A definition identity is
 * not an icon choice: CLR activities and custom reusable activities both have one.
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

  const configured = normalizeConfiguredIcon(activity.iconName) ?? normalizeConfiguredIcon(activity.icon);
  if (configured) return configured;

  const typeIcon = iconFromTypeKey(activity.activityTypeKey, activity.displayName);
  if (typeIcon) return typeIcon;

  const categoryIcon = iconFromCategory(activity.category);
  if (categoryIcon) return categoryIcon;

  if ((activity.executionType ?? "").toLowerCase() === "trigger") return "trigger";
  if (activity.activityDefinitionId) return "reusable";
  return "activity";
}

/** Category / type fallback used when a configured icon is unknown or fails to load. */
export function resolveActivityIconFallback(activity: ActivityIconSource | undefined): WorkflowNodeIcon {
  if (!activity) return "activity";
  return resolveActivityIcon({
    activityTypeKey: activity.activityTypeKey,
    displayName: activity.displayName,
    category: activity.category,
    executionType: activity.executionType,
    activityDefinitionId: activity.activityDefinitionId
  });
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

export const activityIconMaxLength = 256;
const assetIconPattern = /^asset:([A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?)\/([A-Za-z0-9._-]+(?:\/[A-Za-z0-9._-]+)*)$/;
const extensionIconPattern = /^ext:([A-Za-z][A-Za-z0-9_-]*)$/;
const lucideKebabPattern = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;

function hasDotOnlyAssetSegment(path: string): boolean {
  return path.split("/").some(segment => segment === "." || segment === "..");
}

function normalizeConfiguredIcon(icon: string | null | undefined): WorkflowNodeIcon | null {
  if (!icon) return null;
  const trimmed = icon.trim();
  if (!trimmed || trimmed.length > activityIconMaxLength || trimmed.includes("://") || trimmed.includes("\\")) return null;
  if (hasDotOnlyAssetSegment(trimmed)) return null;

  const kind = trimmed.toLowerCase();
  if (knownIconSet.has(kind)) return kind as KnownWorkflowNodeIcon;

  if (isActivityIconAsset(trimmed) || isActivityIconExtension(trimmed)) return trimmed;

  const lucideName = toLucideExportName(trimmed);
  if (lucideName && (/^[A-Z][A-Za-z0-9]*$/.test(trimmed) || lucideKebabPattern.test(trimmed))) return lucideName;
  return null;
}

/** Turns a persisted `asset:package/path` value into the hosted static-web-asset URL. */
export function toActivityIconAssetUrl(icon: string, baseHref?: string): string | null {
  const trimmed = icon.trim();
  if (trimmed.length > activityIconMaxLength) return null;
  const match = trimmed.match(assetIconPattern);
  if (!match) return null;
  const [, packageId, path] = match;
  if (hasDotOnlyAssetSegment(path)) return null;
  const relative = `_content/${encodeURIComponent(packageId)}/${path.split("/").map(encodeURIComponent).join("/")}`;
  return new URL(relative, resolveStudioHostBase(baseHref)).pathname;
}

/** Host origin + PathBase. `/studio/...` is the SPA mount, so `_content` sits beside it. */
export function resolveStudioHostBase(baseHref?: string): string {
  const source = baseHref
    ?? (typeof document !== "undefined" ? document.baseURI : "http://localhost/");
  const url = new URL(source, "http://localhost/");
  const studio = url.pathname.indexOf("/studio");
  const prefix = studio >= 0 ? url.pathname.slice(0, studio) : url.pathname;
  url.pathname = `${prefix.replace(/\/+$/, "")}/`;
  url.search = "";
  url.hash = "";
  return url.toString();
}

export function isActivityIconAsset(icon: string): boolean {
  const trimmed = icon.trim();
  if (trimmed.length > activityIconMaxLength) return false;
  const match = trimmed.match(assetIconPattern);
  return Boolean(match && !hasDotOnlyAssetSegment(match[2] ?? ""));
}

export function isActivityIconExtension(icon: string): boolean {
  const trimmed = icon.trim();
  return trimmed.length <= activityIconMaxLength && extensionIconPattern.test(trimmed);
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
