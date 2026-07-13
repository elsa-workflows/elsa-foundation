import type { AuthSession, ElsaStudioModuleApi, StudioDashboardWidgetContribution, StudioDashboardWidgetSize } from "@elsa-workflows/studio-sdk";

export const dashboardPreferenceNamespace = "dashboard";
export const dashboardPreferenceSchemaVersion = 1;
export type DashboardRefreshInterval = 0 | 60_000 | 300_000 | 900_000 | 1_800_000;

export interface DashboardWidgetPreference {
  id: string;
  visible: boolean;
  size: StudioDashboardWidgetSize;
  settingsSchemaVersion?: number;
  settings?: unknown;
}

export interface DashboardPreferences {
  refreshIntervalMs: DashboardRefreshInterval;
  autoAddNewWidgets: boolean;
  widgets: DashboardWidgetPreference[];
}

export interface DashboardPreferenceDocument {
  namespace: string;
  schemaVersion: number;
  revision?: string;
  value: DashboardPreferences;
  updatedAt?: string;
}

export interface StudioPreferenceScope {
  studioHostId: string;
  backendBaseUrl: string;
  subjectId: string;
  tenantId: string;
}

export function createPreferenceScope(api: ElsaStudioModuleApi, session: AuthSession): StudioPreferenceScope {
  return {
    studioHostId: api.runtime.hostId ?? "default",
    backendBaseUrl: api.backend.baseUrl,
    subjectId: session.subject ?? "anonymous",
    tenantId: session.tenantId ?? "default"
  };
}

export function defaultDashboardPreferences(api: ElsaStudioModuleApi): DashboardPreferences {
  const configured = api.runtime.dashboard?.defaultRefreshIntervalMs;
  const allowed: DashboardRefreshInterval[] = [0, 60_000, 300_000, 900_000, 1_800_000];
  return { refreshIntervalMs: allowed.includes(configured as DashboardRefreshInterval) ? configured as DashboardRefreshInterval : 300_000, autoAddNewWidgets: true, widgets: [] };
}

export function reconcileDashboardPreferences(
  preferences: DashboardPreferences,
  contributions: StudioDashboardWidgetContribution[],
  pinnedWidgetIds: readonly string[] = [],
  onSettingsReset: (widgetId: string) => void = () => undefined
): DashboardPreferences {
  const byId = new Map(preferences.widgets.map(entry => [entry.id, entry]));
  const pinned = new Set(pinnedWidgetIds);
  const known = contributions.map(widget => {
    const stored = byId.get(widget.id);
    const validSize = stored && widget.supportedSizes.includes(stored.size) ? stored.size : widget.defaultSize;
    let settings = stored?.settings;
    let settingsSchemaVersion = stored?.settingsSchemaVersion;
    if (widget.settings) {
      let reset = false;
      try {
        if (settings !== undefined && settingsSchemaVersion !== widget.settings.schemaVersion) {
          const migrated = widget.settings.migrate?.(settings, settingsSchemaVersion ?? 0);
          settings = migrated ?? widget.settings.defaults;
          reset = migrated == null;
          settingsSchemaVersion = widget.settings.schemaVersion;
        }
        const validated = widget.settings.validate(settings ?? widget.settings.defaults);
        if (validated == null) {
          settings = widget.settings.defaults;
          reset ||= stored?.settings !== undefined;
        } else {
          settings = validated;
        }
      } catch {
        settings = widget.settings.defaults;
        reset = stored?.settings !== undefined;
      }
      settingsSchemaVersion = widget.settings.schemaVersion;
      if (reset) onSettingsReset(widget.id);
    }
    return { id: widget.id, visible: pinned.has(widget.id) || (stored?.visible ?? (preferences.autoAddNewWidgets && widget.defaultVisible)), size: validSize, settings, settingsSchemaVersion };
  });
  const knownIds = new Set(contributions.map(widget => widget.id));
  return { ...preferences, widgets: [...known, ...preferences.widgets.filter(entry => !knownIds.has(entry.id))] };
}

export class DashboardPreferenceStore {
  private revision?: string;
  private pendingWrite: Promise<void> = Promise.resolve();

  constructor(private readonly api: ElsaStudioModuleApi, private readonly scope: StudioPreferenceScope) {}

  async load(): Promise<DashboardPreferenceDocument> {
    if (this.scope.subjectId === "anonymous") return this.loadLocal();
    try {
      const document = await this.api.backend.http.getJson<DashboardPreferenceDocument>(`/_elsa/studio/preferences/${dashboardPreferenceNamespace}`, { headers: this.headers() });
      this.revision = document.revision;
      return document;
    } catch (error) {
      if (hasStatus(error, 404)) return this.defaults();
      return this.loadLocal();
    }
  }

  async save(document: DashboardPreferenceDocument): Promise<DashboardPreferenceDocument> {
    if (this.scope.subjectId === "anonymous") return this.saveLocal(document);
    let resolveWrite!: (document: DashboardPreferenceDocument) => void;
    let rejectWrite!: (error: unknown) => void;
    const result = new Promise<DashboardPreferenceDocument>((resolve, reject) => { resolveWrite = resolve; rejectWrite = reject; });
    this.pendingWrite = this.pendingWrite.then(async () => {
      try { resolveWrite(await this.saveNow(document)); } catch (error) { rejectWrite(error); }
    });
    return result;
  }

  private async saveNow(document: DashboardPreferenceDocument): Promise<DashboardPreferenceDocument> {
    const revision = this.revision ?? document.revision;
    const headers = { ...this.headers(), ...(revision ? { "If-Match": `"${revision}"` } : { "If-None-Match": "*" }) };
    try {
      const saved = await this.api.backend.http.putJson<DashboardPreferenceDocument>(`/_elsa/studio/preferences/${dashboardPreferenceNamespace}`, {
        schemaVersion: dashboardPreferenceSchemaVersion,
        value: document.value
      }, { headers });
      this.revision = saved.revision;
      return saved;
    } catch (error) {
      if (hasStatus(error, 409) || hasStatus(error, 412)) throw error;
      return this.saveLocal(document);
    }
  }

  private defaults(): DashboardPreferenceDocument { return { namespace: dashboardPreferenceNamespace, schemaVersion: dashboardPreferenceSchemaVersion, value: defaultDashboardPreferences(this.api) }; }
  private headers() { return { "X-Elsa-Studio-Host-Id": this.scope.studioHostId }; }
  private localKey() { return `elsa-studio:preferences:${encodeURIComponent(JSON.stringify(this.scope))}:${dashboardPreferenceNamespace}`; }
  private saveLocal(document: DashboardPreferenceDocument) {
    const local = { ...document, updatedAt: new Date().toISOString() };
    try { localStorage.setItem(this.localKey(), JSON.stringify(local)); } catch { /* device storage is best effort */ }
    return local;
  }
  private loadLocal() {
    try {
      const value = localStorage.getItem(this.localKey());
      if (value) return JSON.parse(value) as DashboardPreferenceDocument;
    } catch { /* fall through to defaults */ }
    return this.defaults();
  }
}

function hasStatus(error: unknown, status: number) { return error instanceof Error && (error as Error & { status?: number }).status === status; }
