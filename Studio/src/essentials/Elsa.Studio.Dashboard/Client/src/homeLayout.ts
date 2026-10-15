import {
  homeWidgetIds,
  normalizeHomeWidgetSize,
  type AuthSession,
  type ElsaStudioModuleApi,
  type StudioDashboardWidgetContribution,
  type StudioDashboardWidgetSize
} from "@elsa-workflows/studio-sdk";
import { createPreferenceScope, type StudioPreferenceScope } from "./dashboardPreferences";

export const homeLayoutRoute = "/_elsa/studio/home-layout";
export const homeLayoutDefaultRoute = "/_elsa/studio/home-layout/default";
export const homeLayoutSchemaVersion = 1;
export const homeLayoutLocalStoragePrefix = "elsa-studio:home-layout:";

export type HomeLayoutSource = "user" | "default" | "builtin" | "local";

export interface HomeWidgetLayoutEntry {
  id: string;
  size: StudioDashboardWidgetSize;
  visible: boolean;
  options?: Record<string, unknown>;
}

export interface HomeLayoutValue {
  widgets: HomeWidgetLayoutEntry[];
}

export interface HomeLayoutDocument {
  source: HomeLayoutSource;
  namespace: string;
  schemaVersion: number;
  revision?: string;
  value: HomeLayoutValue;
  updatedAt?: string;
}

export const builtInHomeLayout: HomeLayoutValue = {
  widgets: [
    { id: homeWidgetIds.getStarted, size: "full", visible: true },
    { id: homeWidgetIds.recentWorkflows, size: "medium", visible: true },
    { id: homeWidgetIds.needsAttention, size: "medium", visible: true },
    { id: homeWidgetIds.recentRuns, size: "full", visible: true }
  ]
};

export function createHomeLayoutScope(api: ElsaStudioModuleApi, session: AuthSession): StudioPreferenceScope {
  return createPreferenceScope(api, session);
}

export function isHomeWidgetFeatureEnabled(
  widget: Pick<StudioDashboardWidgetContribution, "requiresFeature">,
  enabledFeatures: Iterable<string> | undefined
): boolean {
  const required = typeof widget.requiresFeature === "string"
    ? [widget.requiresFeature]
    : [...(widget.requiresFeature ?? [])];
  if (required.length === 0) return true;
  if (!enabledFeatures) return true;
  const available = new Set([...enabledFeatures]);
  return required.every(feature => available.has(feature));
}

export function defaultHomeLayoutDocument(): HomeLayoutDocument {
  return {
    source: "builtin",
    namespace: "home-layout",
    schemaVersion: homeLayoutSchemaVersion,
    value: {
      widgets: builtInHomeLayout.widgets.map(entry => ({ ...entry }))
    }
  };
}

export function reconcileHomeLayout(
  layout: HomeLayoutValue,
  contributions: StudioDashboardWidgetContribution[],
  _excludedWidgetIds: ReadonlySet<string> = new Set()
): HomeLayoutValue {
  const known = new Map(contributions.map(widget => [widget.id, widget]));
  const widgets: HomeWidgetLayoutEntry[] = [];
  const seen = new Set<string>();

  for (const entry of layout.widgets) {
    if (seen.has(entry.id)) continue;
    seen.add(entry.id);
    const widget = known.get(entry.id);
    widgets.push({
      id: entry.id,
      size: widget
        ? (widget.supportedSizes.includes(normalizeHomeWidgetSize(entry.size))
          ? normalizeHomeWidgetSize(entry.size)
          : widget.defaultSize)
        : normalizeHomeWidgetSize(entry.size),
      visible: entry.visible !== false,
      options: entry.options
    });
  }

  return { widgets };
}

export class HomeLayoutStore {
  private revision?: string;
  private pendingWrite: Promise<void> = Promise.resolve();

  constructor(private readonly api: ElsaStudioModuleApi, private readonly scope: StudioPreferenceScope) {}

  get anonymous() {
    return this.scope.subjectId === "anonymous";
  }

