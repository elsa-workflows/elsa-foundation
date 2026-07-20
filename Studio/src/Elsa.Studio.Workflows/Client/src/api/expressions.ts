import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ExpressionDescriptor,
  ExpressionDescriptorsResponse,
  VariableTypeDescriptor,
  VariableTypeDescriptorsResponse
} from "../workflowTypes";
import { builtInConversionProfiles, type ConversionProfileReference } from "../conversionSettings";
import { ApiCapabilityUnavailableError, capabilityIds, resolveCapabilityLink } from "./capabilities";

export async function listExpressionDescriptors(context: StudioEndpointContext): Promise<ExpressionDescriptor[]> {
  const path = await resolveCapabilityLink(context, capabilityIds.expressions, "expression-descriptors");
  const response = await context.http.getJson<ExpressionDescriptorsResponse | ExpressionDescriptor[]>(path);
  if (Array.isArray(response)) return response;
  return response.items ?? response.descriptors ?? response.expressionDescriptors ?? [];
}

/**
 * Registered value-conversion profiles for the binding-level Profile picker. The foundation registry
 * (`IValueConversionProfileRegistry.List`) is not exposed over HTTP yet, so when the capability
 * document does not advertise a `conversion-profiles` relation this degrades to the built-in
 * `elsa.json@1`/`elsa.xml@1` profiles — the picker keeps free-form id/version entry either way, so
 * host-registered profiles remain authorable without the listing.
 */
export async function listConversionProfiles(context: StudioEndpointContext): Promise<ConversionProfileReference[]> {
  let path: string;
  try {
    path = await resolveCapabilityLink(context, capabilityIds.expressions, "conversion-profiles");
  } catch (error) {
    if (error instanceof ApiCapabilityUnavailableError) return builtInConversionProfiles;
    throw error;
  }
  const response = await context.http.getJson<{ items?: unknown[] } | unknown[]>(path);
  const items = Array.isArray(response) ? response : response.items ?? [];
  const profiles = items
    .map(item => {
      if (!item || typeof item !== "object") return null;
      const record = item as Record<string, unknown>;
      const profile = record.profile && typeof record.profile === "object" ? record.profile as Record<string, unknown> : record;
      const id = typeof profile.id === "string" ? profile.id.trim() : "";
      const version = typeof profile.version === "string" ? profile.version.trim() : "";
      return id ? { id, version } : null;
    })
    .filter((profile): profile is ConversionProfileReference => profile != null);
  return profiles.length > 0 ? profiles : builtInConversionProfiles;
}

export async function listVariableTypeDescriptors(context: StudioEndpointContext): Promise<VariableTypeDescriptor[]> {
  const path = await resolveCapabilityLink(context, capabilityIds.expressions, "variable-types");
  const response = await context.http.getJson<VariableTypeDescriptorsResponse | VariableTypeDescriptor[]>(path);
  const descriptors = Array.isArray(response) ? response : response.items ?? response.descriptors ?? [];
  return descriptors.filter((descriptor): descriptor is VariableTypeDescriptor => {
    if (!descriptor || typeof descriptor !== "object") return false;
    return typeof descriptor.alias === "string" && descriptor.alias.length > 0;
  });
}
