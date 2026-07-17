import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ActivityAvailabilityDiagnostics,
  ActivityAvailabilitySettings,
  ActivityCatalogItem,
  ActivityCatalogResponse,
  SaveActivityAvailabilitySettingsRequest
} from "../workflowTypes";
import {
  normalizeActivityAvailabilityDiagnostics,
  normalizeActivityAvailabilitySettings
} from "../activityAvailability";
import type {
  ActivityDefinitionChildCollectionRequest,
  ActivityDefinitionCollectionRequest,
  ActivityAuthoringCapabilities,
  ActivityDraftValidationView,
  ActivityDefinitionDraftView,
  ActivityDefinitionDraftManagementView,
  ActivityDefinitionManagementView,
  ActivityDefinitionVersionManagementView,
  ActivityDefinitionVersionView,
  ActivityManagementPage,
  CreateActivityDefinitionDraftRequest,
  CreateActivityDefinitionRequest,
  CreateActivityDefinitionResponse,
  MigrateActivityDefinitionDraftRequest,
  RecommendedActivityDefinition,
  RecommendedActivityDefinitionPage,
  ReplaceActivityDefinitionDraftRequest
} from "../activityDefinitionTypes";
import {
  ApiCapabilityUnavailableError,
  ApiCapabilityVersionMismatchError,
  capabilityIds,
  resolveCapabilityLink
} from "./capabilities";
import { listExpressionDescriptors } from "./expressions";

export const activityDesignKeys = {
  all: ["activity-design"] as const,
  catalog: ["activity-design", "catalog"] as const,
  availabilitySettings: ["activity-design", "availability", "settings"] as const,
  availabilityDiagnostics: ["activity-design", "availability", "diagnostics"] as const,
  definitionCollections: ["activity-design", "definitions"] as const,
  definitionResources: ["activity-design", "definition"] as const,
  authoringCapabilities: ["activity-design", "authoring-capabilities"] as const,
  recommendedDefinitions: ["activity-design", "recommended-definitions"] as const,
  contractExpressionDescriptors: ["activity-design", "contract-expression-descriptors"] as const,
  definitionCollection: (request: ActivityDefinitionCollectionRequest) => ["activity-design", "definitions", request] as const,
  definition: (definitionId: string) => ["activity-design", "definition", definitionId] as const,
  definitionDrafts: (request: ActivityDefinitionChildCollectionRequest) => ["activity-design", "definition", request.definitionId, "drafts", request] as const,
  definitionDraft: (definitionId: string, draftId: string) => ["activity-design", "definition", definitionId, "draft", draftId] as const,
  fullDefinitionDraft: (draftId: string) => ["activity-design", "authoring-draft", draftId] as const,
  fullDefinitionVersion: (versionId: string) => ["activity-design", "authoring-version", versionId] as const,
  definitionVersions: (request: ActivityDefinitionChildCollectionRequest) => ["activity-design", "definition", request.definitionId, "versions", request] as const,
  definitionVersion: (definitionId: string, versionId: string) => ["activity-design", "definition", definitionId, "version", versionId] as const
};

export type ActivityDefinitionReadFailureKind = "unavailable" | "forbidden" | "not-found" | "expired" | "failed";

export function classifyActivityDefinitionReadFailure(error: unknown): ActivityDefinitionReadFailureKind {
  if (error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError) return "unavailable";
  if (error instanceof StudioHttpError) {
    if (error.status === 401 || error.status === 403) return "forbidden";
    if (error.status === 404) return "not-found";
    if (error.status === 410) return "expired";
  }
  return "failed";
}

export function useActivityDefinitions(context: StudioEndpointContext, request: ActivityDefinitionCollectionRequest) {
  return useQuery({
    queryKey: activityDesignKeys.definitionCollection(request),
    queryFn: ({ signal }) => listActivityDefinitions(context, request, signal),
    placeholderData: previous => previous,
    retry: false
  });
}

export function useActivityAuthoringCapabilities(context: StudioEndpointContext, enabled = true) {
  return useQuery({
    queryKey: activityDesignKeys.authoringCapabilities,
    queryFn: ({ signal }) => getActivityAuthoringCapabilities(context, signal),
    enabled,
    staleTime: 60_000,
    retry: false
  });
}

