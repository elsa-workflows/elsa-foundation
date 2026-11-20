import { getBackendCapabilities, resolveBackendPath, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
export const decisionsCapability = [{ id: "elsa.api.decisions", contractVersion: "1", relations: ["decisions"] }];
export type Scalar = { kind: "number" | "string" | "boolean" | "null"; value: string | null };
export type DecisionValue = Scalar | DecisionValue[] | { [key: string]: DecisionValue };
export interface DecisionAsset { id: string; name: string; description?: string | null; revision: string; sourceHash: string; xml: string | null; isSensitive: boolean; publishedVersionId?: string | null; createdAt: string; updatedAt: string }
export interface DecisionVersion { id: string; assetId: string; number: number; name: string; xml: string | null; sourceHash: string; publishedAt: string; isSensitive?: boolean }
export interface Diagnostic { code: string; message: string; severity?: string; decisionId?: string; ruleId?: string | null; clauseId?: string | null }
export interface Validation { success: boolean; diagnostics: Diagnostic[] }
export interface RuleTrace { ruleId: string; matched: boolean; selected: boolean; cells: { inputId: string; truth: string }[] }
export interface Evaluation { status: string; result?: DecisionValue | null; diagnostics: Diagnostic[]; trace: RuleTrace[]; decisionId: string; contentHash: string; profileId: string; semanticsId: string; operations: number }
export interface DecisionCase { id: string; name: string; revision: string; inputs: Record<string, Scalar> | null; expectedStatus: string; expectedResult: DecisionValue | null; isSensitive?: boolean }
export interface Comparison { caseId: string; name: string; passed: boolean; status: string; expected?: unknown; actual?: unknown; failurePath?: string; matchedRules?: string[]; selectedRules?: string[]; trace?: RuleTrace[] }
export interface TestRun { id: string; candidateId: string; candidateHash: string; comparisons?: Comparison[] | null; passed: boolean; hasDetailedPayload: boolean; evaluatedAt: string; completedAt: string }
export interface SaveAsset { name: string; description?: string; xml: string; isSensitive: boolean }
async function base(api: ElsaStudioModuleApi) {
  const document = api.backendSession?.capabilities ?? await getBackendCapabilities(api.backend);
  const capability = document.capabilities.find(item => item.id === "elsa.api.decisions");
  if (!capability || capability.contractVersion !== "1" || capability.status === "dormant") throw new Error(capability?.reason ?? "Business decisions are unavailable on this backend.");
  const link = capability.links.find(item => item.rel === "decisions");
  if (!link || link.templated) throw new Error("This backend does not advertise the Decisions collection.");
  return resolveBackendPath(api.backend.baseUrl, link.href).replace(/\/$/, "");
}
const segment = (value: string) => { if (!value || /[\\/]/.test(value) || Array.from(value).some(character => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127) || value === "." || value === "..") throw new Error("Invalid decision resource identity."); return encodeURIComponent(value); };
async function address(api: ElsaStudioModuleApi, id?: string, suffix = "") { return `${await base(api)}${id ? `/${segment(id)}` : ""}${suffix}`; }
export const operationId = () => crypto.randomUUID();
export const listDecisions = async (api: ElsaStudioModuleApi, signal?: AbortSignal) => (await api.backend.http.getJson<{ items: DecisionAsset[] }>(await address(api), { signal })).items;
export const getDecision = async (api: ElsaStudioModuleApi, id: string, signal?: AbortSignal) => api.backend.http.getJson<DecisionAsset>(await address(api, id), { signal });
export const createDecision = async (api: ElsaStudioModuleApi, input: SaveAsset) => api.backend.http.postJson<DecisionAsset>(await address(api), { operationId: operationId(), ...input });
export const saveDecision = async (api: ElsaStudioModuleApi, asset: DecisionAsset, input: SaveAsset) => api.backend.http.putJson<DecisionAsset>(await address(api, asset.id), { operationId: operationId(), expectedRevision: asset.revision, ...input });
export const validateDecision = async (api: ElsaStudioModuleApi, asset: DecisionAsset) => api.backend.http.postJson<Validation>(await address(api, asset.id, "/validate"), { expectedRevision: asset.revision });
export const publishDecision = async (api: ElsaStudioModuleApi, asset: DecisionAsset) => api.backend.http.postJson<DecisionVersion>(await address(api, asset.id, "/publish"), { operationId: operationId(), expectedRevision: asset.revision });
export const versions = async (api: ElsaStudioModuleApi, id: string) => (await api.backend.http.getJson<{ items: DecisionVersion[] }>(await address(api, id, "/versions"))).items;
export const version = async (api: ElsaStudioModuleApi, id: string, versionId: string) => api.backend.http.getJson<DecisionVersion>(await address(api, id, `/versions/${segment(versionId)}`));
export const cloneVersion = async (api: ElsaStudioModuleApi, asset: DecisionAsset, versionId: string) => api.backend.http.postJson<DecisionAsset>(await address(api, asset.id, "/clone-to-draft"), { operationId: operationId(), expectedRevision: asset.revision, versionId });
export const evaluateDecision = async (api: ElsaStudioModuleApi, id: string, versionId: string, inputs: Record<string, Scalar>) => api.backend.http.postJson<Evaluation>(await address(api, id, "/evaluate"), { versionId, inputs });
export const listCases = async (api: ElsaStudioModuleApi, id: string) => (await api.backend.http.getJson<{ items: DecisionCase[] }>(await address(api, id, "/cases"))).items;
export const saveCase = async (api: ElsaStudioModuleApi, id: string, input: Omit<DecisionCase, "id" | "revision">, existing?: DecisionCase) => {
  const path = await address(api, id, `/cases${existing ? `/${segment(existing.id)}` : ""}`);
  const body = { operationId: operationId(), ...input, ...(existing ? { expectedRevision: existing.revision } : {}) };
  return existing ? api.backend.http.putJson<DecisionCase>(path, body) : api.backend.http.postJson<DecisionCase>(path, body);
};
export const deleteCase = async (api: ElsaStudioModuleApi, id: string, item: DecisionCase) => api.backend.http.deleteJson(await address(api, id, `/cases/${segment(item.id)}?expectedRevision=${encodeURIComponent(item.revision)}&operationId=${operationId()}`));
export const runCases = async (api: ElsaStudioModuleApi, asset: DecisionAsset, items: DecisionCase[], candidate?: DecisionVersion, requestOperationId = operationId()) => api.backend.http.postJson<TestRun>(await address(api, asset.id, "/test"), { operationId: requestOperationId, ...(candidate ? { versionId: candidate.id } : { candidateRevision: asset.revision }), candidateHash: candidate?.sourceHash ?? asset.sourceHash, cases: items.map(item => ({ id: item.id, revision: item.revision })) });
export const history = async (api: ElsaStudioModuleApi, id: string) => (await api.backend.http.getJson<{ items: TestRun[] }>(await address(api, id, "/runs"))).items;
export const getRun = async (api: ElsaStudioModuleApi, id: string, runId: string) => api.backend.http.getJson<TestRun>(await address(api, id, `/runs/${segment(runId)}`));
export const deleteRun = async (api: ElsaStudioModuleApi, id: string, runId: string) => api.backend.http.deleteJson(await address(api, id, `/runs/${segment(runId)}?operationId=${operationId()}`));
export async function exportDecision(api: ElsaStudioModuleApi, id: string, versionId?: string) {
  if (!api.backend.http.requestResponse) throw new Error("This host does not support authenticated decision downloads.");
  const response = await api.backend.http.requestResponse(await address(api, id, `/export${versionId ? `?versionId=${encodeURIComponent(versionId)}` : ""}`));
  if (!response.ok) throw new Error(`Decision export failed (${response.status}).`);
  return response.text();
}
export function can(api: ElsaStudioModuleApi, permission: string) {
  const identity = api.backendSession?.identity;
  return !identity || identity.status !== "authenticated" || identity.permissions.includes(permission) || identity.permissions.includes("*");
}
export const failureMessage = (error: unknown) => error instanceof Error ? error.message : "The operation could not be completed.";
export const isConflict = (error: unknown) => typeof error === "object" && error !== null && "status" in error && error.status === 409;
