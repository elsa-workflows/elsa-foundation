import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  Elsa3ReusableImportAnalysisPage,
  Elsa3ReusableImportReceipt,
  Elsa3ReusableImportSelectionReadiness,
  Elsa3ReusableImportUploadResult
} from "../elsa3ReusableImportTypes";

const root = "migration/elsa3/reusable-activities";

export async function uploadElsa3ReusableCollection(
  context: StudioEndpointContext,
  content: string,
  signal?: AbortSignal
) {
  return context.http.requestJson<Elsa3ReusableImportUploadResult>(`${root}/collections`, {
    method: "POST",
    body: content,
    headers: { "Content-Type": "application/json" },
    signal
  });
}

export async function analyzeElsa3ReusableCollection(
  context: StudioEndpointContext,
  collectionHandle: string,
  offset = 0,
  limit = 100,
  signal?: AbortSignal
) {
  const query = new URLSearchParams({ offset: String(offset), limit: String(limit) });
  return context.http.getJson<Elsa3ReusableImportAnalysisPage>(
    `${root}/collections/${encodeURIComponent(collectionHandle)}/analysis?${query}`,
    signal ? { signal } : undefined
  );
}

export async function expandElsa3ReusableSelection(
  context: StudioEndpointContext,
  collectionHandle: string,
  planId: string,
  selectedSourceVersionIds: string[],
  signal?: AbortSignal
) {
  return context.http.postJson<Elsa3ReusableImportSelectionReadiness>(
    `${root}/collections/${encodeURIComponent(collectionHandle)}/selection`,
    { planId, selectedSourceVersionIds },
    signal ? { signal } : undefined
  );
}

export async function applyElsa3ReusableImport(
  context: StudioEndpointContext,
  collectionHandle: string,
  planId: string,
  selectedSourceVersionIds: string[],
  idempotencyKey: string,
  signal?: AbortSignal
) {
  return context.http.postJson<Elsa3ReusableImportReceipt>(
    `${root}/collections/${encodeURIComponent(collectionHandle)}/apply`,
    { planId, selectedSourceVersionIds, idempotencyKey },
    signal ? { signal } : undefined
  );
}

export async function getElsa3ReusableImportReceipt(
  context: StudioEndpointContext,
  idempotencyKey: string,
  signal?: AbortSignal
) {
  return context.http.getJson<Elsa3ReusableImportReceipt>(
    `${root}/imports/${encodeURIComponent(idempotencyKey)}`,
    signal ? { signal } : undefined
  );
}