export function useActivityContractExpressionDescriptors(context: StudioEndpointContext, enabled = true) {
  return useQuery({
    queryKey: activityDesignKeys.contractExpressionDescriptors,
    queryFn: () => listExpressionDescriptors(context),
    enabled,
    staleTime: 60_000,
    retry: false
  });
}

export function useFullActivityDefinitionDraft(context: StudioEndpointContext, draftId: string | null, enabled = true) {
  return useQuery({
    queryKey: activityDesignKeys.fullDefinitionDraft(draftId ?? ""),
    queryFn: ({ signal }) => getActivityDefinitionDraft(context, draftId!, signal),
    enabled: enabled && Boolean(draftId),
    refetchOnMount: "always",
    refetchOnReconnect: false,
    refetchOnWindowFocus: false,
    retry: false
  });
}

export function useFullActivityDefinitionVersion(context: StudioEndpointContext, versionId: string | null, enabled = true) {
  return useQuery({
    queryKey: activityDesignKeys.fullDefinitionVersion(versionId ?? ""),
    queryFn: ({ signal }) => getActivityDefinitionVersion(context, versionId!, signal),
    enabled: enabled && Boolean(versionId),
    staleTime: Infinity,
    retry: false
  });
}

export function useActivityDefinition(context: StudioEndpointContext, definitionId: string) {
  return useQuery({
    queryKey: activityDesignKeys.definition(definitionId),
    queryFn: ({ signal }) => getActivityDefinition(context, definitionId, signal),
    retry: false
  });
}

export function useActivityDefinitionDrafts(context: StudioEndpointContext, request: ActivityDefinitionChildCollectionRequest, enabled = true) {
  return useQuery({
    queryKey: activityDesignKeys.definitionDrafts(request),
    queryFn: ({ signal }) => listActivityDefinitionDrafts(context, request, signal),
    placeholderData: previous => previous,
    enabled,
    retry: false
  });
}

export function useActivityDefinitionVersions(context: StudioEndpointContext, request: ActivityDefinitionChildCollectionRequest, enabled = true) {
  return useQuery({
    queryKey: activityDesignKeys.definitionVersions(request),
    queryFn: ({ signal }) => listActivityDefinitionVersions(context, request, signal),
    placeholderData: previous => previous,
    enabled,
    retry: false
  });
}

export function useActivityDefinitionDraft(context: StudioEndpointContext, definitionId: string, draftId: string | null, enabled = true) {
  return useQuery({
    queryKey: activityDesignKeys.definitionDraft(definitionId, draftId ?? ""),
    queryFn: ({ signal }) => getActivityDefinitionDraftSummary(context, definitionId, draftId!, signal),
    enabled: enabled && Boolean(draftId),
    retry: false
  });
}

export function useActivityDefinitionVersion(context: StudioEndpointContext, definitionId: string, versionId: string | null, enabled = true) {
  return useQuery({
    queryKey: activityDesignKeys.definitionVersion(definitionId, versionId ?? ""),
    queryFn: ({ signal }) => getActivityDefinitionVersionSummary(context, definitionId, versionId!, signal),
    enabled: enabled && Boolean(versionId),
    retry: false
  });
}

export function useWorkflowActivities(context: StudioEndpointContext) {
  return useQuery({
    queryKey: activityDesignKeys.catalog,
    queryFn: () => listActivities(context),
    staleTime: 60_000
  });
}

export function useActivityAvailabilitySettings(context: StudioEndpointContext) {
  return useQuery({
    queryKey: activityDesignKeys.availabilitySettings,
    queryFn: () => getActivityAvailabilitySettings(context)
  });
}

export function useActivityAvailabilityDiagnostics(context: StudioEndpointContext) {
  return useQuery({
    queryKey: activityDesignKeys.availabilityDiagnostics,
    queryFn: () => listActivityAvailabilityDiagnostics(context)
  });
}

