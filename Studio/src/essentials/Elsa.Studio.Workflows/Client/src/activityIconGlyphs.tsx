import {
  Activity,
  AlertTriangle,
  Boxes,
  Braces,
  Clock,
  Code2,
  Database,
  FlagTriangleRight,
  GitBranch,
  GitFork,
  GitMerge,
  Globe,
  Layers3,
  ListTree,
  Mail,
  Play,
  Repeat,
  Square,
  Terminal,
  Timer,
  Variable,
  Workflow,
  Zap,
  type LucideIcon
} from "lucide-react";
import { toLucideExportName, type WorkflowNodeIcon } from "./activityIcons";

/**
 * Lazy glyph registry for activity icons. Keep this module off the Workflows entry and landing
 * paths — load it through `loadActivityIconRegistry` / `renderActivityIcon`. A later PR can register
 * extension glyphs (including any lucide name) via `registerActivityIconGlyphs`.
 */

export type ActivityIconGlyphResolver = (icon: string) => LucideIcon | undefined;

const lucideByExportName: Record<string, LucideIcon> = {
  Activity,
  AlertTriangle,
  Boxes,
  Braces,
  Clock,
  Code2,
  Database,
  FlagTriangleRight,
  GitBranch,
  GitFork,
  GitMerge,
  Globe,
  Layers3,
  ListTree,
  Mail,
  Play,
  Repeat,
  Square,
  Terminal,
  Timer,
  Variable,
  Workflow,
  Zap
};

const kindIcons: Record<string, LucideIcon> = {
  flowchart: GitBranch,
  sequence: ListTree,
  terminal: Terminal,
  runtime: Play,
  trigger: Zap,
  reusable: Layers3,
  branch: GitBranch,
  fork: GitFork,
  join: GitMerge,
  loop: Repeat,
  timer: Timer,
  clock: Clock,
  scripting: Code2,
  email: Mail,
  http: Globe,
  variable: Variable,
  start: Play,
  finish: Square,
  event: Zap,
  database: Database,
  bpmn: Workflow,
  fault: AlertTriangle,
  correlate: Braces,
  activity: Boxes
};

const extensionResolvers: ActivityIconGlyphResolver[] = [];

export const mappedLucideExportNames = Object.keys(lucideByExportName);

export function registerActivityIconGlyphs(resolve: ActivityIconGlyphResolver) {
  extensionResolvers.push(resolve);
}

export function resolveActivityIconGlyph(icon: WorkflowNodeIcon | undefined): LucideIcon {
  if (!icon) return Boxes;
  if (kindIcons[icon]) return kindIcons[icon];
  const lucideName = toLucideExportName(icon);
  if (lucideName && lucideByExportName[lucideName]) return lucideByExportName[lucideName];
  for (const resolve of extensionResolvers) {
    const found = resolve(icon);
    if (found) return found;
  }
  return Boxes;
}

export function ActivityIconGlyph({ icon }: { icon?: WorkflowNodeIcon }) {
  const Icon = resolveActivityIconGlyph(icon);
  return <Icon size={15} />;
}