  async load(): Promise<HomeLayoutDocument> {
    if (this.anonymous) return this.loadLocal();
    try {
      const document = await this.api.backend.http.getJson<HomeLayoutDocument>(homeLayoutRoute, { headers: this.headers() });
      const normalized = normalizeDocument(document);
      this.revision = normalized.source === "user" ? normalized.revision : undefined;
      return normalized;
    } catch {
      return this.loadLocal();
    }
  }

  async save(document: HomeLayoutDocument): Promise<HomeLayoutDocument> {
    if (this.anonymous) return this.saveLocal(document);
    let resolveWrite!: (document: HomeLayoutDocument) => void;
    let rejectWrite!: (error: unknown) => void;
    const result = new Promise<HomeLayoutDocument>((resolve, reject) => { resolveWrite = resolve; rejectWrite = reject; });
    this.pendingWrite = this.pendingWrite.then(async () => {
      try { resolveWrite(await this.saveNow(document)); } catch (error) { rejectWrite(error); }
    });
    return result;
  }

  async reset(): Promise<HomeLayoutDocument> {
    if (this.anonymous) {
      try { localStorage.removeItem(this.localKey()); } catch { /* best effort */ }
      this.revision = undefined;
      return defaultHomeLayoutDocument();
    }
    try {
      await this.api.backend.http.deleteJson(homeLayoutRoute, { headers: this.headers() });
    } catch (error) {
      if (!hasStatus(error, 404)) throw error;
    }
    this.revision = undefined;
    return this.load();
  }

  async saveDefault(document: HomeLayoutDocument): Promise<HomeLayoutDocument> {
    const revision = document.source === "default" ? document.revision : undefined;
    const headers = { ...this.headers(), ...(revision ? { "If-Match": `"${revision}"` } : { "If-None-Match": "*" }) };
    const saved = await this.api.backend.http.putJson<HomeLayoutDocument>(homeLayoutDefaultRoute, {
      schemaVersion: homeLayoutSchemaVersion,
      value: document.value
    }, { headers });
    return normalizeDocument(saved);
  }

  private async saveNow(document: HomeLayoutDocument): Promise<HomeLayoutDocument> {
    const revision = this.revision ?? document.revision;
    const headers = { ...this.headers(), ...(revision ? { "If-Match": `"${revision}"` } : { "If-None-Match": "*" }) };
    try {
      const saved = await this.api.backend.http.putJson<HomeLayoutDocument>(homeLayoutRoute, {
        schemaVersion: homeLayoutSchemaVersion,
        value: document.value
      }, { headers });
      this.revision = saved.revision;
      return normalizeDocument(saved);
    } catch (error) {
      if (hasStatus(error, 409) || hasStatus(error, 412)) throw error;
      return this.saveLocal(document);
    }
  }

  private headers() {
    return { "X-Elsa-Studio-Host-Id": this.scope.studioHostId };
  }

  private localKey() {
    return `${homeLayoutLocalStoragePrefix}${encodeURIComponent(JSON.stringify(this.scope))}`;
  }

  private saveLocal(document: HomeLayoutDocument): HomeLayoutDocument {
    const local: HomeLayoutDocument = { ...document, source: "local", updatedAt: new Date().toISOString() };
    try { localStorage.setItem(this.localKey(), JSON.stringify(local)); } catch { /* device storage is best effort */ }
    return local;
  }

  private loadLocal(): HomeLayoutDocument {
    try {
      const value = localStorage.getItem(this.localKey());
      if (value) return normalizeDocument(JSON.parse(value) as HomeLayoutDocument);
    } catch { /* fall through to builtin */ }
    return defaultHomeLayoutDocument();
  }
}

function normalizeDocument(document: HomeLayoutDocument): HomeLayoutDocument {
  return {
    ...document,
    source: document.source ?? "builtin",
    namespace: document.namespace ?? "home-layout",
    schemaVersion: document.schemaVersion ?? homeLayoutSchemaVersion,
    value: {
      widgets: (document.value?.widgets ?? []).map(entry => ({
        id: entry.id,
        size: normalizeHomeWidgetSize(entry.size),
        visible: entry.visible !== false,
        options: entry.options
      }))
    }
  };
}

function hasStatus(error: unknown, status: number) {
  return error instanceof Error && (error as Error & { status?: number }).status === status;
}