export function useSaveActivityAvailabilitySettings(context: StudioEndpointContext) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: SaveActivityAvailabilitySettingsRequest) => saveActivityAvailabilitySettings(context, request),
    onSuccess: saved => {
      queryClient.setQueryData(activityDesignKeys.availabilitySettings, saved);
      void queryClient.invalidateQueries({ queryKey: activityDesignKeys.availabilityDiagnostics });
      void queryClient.invalidateQueries({ queryKey: activityDesignKeys.catalog });
    }
  });
}

export async function listActivities(context: StudioEndpointContext) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-catalog");
  const response = await context.http.getJson<ActivityCatalogResponse>(path);
  return { activities: (response.activities ?? []).map(normalizeActivity) } satisfies ActivityCatalogResponse;
}

export async function listRecommendedActivityDefinitions(context: StudioEndpointContext) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "recommended-activity-definitions");
  const items: RecommendedActivityDefinition[] = [];
  let offset = 0;

  while (true) {
    const parameters = new URLSearchParams({ offset: String(offset), limit: "100" });
    const page = await context.http.getJson<RecommendedActivityDefinitionPage>(`${path}?${parameters.toString()}`);
    items.push(...(Array.isArray(page?.items) ? page.items : []));
    if (typeof page?.nextOffset !== "number") return items;
    if (page.nextOffset <= offset) throw new Error("Recommended Activity Definition pagination did not advance.");
    offset = page.nextOffset;
  }
}

export async function listActivityDefinitions(
  context: StudioEndpointContext,
  request: ActivityDefinitionCollectionRequest,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-definitions");
  const parameters = new URLSearchParams({ limit: String(request.limit), sort: "identity-asc" });
  setOptionalParameter(parameters, "cursor", request.cursor);
  setOptionalParameter(parameters, "search", request.search);
  setOptionalParameter(parameters, "authority", request.authority);
  setOptionalParameter(parameters, "providerKey", request.providerKey);
  return normalizeManagementPage(await context.http.getJson<ActivityManagementPage<ActivityDefinitionManagementView>>(
    `${path}?${parameters.toString()}`,
    { signal }
  ));
}

export async function getActivityDefinition(context: StudioEndpointContext, definitionId: string, signal?: AbortSignal) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-definition", { definitionId });
  return context.http.getJson<ActivityDefinitionManagementView>(path, { signal });
}

export async function getActivityAuthoringCapabilities(context: StudioEndpointContext, signal?: AbortSignal) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-authoring-capabilities");
  return context.http.getJson<ActivityAuthoringCapabilities>(path, { signal });
}

export async function createActivityDefinition(context: StudioEndpointContext, request: CreateActivityDefinitionRequest) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-definitions");
  return context.http.postJson<CreateActivityDefinitionResponse>(path, request);
}

export async function createActivityDefinitionDraft(
  context: StudioEndpointContext,
  definitionId: string,
  request: CreateActivityDefinitionDraftRequest
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-definition-drafts", { definitionId });
  return context.http.postJson<ActivityDefinitionDraftView>(path, request);
}

export async function migrateActivityDefinitionDraft(
  context: StudioEndpointContext,
  draftId: string,
  request: MigrateActivityDefinitionDraftRequest
) {
  const draftPath = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-definition-draft", { draftId });
  return context.http.postJson<ActivityDefinitionDraftView>(`${draftPath}/migrate-provider`, request);
}

export async function getActivityDefinitionDraft(
  context: StudioEndpointContext,
  draftId: string,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-definition-draft", { draftId });
  return context.http.getJson<ActivityDefinitionDraftView>(path, { signal });
}

export async function getActivityDefinitionVersion(
  context: StudioEndpointContext,
  versionId: string,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-definition-version", { versionId });
  return context.http.getJson<ActivityDefinitionVersionView>(path, { signal });
}

export async function replaceActivityDefinitionDraft(
  context: StudioEndpointContext,
  draftId: string,
  request: ReplaceActivityDefinitionDraftRequest
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-definition-draft", { draftId });
  return context.http.putJson<ActivityDefinitionDraftView>(path, request);
}

export async function createActivityDefinitionConflictCopy(
  context: StudioEndpointContext,
  draftId: string,
  request: Omit<ReplaceActivityDefinitionDraftRequest, "expectedRevision"> & { expectedSourceRevision: number }
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-draft-conflict-copies", { draftId });
  return context.http.postJson<ActivityDefinitionDraftView>(path, request);
}

