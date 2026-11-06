import { getBackendCapabilities, StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { assertSafeParsedLiterals, parseTriggerResponse, serializeTriggerPayload } from "./json";
import { triggerCapability, type ActorView, type ChangePreview, type ChangeResult, type CreateTriggerRequest, type DeliveryView, type InputMapping, type MappingContext, type MappingPreview, type Page, type SubscriptionView, type TargetSelector, type TargetView, type TriggerCommand, type TriggerTypeDescriptor, type TriggerView } from "./types";

export class TriggerCapabilityError extends Error {}
export function operationId() { return crypto.randomUUID(); }

/** Every path comes from the selected backend's capability document, including custom shell mounts. */
export function triggerClient(context: StudioEndpointContext) {
  async function readResponse<T>(response: Response): Promise<T> {
    const text = await response.text();
    if (!response.ok) {
      let payload: { message?: string; error?: string; detail?: string; title?: string } = {};
      try { payload = JSON.parse(text); } catch { /* A non-JSON error must not expose an HTML body. */ }
      throw new StudioHttpError(response.status, payload.message || payload.error || payload.detail || payload.title || `Trigger request failed (${response.status}).`, null, payload);
    }
    try { return parseTriggerResponse<T>(text); } catch { throw new Error("The backend returned an invalid or unsupported trigger response."); }
  }
  async function route(relation: string, variables: Record<string, string> = {}) {
    const document = await getBackendCapabilities(context);
    const capability = document.capabilities.find(item => item.id === triggerCapability.id);
    if (!capability) throw new TriggerCapabilityError("This backend does not provide standalone triggers.");
    if (capability.contractVersion !== triggerCapability.contractVersion) throw new TriggerCapabilityError(`Standalone triggers require API contract 1; this backend advertises ${capability.contractVersion}.`);
    if (capability.status === "dormant") throw new TriggerCapabilityError(capability.reason || "Standalone triggers are unavailable on this backend.");
    const link = capability.links.find(item => item.rel === relation);
    if (!link) throw new TriggerCapabilityError(`This backend does not provide the ${relation.replaceAll("-", " ")} operation.`);
    if (!link.href || link.href.startsWith("/") || link.href.includes("\\") || /^[a-z][a-z\d+.-]*:/i.test(link.href) || link.href.includes("?") || link.href.includes("#")) throw new TriggerCapabilityError("The backend advertised an invalid trigger operation address.");
    const path = link.href.replace(/\{([^}]+)\}/g, (_match, key: string) => {
      if (!Object.hasOwn(variables, key) || !variables[key] || variables[key] === "." || variables[key] === "..") throw new TriggerCapabilityError("A trigger operation address is incomplete.");
      return encodeURIComponent(variables[key]);
    });
    if (path.split("/").some(segment => !segment || segment === "." || segment === ".." || /%2e|%2f|%5c/i.test(segment))) throw new TriggerCapabilityError("The backend advertised an unsafe trigger operation address.");
    // Studio's authenticated client resolves this path beneath its selected backend's shell base.
    return `/${path}`;
  }
  async function get<T>(relation: string, variables?: Record<string, string>, query?: URLSearchParams, signal?: AbortSignal) {
    const path = await route(relation, variables);
    const url = `${path}${query?.size ? `?${query}` : ""}`;
    if (context.http.requestResponse) return readResponse<T>(await context.http.requestResponse(url, { signal }));
    const result = await context.http.getJson<T>(url, { signal }); assertSafeParsedLiterals(result); return result;
  }
  async function post<T>(relation: string, body: unknown, variables?: Record<string, string>, signal?: AbortSignal) {
    const path = await route(relation, variables);
    const init = { method: "POST", headers: { "content-type": "application/json" }, body: serializeTriggerPayload(body), signal };
    if (context.http.requestResponse) return readResponse<T>(await context.http.requestResponse(path, init));
    const result = await context.http.requestJson<T>(path, init); assertSafeParsedLiterals(result); return result;
  }
  const pageQuery = (filters: Record<string, string | undefined>) => new URLSearchParams(Object.entries({ limit: "25", ...filters }).filter((entry): entry is [string, string] => entry[1] !== undefined && entry[1] !== ""));
  return {
    actor: (signal?: AbortSignal) => get<ActorView>("identity", undefined, undefined, signal),
    types: (signal?: AbortSignal) => get<Page<TriggerTypeDescriptor>>("types", undefined, undefined, signal),
    list: (filters: { search?: string; after?: string; typeKey?: string; state?: string } = {}, signal?: AbortSignal) => get<Page<TriggerView>>("triggers", undefined, pageQuery(filters), signal),
    read: (triggerId: string, signal?: AbortSignal) => get<TriggerView>("trigger", { triggerId }, undefined, signal),
    create: (request: CreateTriggerRequest, signal?: AbortSignal) => post<TriggerView>("triggers", request, undefined, signal),
    subscriptions: (filters: { triggerId?: string; definitionId?: string; slotName?: string; after?: string }, signal?: AbortSignal) => get<Page<SubscriptionView>>("subscriptions", undefined, pageQuery(filters), signal),
    subscription: (subscriptionId: string, signal?: AbortSignal) => get<SubscriptionView>("subscription", { subscriptionId }, undefined, signal),
    targets: (filters: { search?: string; after?: string } = {}, signal?: AbortSignal) => get<Page<TargetView>>("targets", undefined, pageQuery(filters), signal),
    target: (target: TargetSelector, signal?: AbortSignal) => get<TargetView>("target", { definitionId: target.definitionId, slotName: target.slotName }, undefined, signal),
    preview: (triggerId: string, expectedRevision: number, command: TriggerCommand, signal?: AbortSignal) => post<ChangePreview>("change-preview", { expectedRevision, command }, { triggerId }, signal),
    apply: (triggerId: string, expectedRevision: number, command: TriggerCommand, previewId: string, requestId: string, signal?: AbortSignal) => post<ChangeResult>("change-apply", { requestId, previewId, expectedRevision, command }, { triggerId }, signal),
    mappingContext: (triggerId: string, expectedRevision: number, target: TargetSelector, mappings: InputMapping[], signal?: AbortSignal) => post<MappingContext>("mapping-context", { expectedRevision, target, mappings }, { triggerId }, signal),
    mappingPreview: (triggerId: string, expectedRevision: number, target: TargetSelector, mappings: InputMapping[], sample: unknown, contractRevision?: string, signal?: AbortSignal) => post<MappingPreview>("mapping-preview", { expectedRevision, target, mappings, sample, contractRevision }, { triggerId }, signal),
    deliveries: (filters: { triggerId?: string; after?: string } = {}, signal?: AbortSignal) => get<Page<DeliveryView>>("deliveries", undefined, pageQuery(filters), signal),
    delivery: (deliveryId: string, signal?: AbortSignal) => get<DeliveryView>("delivery", { deliveryId }, undefined, signal)
  };
}
export type TriggerClient = ReturnType<typeof triggerClient>;
