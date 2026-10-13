import { useEffect, useState, type ComponentType } from "react";
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
import { getRegisteredActivityIcon } from "@elsa-workflows/studio-sdk";
import {
  isActivityIconAsset,
  isActivityIconExtension,
  toActivityIconAssetUrl,
  toLucideExportName,
  type WorkflowNodeIcon
} from "./activityIcons";

/**
 * Lazy glyph registry for activity icons. Keep this module off the Workflows entry and landing
 * paths — load it through `loadActivityIconRegistry` / `renderActivityIcon`. Extension modules
 * register React icons with `registerActivityIcons`; unmapped lucide names load on demand.
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
const lucideLoaderCache = new Map<string, Promise<LucideIcon | undefined>>();

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

function toLucideKebabName(exportName: string): string {
  return exportName
    .replace(/([a-z0-9])([A-Z])/g, "$1-$2")
    .replace(/([A-Z])([A-Z][a-z])/g, "$1-$2")
    .replace(/([a-zA-Z])(\d+)/g, "$1-$2")
    .toLowerCase();
}

function loadLucideIcon(exportName: string): Promise<LucideIcon | undefined> {
  const cached = lucideLoaderCache.get(exportName);
  if (cached) return cached;

  const pending = import("lucide-react/dynamicIconImports")
    .then(module => {
      const loaders = module.default as Record<string, () => Promise<{ default: LucideIcon }>>;
      const loader = loaders[toLucideKebabName(exportName)];
      return loader ? loader().then(loaded => loaded.default) : undefined;
    })
    .catch(() => undefined);

  lucideLoaderCache.set(exportName, pending);
  return pending;
}

function LazyLucideGlyph({ name, fallback: Fallback }: { name: string; fallback: LucideIcon }) {
  const [Icon, setIcon] = useState<LucideIcon | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setIcon(null);
    setFailed(false);
    loadLucideIcon(name)
      .then(loaded => {
        if (cancelled) return;
        // Store the component behind a wrapper so React does not call it as a setState updater.
        if (loaded) setIcon(() => loaded);
        else setFailed(true);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });
    return () => {
      cancelled = true;
    };
  }, [name]);

  const Glyph = failed || !Icon ? Fallback : Icon;
  return <Glyph size={15} />;
}

function ActivityAssetIcon({ src, fallback: Fallback }: { src: string; fallback: LucideIcon }) {
  const [failed, setFailed] = useState(false);
  if (failed) return <Fallback size={15} />;
  return (
    <img
      className="wf-activity-icon-asset"
      src={src}
      alt=""
      width={15}
      height={15}
      draggable={false}
      onError={() => setFailed(true)}
    />
  );
}

function RegisteredActivityIcon({ name, fallback: Fallback }: { name: string; fallback: LucideIcon }) {
  const Icon = getRegisteredActivityIcon(name) as ComponentType<{ size?: number }> | undefined;
  if (!Icon) return <Fallback size={15} />;
  return <Icon size={15} />;
}

export function ActivityIconGlyph({ icon, fallback }: { icon?: WorkflowNodeIcon; fallback?: WorkflowNodeIcon }) {
  const Fallback = resolveActivityIconGlyph(fallback);

  if (icon && isActivityIconAsset(icon)) {
    const src = toActivityIconAssetUrl(icon);
    if (src) return <ActivityAssetIcon src={src} fallback={Fallback} />;
    return <Fallback size={15} />;
  }

  if (icon && isActivityIconExtension(icon)) {
    return <RegisteredActivityIcon name={icon} fallback={Fallback} />;
  }

  if (icon && !kindIcons[icon]) {
    const lucideName = toLucideExportName(icon);
    if (lucideName && !lucideByExportName[lucideName]) {
      return <LazyLucideGlyph name={lucideName} fallback={Fallback} />;
    }
  }

  const Icon = resolveActivityIconGlyph(icon);
  return <Icon size={15} />;
}