export async function validateActivityDefinitionDraft(
  context: StudioEndpointContext,
  draftId: string,
  expectedRevision: number
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-draft-validation", { draftId });
  return context.http.postJson<ActivityDraftValidationView>(path, { expectedRevision });
}

export async function listActivityDefinitionDrafts(
  context: StudioEndpointContext,
  request: ActivityDefinitionChildCollectionRequest,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-definition-drafts", { definitionId: request.definitionId });
  const parameters = new URLSearchParams({ limit: String(request.limit), sort: "identity-asc" });
  setOptionalParameter(parameters, "cursor", request.cursor);
  setOptionalParameter(parameters, "search", request.search);
  return normalizeManagementPage(await context.http.getJson<ActivityManagementPage<ActivityDefinitionDraftManagementView>>(
    `${path}?${parameters.toString()}`,
    { signal }
  ));
}

export async function listActivityDefinitionVersions(
  context: StudioEndpointContext,
  request: ActivityDefinitionChildCollectionRequest,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-definition-versions", { definitionId: request.definitionId });
  const parameters = new URLSearchParams({ limit: String(request.limit), sort: "identity-asc" });
  setOptionalParameter(parameters, "cursor", request.cursor);
  setOptionalParameter(parameters, "search", request.search);
  return normalizeManagementPage(await context.http.getJson<ActivityManagementPage<ActivityDefinitionVersionManagementView>>(
    `${path}?${parameters.toString()}`,
    { signal }
  ));
}

export async function getActivityDefinitionDraftSummary(
  context: StudioEndpointContext,
  definitionId: string,
  draftId: string,
  signal?: AbortSignal
): Promise<ActivityDefinitionDraftManagementView> {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-definition-draft", { draftId });
  const draft = await context.http.getJson<ReusableActivityDraftSummarySource>(path, { signal });
  if (draft.definitionId !== definitionId) throw new StudioHttpError(404, "Activity draft not found", null);
  return {
    draft: {
      draftId: draft.draftId,
      definitionId: draft.definitionId,
      revision: draft.revision,
      sourceVersionId: draft.sourceVersionId,
      status: draft.status,
      providerKey: draft.provider.providerKey,
      providerSchemaVersion: draft.provider.schemaVersion,
      updatedAt: draft.updatedAt,
      presentationLabel: draft.presentationLabel
    },
    actions: []
  };
}

export async function getActivityDefinitionVersionSummary(
  context: StudioEndpointContext,
  definitionId: string,
  versionId: string,
  signal?: AbortSignal
): Promise<ActivityDefinitionVersionManagementView> {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-definition-version", { versionId });
  const version = await context.http.getJson<ReusableActivityVersionSummarySource>(path, { signal });
  if (version.definition.definitionId !== definitionId) throw new StudioHttpError(404, "Activity version not found", null);
  return {
    version: {
      versionId: version.versionId,
      definitionId: version.definition.definitionId,
      version: version.version,
      lifecycle: version.lifecycle,
      publishedAt: version.publishedAt
    },
    providerKey: version.provider.providerKey,
    providerSchemaVersion: version.provider.schemaVersion,
    isRecommended: version.definition.recommendedVersionId === version.versionId,
    actions: [],
    contract: version.contract
  };
}

export async function redactActivityDefinitionManagementCache(queryClient: QueryClient) {
  await Promise.all([
    queryClient.cancelQueries({ queryKey: activityDesignKeys.definitionCollections }),
    queryClient.cancelQueries({ queryKey: activityDesignKeys.definitionResources })
  ]);
  queryClient.setQueriesData({ queryKey: activityDesignKeys.definitionCollections }, emptyManagementPage);
  queryClient.setQueriesData({ queryKey: activityDesignKeys.definitionResources }, () => null);
}

export async function redactActivityDefinitionChildCache(queryClient: QueryClient, definitionId: string, kind: "draft" | "version") {
  const collectionPrefix = ["activity-design", "definition", definitionId, `${kind}s`] as const;
  const detailPrefix = ["activity-design", "definition", definitionId, kind] as const;
  await Promise.all([
    queryClient.cancelQueries({ queryKey: collectionPrefix }),
    queryClient.cancelQueries({ queryKey: detailPrefix })
  ]);
  queryClient.setQueriesData({ queryKey: collectionPrefix }, emptyManagementPage);
  queryClient.setQueriesData({ queryKey: detailPrefix }, () => null);
}

