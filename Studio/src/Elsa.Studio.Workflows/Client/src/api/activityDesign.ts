import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ActivityAvailabilityDiagnostics,
  ActivityAvailabilitySettings,
  ActivityCatalogItem,
  ActivityCatalogResponse,
  SaveActivityAvailabilitySettingsRequest
} from "../workflowTypes";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";

export const activityDesignKeys = {
  all: ["activity-design"] as const,
  catalog: ["activity-design", "catalog"] as const,
  availabilitySettings: ["activity-design", "availability", "settings"] as const,
  availabilityDiagnostics: ["activity-design", "availability", "diagnostics"] as const
};

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
  return context.http.getJson<ActivityAvailabilitySettings>(path);
}

export async function saveActivityAvailabilitySettings(
  context: StudioEndpointContext,
  request: SaveActivityAvailabilitySettingsRequest
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-availability");
  return context.http.putJson<ActivityAvailabilitySettings>(path, request);
}

export async function listActivityAvailabilityDiagnostics(context: StudioEndpointContext) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-availability-diagnostics");
  return context.http.getJson<ActivityAvailabilityDiagnostics>(path);
}
