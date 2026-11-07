import type { ElsaStudioModuleApi, StudioCanvasEditorPreferenceStore } from "@elsa-workflows/studio-sdk";

interface PreferenceDocument { schemaVersion: number; revision?: string; value: { editorId: string } }
const namespace = "workflow-authoring";
function valid(document: PreferenceDocument) { return document.schemaVersion === 1 && typeof document.value?.editorId === "string" && /^[A-Za-z0-9._-]{1,128}$/.test(document.value.editorId); }
function hasStatus(error: unknown, status: number) { return error instanceof Error && (error as Error & { status?: number }).status === status; }

/** Same governed, per-user/tenant/host preference endpoint and local fallback as other Studio preferences. */
export function createCanvasEditorPreferences(api: Pick<ElsaStudioModuleApi, "backend" | "runtime">): StudioCanvasEditorPreferenceStore {
  const scope = { studioHostId: api.runtime.hostId ?? "default", backendBaseUrl: api.backend.baseUrl,
    subjectId: api.runtime.identity?.subject ?? "anonymous", tenantId: api.runtime.identity?.tenantId ?? "default" };
  const key = `elsa-studio:preferences:${encodeURIComponent(JSON.stringify(scope))}:${namespace}`;
  const headers = { "X-Elsa-Studio-Host-Id": scope.studioHostId };
  let revision: string | undefined;
  let writes = Promise.resolve();
  const localLoad = () => {
    try { const document: PreferenceDocument = JSON.parse(localStorage.getItem(key) ?? "null"); return document && valid(document) ? document.value.editorId : null; }
    catch { return null; }
  };
  const localSave = (document: PreferenceDocument) => { try { localStorage.setItem(key, JSON.stringify(document)); } catch { /* device storage is best effort */ } };
  return {
    async load() {
      if (scope.subjectId === "anonymous") return localLoad();
      try {
        const document = await api.backend.http.getJson<PreferenceDocument>(`/_elsa/studio/preferences/${namespace}`, { headers });
        if (!valid(document)) return null;
        revision = document.revision;
        return document.value.editorId;
      } catch { return localLoad(); }
    },
    save(editorId) {
      const document = { schemaVersion: 1, value: { editorId } };
      if (!valid(document)) return Promise.reject(new Error("Invalid canvas editor preference."));
      const save = async () => {
        if (scope.subjectId === "anonymous") { localSave(document); return; }
        try {
          const saved = await api.backend.http.putJson<PreferenceDocument>(`/_elsa/studio/preferences/${namespace}`, document,
            { headers: { ...headers, ...(revision ? { "If-Match": `"${revision}"` } : { "If-None-Match": "*" }) } });
          revision = saved.revision;
        } catch (error) {
          if (hasStatus(error, 409) || hasStatus(error, 412)) throw error;
          localSave(document);
        }
      };
      const result = writes.then(save);
      writes = result.catch(() => undefined);
      return result;
    }
  };
}