function normalizeManagementPage<T>(page: ActivityManagementPage<T>): ActivityManagementPage<T> {
  const items = Array.isArray(page?.items) ? page.items : [];
  const totalCount = Number.isFinite(page?.totalCount) ? Math.max(0, page.totalCount) : items.length;
  return {
    items,
    count: items.length,
    totalCount,
    hasMore: page?.hasMore === true && typeof page?.continuation === "string",
    continuation: typeof page?.continuation === "string" ? page.continuation : null,
    snapshot: {
      snapshotId: typeof page?.snapshot?.snapshotId === "string" ? page.snapshot.snapshotId : "",
      asOf: typeof page?.snapshot?.asOf === "string" ? page.snapshot.asOf : new Date(0).toISOString()
    }
  };
}

function setOptionalParameter(parameters: URLSearchParams, name: string, value: string | null | undefined) {
  const normalized = value?.trim();
  if (normalized) parameters.set(name, normalized);
}

function emptyManagementPage() {
  return { items: [], count: 0, totalCount: 0, hasMore: false, continuation: null, snapshot: { snapshotId: "", asOf: new Date(0).toISOString() } };
}

type ReusableActivityDraftSummarySource = {
  draftId: string;
  definitionId: string;
  revision: number;
  sourceVersionId?: string | null;
  status: string;
  provider: { providerKey: string; schemaVersion: string };
  updatedAt: string;
  presentationLabel?: string | null;
};

type ReusableActivityVersionSummarySource = {
  definition: { definitionId: string; recommendedVersionId?: string | null };
  versionId: string;
  version: string;
  lifecycle: string;
  provider: { providerKey: string; schemaVersion: string };
  contract: ActivityDefinitionVersionView["contract"];
  publishedAt: string;
};

function normalizeActivity(activity: ActivityCatalogItem): ActivityCatalogItem {
  const template = activity.authoringTemplate as unknown as {
    nodeId: string;
    activityVersionId: string;
    inputs?: unknown;
    outputs?: unknown;
    structure?: ActivityCatalogItem["authoringTemplate"] extends infer T
      ? T extends { structure?: infer S } ? S : never
      : never;
  } | null | undefined;
  return {
    ...activity,
    category: activity.category ?? "Uncategorized",
    inputs: normalizeProperties(activity.inputs),
    outputs: normalizeProperties(activity.outputs),
    designFacets: activity.designFacets ?? [],
    authoringTemplate: template
      ? {
          ...template,
          inputs: normalizeArguments(template.inputs),
          outputs: normalizeArguments(template.outputs)
        }
      : undefined
  } as ActivityCatalogItem;
}

function normalizeProperties(properties: unknown[]) {
  return (properties ?? []).map(property => {
    if (!property || typeof property !== "object") return property;
    const value = property as Record<string, unknown>;
    return { ...value, typeName: value.typeName ?? value.type };
  });
}

function normalizeArguments(value: unknown) {
  if (Array.isArray(value)) return value;
  if (!value || typeof value !== "object") return [];
  return Object.entries(value as Record<string, unknown>)
    .map(([referenceKey, argument]) => ({ referenceKey, value: argument }));
}

export async function getActivityAvailabilitySettings(context: StudioEndpointContext) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-availability");
  const response = await context.http.getJson<ActivityAvailabilitySettings>(path);
  return normalizeActivityAvailabilitySettings(response);
}

export async function saveActivityAvailabilitySettings(
  context: StudioEndpointContext,
  request: SaveActivityAvailabilitySettingsRequest
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-availability");
  const response = await context.http.putJson<ActivityAvailabilitySettings>(path, request);
  return normalizeActivityAvailabilitySettings(response);
}

export async function listActivityAvailabilityDiagnostics(context: StudioEndpointContext) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-availability-diagnostics");
  const response = await context.http.getJson<ActivityAvailabilityDiagnostics>(path);
  return normalizeActivityAvailabilityDiagnostics(response);
}
