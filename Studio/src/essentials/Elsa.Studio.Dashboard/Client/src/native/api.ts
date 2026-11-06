import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { AudienceCandidate, AudienceGrant, DashboardResource, DatasetDeclaration, DefinitionRevision, Page, QueryResult, QuestionDefinition } from "./types";
import { serializeTiles } from "./types";
import { scheduleNativeRequest } from "./requestScheduler";

function revisionTag(revision: string) {
  if (!revision || [...revision].some(character => character.charCodeAt(0) <= 32 || character.charCodeAt(0) === 34 || character.charCodeAt(0) === 127))
    throw new Error("The saved revision is invalid. Reload the current definition before retrying.");
  return `"${revision}"`;
}

export class DashboardApi {
  constructor(readonly context: StudioEndpointContext, private readonly actorScope?: string) {}
  private request<T>(signal: AbortSignal | undefined, work: () => Promise<T>) { return scheduleNativeRequest(this.context, this.actorScope, signal, work); }
  private url(path: string) { return `${this.context.baseUrl.replace(/\/$/, "")}/_elsa/studio/${path}`; }
  list(kind: "question" | "dashboard", view: string, search = "", continuationToken?: string | null, signal?: AbortSignal) {
    const query = new URLSearchParams({ ownership: view, search, pageSize: "50" });
    if (continuationToken) query.set("continuationToken", continuationToken);
    return this.request(signal, () => this.context.http.getJson<Page<DashboardResource>>(this.url(`${kind}s?${query}`), { signal }));
  }
  get(kind: "question" | "dashboard", id: string, signal?: AbortSignal) { return this.request(signal, () => this.context.http.getJson<DashboardResource>(this.url(`${kind}s/${encodeURIComponent(id)}`), { signal })); }
  datasets(signal?: AbortSignal) { return this.request(signal, () => this.context.http.getJson<Page<DatasetDeclaration>>(this.url("dashboard-datasets"), { signal })); }
  preview(definition: QuestionDefinition, signal?: AbortSignal) { return this.request(signal, () => this.context.http.postJson<QueryResult>(this.url("dashboard-queries/preview"), definition, { signal })); }
  savedPreview(id: string, revision: string, definition: QuestionDefinition, signal?: AbortSignal) { return this.request(signal, () => this.context.http.postJson<QueryResult>(this.url(`questions/${encodeURIComponent(id)}/preview`), definition, { headers: { "If-Match": revisionTag(revision) }, signal })); }
  validate(definition: QuestionDefinition, signal?: AbortSignal) { return this.request(signal, () => this.context.http.postJson<{ isValid: boolean; problems: { code: string; path: string; message: string }[] }>(this.url("dashboard-queries/validate"), definition, { signal })); }
  save(resource: Pick<DashboardResource, "kind" | "name" | "description"> & Partial<DashboardResource>, operationKey: string, signal?: AbortSignal) {
    const body = { name: resource.name, description: resource.description, ...(resource.kind === "question" ? resource.definitionState === "withheld" ? { preserveDefinition: true } : { definition: resource.definition } : { tiles: serializeTiles(resource.tiles ?? []) }) };
    return this.request(signal, () => resource.id
      ? this.context.http.putJson<DashboardResource>(this.url(`${resource.kind}s/${encodeURIComponent(resource.id)}`), body, { headers: { "If-Match": revisionTag(resource.revision!) }, signal })
      : this.context.http.postJson<DashboardResource>(this.url(`${resource.kind}s`), { ...body, operationKey }, { signal }));
  }
  remove(resource: DashboardResource, signal?: AbortSignal) { return this.request(signal, () => this.context.http.deleteJson(this.url(`${resource.kind}s/${encodeURIComponent(resource.id)}`), { headers: { "If-Match": revisionTag(resource.revision) }, signal })); }
  copy(resource: DashboardResource, name: string, operationKey: string, signal?: AbortSignal) { return this.request(signal, () => this.context.http.postJson<DashboardResource>(this.url(`${resource.kind}s/${encodeURIComponent(resource.id)}/copies`), { name, operationKey }, { headers: { "If-Match": revisionTag(resource.revision) }, signal })); }
  history(resource: DashboardResource, signal?: AbortSignal) { return this.request(signal, () => this.context.http.getJson<Page<DefinitionRevision>>(this.url(`${resource.kind}s/${encodeURIComponent(resource.id)}/revisions`), { signal })); }
  revision(resource: DashboardResource, revision: string, signal?: AbortSignal) { return this.request(signal, () => this.context.http.getJson<DashboardResource>(this.url(`${resource.kind}s/${encodeURIComponent(resource.id)}/revisions/${encodeURIComponent(revision)}`), { signal })); }
  restore(resource: DashboardResource, revision: string, signal?: AbortSignal) { return this.request(signal, () => this.context.http.postJson<DashboardResource>(this.url(`${resource.kind}s/${encodeURIComponent(resource.id)}/restorations`), { revision }, { headers: { "If-Match": revisionTag(resource.revision) }, signal })); }
  audience(resource: DashboardResource, signal?: AbortSignal) { return this.request(signal, () => this.context.http.getJson<Page<AudienceGrant>>(this.url(`${resource.kind}s/${encodeURIComponent(resource.id)}/audience`), { signal })); }
  share(resource: DashboardResource, items: AudienceGrant[], signal?: AbortSignal) { return this.request(signal, () => this.context.http.putJson<DashboardResource>(this.url(`${resource.kind}s/${encodeURIComponent(resource.id)}/audience`), { items }, { headers: { "If-Match": revisionTag(resource.revision) }, signal })); }
  candidates(search: string, kind = "user", signal?: AbortSignal, continuationToken?: string | null) {
    const query = new URLSearchParams({ kind, search, pageSize: "25" });
    if (continuationToken) query.set("continuationToken", continuationToken);
    return this.request(signal, () => this.context.http.getJson<Page<AudienceCandidate>>(this.url(`dashboard-audience-candidates?${query}`), { signal }));
  }
  execute(questionId: string, revision: string, signal?: AbortSignal) { return this.request(signal, () => this.context.http.postJson<QueryResult>(this.url(`questions/${encodeURIComponent(questionId)}/execute`), {}, { headers: { "If-Match": revisionTag(revision) }, signal })); }
  tile(dashboard: DashboardResource, tileId: string, questionRevision?: string, signal?: AbortSignal) { return this.request(signal, () => this.context.http.postJson<QueryResult>(this.url(`dashboards/${encodeURIComponent(dashboard.id)}/tiles/${encodeURIComponent(tileId)}/execute`), { questionRevision }, { headers: { "If-Match": revisionTag(dashboard.revision) }, signal })); }
  drill(request: unknown, signal?: AbortSignal) { return this.request(signal, () => this.context.http.postJson<QueryResult>(this.url("dashboard-queries/drill"), request, { signal })); }
  savedDrill(resource: DashboardResource, descriptor: unknown, signal?: AbortSignal, cursor?: string) { return this.request(signal, () => this.context.http.postJson<QueryResult>(this.url(`questions/${encodeURIComponent(resource.id)}/drill`), { descriptor, pageSize: 25, ...(cursor ? { cursor } : {}) }, { headers: { "If-Match": revisionTag(resource.revision) }, signal })); }
  tileDrill(dashboard: DashboardResource, tileId: string, questionRevision: string, descriptor: unknown, signal?: AbortSignal, cursor?: string) { return this.request(signal, () => this.context.http.postJson<QueryResult>(this.url(`dashboards/${encodeURIComponent(dashboard.id)}/tiles/${encodeURIComponent(tileId)}/drill`), { questionRevision, descriptor, pageSize: 25, ...(cursor ? { cursor } : {}) }, { headers: { "If-Match": revisionTag(dashboard.revision) }, signal })); }
}
