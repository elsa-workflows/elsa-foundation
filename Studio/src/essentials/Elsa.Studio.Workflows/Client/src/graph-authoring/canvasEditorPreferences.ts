import type { ElsaStudioModuleApi, StudioCanvasEditorPreferenceStore } from "@elsa-workflows/studio-sdk";

const namespace = "workflow-authoring";
const validId = (id: unknown): id is string => typeof id === "string" && /^[A-Za-z0-9._-]{1,128}$/.test(id);
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value);
function readDocument(value: unknown, server = false) {
  if (!record(value) || value.schemaVersion !== 1 || (server ? value.namespace !== namespace : value.namespace !== undefined && value.namespace !== namespace)
    || !record(value.value) || Object.keys(value.value).length !== 1 || !validId(value.value.editorId)) return null;
  const revision = value.revision;
  if (server && revision === undefined) return null;
  if (revision !== undefined && (typeof revision !== "string" || !revision
    || [...revision].some(character => character.charCodeAt(0) <= 32 || character.charCodeAt(0) === 127 || character === '"'))) return null;
  return { editorId: value.value.editorId, revision: revision as string | undefined };
}
const statusOf = (error: unknown) => record(error) && typeof error.status === "number" ? error.status : undefined;

/** Uses the existing governed endpoint, with the same user/tenant/host/backend device scope. */
export function createCanvasEditorPreferences(api: Pick<ElsaStudioModuleApi, "backend" | "runtime">): StudioCanvasEditorPreferenceStore {
  const scope = { studioHostId: api.runtime.hostId ?? "default", backendBaseUrl: api.backend.baseUrl,
    subjectId: api.runtime.identity?.subject ?? "anonymous", tenantId: api.runtime.identity?.tenantId ?? "default" };
  const key = `elsa-studio:preferences:${encodeURIComponent(JSON.stringify(scope))}:${namespace}`;
  const headers = { "X-Elsa-Studio-Host-Id": scope.studioHostId };
  const path = `/_elsa/studio/preferences/${namespace}`;
  let revision: string | undefined;
  let writes = Promise.resolve();
  const localLoad = () => {
    try { return readDocument(JSON.parse(localStorage.getItem(key) ?? "null"))?.editorId ?? null; }
    catch { return null; }
  };
  const localSave = (editorId: string) => {
    try { localStorage.setItem(key, JSON.stringify({ schemaVersion: 1, value: { editorId } })); }
    catch { /* Device storage is best effort. */ }
  };
  return {
    async load() {
      const result = writes.then(async () => {
        if (scope.subjectId === "anonymous") return localLoad();
        try {
          const document = readDocument(await api.backend.http.getJson<unknown>(path, { headers }), true);
          revision = document?.revision;
          if (document) localSave(document.editorId);
          return document?.editorId ?? null;
        } catch (error) {
          if (statusOf(error) === 404) revision = undefined;
          return localLoad();
        }
      });
      // Reads share the write queue so a delayed GET cannot restore an older revision.
      writes = result.then(() => undefined);
      return result;
    },
    save(editorId) {
      if (!validId(editorId)) return Promise.reject(new Error("Invalid canvas editor preference."));
      const save = async () => {
        if (scope.subjectId === "anonymous") { localSave(editorId); return; }
        let response: unknown;
        try {
          response = await api.backend.http.putJson<unknown>(path, { schemaVersion: 1, value: { editorId } },
            { headers: { ...headers, ...(revision ? { "If-Match": `"${revision}"` } : { "If-None-Match": "*" }) } });
        } catch (error) {
          const status = statusOf(error);
          if (status !== undefined && status !== 404 && status < 500) throw error;
          localSave(editorId);
          return;
        }
        const saved = readDocument(response, true);
        if (!saved?.revision || saved.editorId !== editorId) throw new Error("The backend returned an incompatible canvas editor preference.");
        revision = saved.revision;
        localSave(saved.editorId);
      };
      const result = writes.then(save);
      writes = result.catch(() => undefined);
      return result;
    }
  };
}
